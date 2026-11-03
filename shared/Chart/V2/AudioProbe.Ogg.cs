using System.Buffers.Binary;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// Ogg Vorbis parser. Every page is verified with its own CRC-32 (poly 0x04C11DB7, no reflection,
/// zero initial value), serial/sequence continuity is enforced, the first packet must be a Vorbis
/// identification header, and the duration comes from the final granule position of the logical
/// stream divided by the declared sample rate.
/// <para>Ogg Opus (<c>OpusHead</c>) and other Ogg codecs are rejected; only Vorbis is supported.</para>
/// </summary>
internal static class OggVorbisAudioFormat
{
    private const int PageHeaderBytes = 27;
    private const int VorbisIdentificationBytes = 30;
    private const int MaxChainedStreams = 64;
    private static readonly uint[] CrcTable = BuildCrcTable();

    internal static double Probe(Stream stream, string source)
    {
        var length = stream.Length;
        var position = AudioProbeSupport.SkipId3v2(stream, source);
        var total = 0.0;
        for (var chain = 0; ; chain++)
        {
            if (position >= length)
            {
                if (chain == 0)
                    throw AudioProbeSupport.Reject(source, "Ogg stream is missing its BOS page");
                break;
            }
            if (chain == MaxChainedStreams)
                throw AudioProbeSupport.Reject(source,
                    $"Ogg stream has more than {MaxChainedStreams} chained logical streams");
            total += ReadLogicalStream(stream, source, ref position, length);
        }
        return total;
    }

    /// <summary>Validates one logical stream and returns its duration in seconds.</summary>
    private static double ReadLogicalStream(Stream stream, string source, ref long position,
        long length)
    {
        var first = ReadPage(stream, source, position, length);
        if (!first.IsBos)
            throw AudioProbeSupport.Reject(source,
                $"Ogg page at byte offset {position} is not a beginning-of-stream page");
        var serial = first.Serial;
        var sampleRate = ReadVorbisIdentification(first.Payload, source);
        var nextSequence = first.Sequence + 1u;
        long? lastGranule = first.Granule >= 0 ? first.Granule : null;
        var page = first;
        position = first.NextOffset;

        while (!page.IsEos)
        {
            if (position >= length)
                throw AudioProbeSupport.Reject(source,
                    "Ogg logical stream ends without an end-of-stream page (truncated file)");
            page = ReadPage(stream, source, position, length);
            if (page.Serial != serial)
                throw AudioProbeSupport.Reject(source,
                    $"Ogg page at byte offset {position} belongs to a different logical stream");
            if (page.Sequence != nextSequence)
                throw AudioProbeSupport.Reject(source,
                    $"Ogg page sequence gap at byte offset {position} " +
                    $"(expected {nextSequence}, found {page.Sequence})");
            nextSequence++;
            if (page.Granule >= 0)
                lastGranule = page.Granule;
            position = page.NextOffset;
        }

        if (lastGranule is null)
            throw AudioProbeSupport.Reject(source,
                "Ogg Vorbis stream has no page with a valid granule position");
        return lastGranule.Value / (double)sampleRate;
    }

    private static int ReadVorbisIdentification(byte[] payload, string source)
    {
        if (AudioProbeSupport.StartsWithAscii(payload, "OpusHead"))
            throw AudioProbeSupport.Reject(source, AudioProbeSupport.Unsupported("(.ogg) Ogg Opus"));
        if (payload.Length < VorbisIdentificationBytes)
            throw AudioProbeSupport.Reject(source,
                "Ogg first packet is too short for a Vorbis identification header");
        if (payload[0] != 0x01)
            throw AudioProbeSupport.Reject(source,
                "Ogg first packet is not a Vorbis identification header");
        if (!AudioProbeSupport.StartsWithAscii(payload.AsSpan(1), "vorbis"))
            throw AudioProbeSupport.Reject(source, AudioProbeSupport.Unsupported("non-Vorbis Ogg"));
        var version = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(7));
        if (version != 0)
            throw AudioProbeSupport.Reject(source,
                $"unsupported Vorbis bitstream version {version}");
        var channels = payload[11];
        var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(12));
        if (channels == 0)
            throw AudioProbeSupport.Reject(source,
                "Vorbis identification header declares zero channels");
        if (sampleRate is < 1 or > 192_000)
            throw AudioProbeSupport.Reject(source,
                $"Vorbis identification header declares an invalid sample rate {sampleRate}");
        if ((payload[29] & 0x01) == 0)
            throw AudioProbeSupport.Reject(source,
                "Vorbis identification header is missing its framing bit");
        return (int)sampleRate;
    }

    private sealed record OggPage(
        bool IsBos, bool IsEos, long Granule, uint Serial, uint Sequence, long NextOffset,
        byte[] Payload);

    private static OggPage ReadPage(Stream stream, string source, long offset, long length)
    {
        Span<byte> header = stackalloc byte[PageHeaderBytes];
        if (length - offset < PageHeaderBytes ||
            AudioProbeSupport.ReadAt(stream, offset, header) != PageHeaderBytes)
            throw AudioProbeSupport.Reject(source,
                $"truncated Ogg page header at byte offset {offset}");
        if (!AudioProbeSupport.StartsWithAscii(header, "OggS"))
            throw AudioProbeSupport.Reject(source,
                $"missing Ogg page capture pattern at byte offset {offset}");
        if (header[4] != 0)
            throw AudioProbeSupport.Reject(source,
                $"unsupported Ogg stream structure version {header[4]} at byte offset {offset}");
        var flags = header[5];
        if ((flags & 0xF8) != 0)
            throw AudioProbeSupport.Reject(source,
                $"invalid Ogg page header flags at byte offset {offset}");
        var granule = BinaryPrimitives.ReadInt64LittleEndian(header[6..]);
        var serial = BinaryPrimitives.ReadUInt32LittleEndian(header[14..]);
        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(header[18..]);
        var declaredCrc = BinaryPrimitives.ReadUInt32LittleEndian(header[22..]);
        var segments = header[26];
        if (segments == 0)
            throw AudioProbeSupport.Reject(source,
                $"Ogg page at byte offset {offset} has no segment table entries");

        var payloadLength = 0;
        Span<byte> table = stackalloc byte[255];
        if (AudioProbeSupport.ReadAt(stream, offset + PageHeaderBytes, table[..segments]) != segments)
            throw AudioProbeSupport.Reject(source,
                $"truncated Ogg segment table at byte offset {offset}");
        for (var i = 0; i < segments; i++)
            payloadLength += table[i];

        var pageLength = PageHeaderBytes + segments + payloadLength;
        if (offset + pageLength > length)
            throw AudioProbeSupport.Reject(source,
                $"truncated Ogg page payload at byte offset {offset}; the file is incomplete");
        var pageBytes = new byte[pageLength];
        AudioProbeSupport.ReadExactlyAt(stream, offset, pageBytes, source,
            $"truncated Ogg page at byte offset {offset}");
        var actualCrc = Crc32(pageBytes);
        if (actualCrc != declaredCrc)
            throw AudioProbeSupport.Reject(source,
                $"Ogg page CRC mismatch at byte offset {offset}; the file is corrupt");

        var payload = pageBytes.AsSpan(PageHeaderBytes + segments, payloadLength).ToArray();
        return new OggPage((flags & 0x02) != 0, (flags & 0x04) != 0, granule, serial, sequence,
            offset + pageLength, payload);
    }

    /// <summary>
    /// Ogg CRC-32 (poly 0x04C11DB7, MSB first, init 0, no final XOR) over the whole page with the
    /// four checksum bytes treated as zero, which is how the specification defines the field.
    /// </summary>
    private static uint Crc32(ReadOnlySpan<byte> page)
    {
        var crc = 0u;
        for (var index = 0; index < page.Length; index++)
        {
            var value = index is >= 22 and < 26 ? (byte)0 : page[index];
            crc = (crc << 8) ^ CrcTable[((crc >> 24) & 0xFF) ^ value];
        }
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var i = 0; i < table.Length; i++)
        {
            var value = (uint)i << 24;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 0x8000_0000) != 0 ? (value << 1) ^ 0x04C1_1DB7 : value << 1;
            table[i] = value;
        }
        return table;
    }
}
