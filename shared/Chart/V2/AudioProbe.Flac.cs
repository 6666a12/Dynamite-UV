using System.Buffers.Binary;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// Native FLAC parser. Duration comes from the mandatory STREAMINFO metadata block; the file is
/// additionally proven structurally complete by walking every audio frame header in file order
/// (sync code, block size, sample rate, channel assignment, bit depth and the header CRC-8) and
/// requiring the summed block sizes to equal the declared total sample count. Truncated, padded or
/// foreign payloads therefore fail instead of silently reporting the STREAMINFO duration.
/// </summary>
internal static class FlacAudioFormat
{
    private const int StreamInfoBytes = 34;
    private const int MaxMetadataBlocks = 4096;
    private const int MaxFrames = 4_000_000;

    private static readonly int[] BlockSizeTable =
        [0, 192, 576, 1152, 2304, 4608, 0, 0, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768];

    private static readonly int[] SampleRateTable =
        [0, 88200, 176400, 192000, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 96000, 0, 0, 0, 0];

    internal static double Probe(Stream stream, string source)
    {
        var length = stream.Length;
        if (length < 4)
            throw AudioProbeSupport.Reject(source, "invalid FLAC signature (\"fLaC\" required)");
        var bytes = new byte[length];
        AudioProbeSupport.ReadExactlyAt(stream, 0, bytes, source, "truncated FLAC file");
        if (!AudioProbeSupport.StartsWithAscii(bytes, "fLaC"))
            throw AudioProbeSupport.Reject(source, "invalid FLAC signature (\"fLaC\" required)");

        var position = 4;
        StreamInfo? info = null;
        for (var blocks = 0; ; blocks++)
        {
            if (blocks >= MaxMetadataBlocks)
                throw AudioProbeSupport.Reject(source, "FLAC metadata block count is implausible");
            if (position + 4 > bytes.Length)
                throw AudioProbeSupport.Reject(source,
                    "truncated FLAC metadata header before the audio frames");
            var isLast = (bytes[position] & 0x80) != 0;
            var type = bytes[position] & 0x7F;
            var blockLength = (bytes[position + 1] << 16) | (bytes[position + 2] << 8) |
                bytes[position + 3];
            var payload = position + 4;
            if (payload + blockLength > bytes.Length)
                throw AudioProbeSupport.Reject(source,
                    $"truncated FLAC metadata block at byte offset {position}");
            if (type == 127)
                throw AudioProbeSupport.Reject(source,
                    $"invalid FLAC metadata block type 127 at byte offset {position}");
            if (type == 0)
            {
                if (info is not null)
                    throw AudioProbeSupport.Reject(source, "duplicate FLAC STREAMINFO block");
                if (blocks != 0)
                    throw AudioProbeSupport.Reject(source,
                        "FLAC STREAMINFO must be the first metadata block");
                if (blockLength < StreamInfoBytes)
                    throw AudioProbeSupport.Reject(source, "invalid FLAC STREAMINFO block length");
                info = ReadStreamInfo(bytes.AsSpan(payload, StreamInfoBytes), source);
            }
            position = payload + blockLength;
            if (isLast)
                break;
        }

        if (info is null)
            throw AudioProbeSupport.Reject(source, "FLAC STREAMINFO block is missing");
        WalkFrames(bytes, position, info, source);
        return info.TotalSamples / (double)info.SampleRate;
    }

    private sealed record StreamInfo(int SampleRate, int Channels, int BitsPerSample,
        int MinBlockSize, int MaxBlockSize, ulong TotalSamples);

    private static StreamInfo ReadStreamInfo(ReadOnlySpan<byte> info, string source)
    {
        var minBlockSize = BinaryPrimitives.ReadUInt16BigEndian(info);
        var maxBlockSize = BinaryPrimitives.ReadUInt16BigEndian(info[2..]);
        if (minBlockSize < 16 || maxBlockSize < minBlockSize)
            throw AudioProbeSupport.Reject(source, "invalid FLAC STREAMINFO block sizes");

        var packed = BinaryPrimitives.ReadUInt64BigEndian(info[10..]);
        var sampleRate = (int)(packed >> 44);
        var channels = (int)((packed >> 41) & 0x07) + 1;
        var bitsPerSample = (int)((packed >> 36) & 0x1F) + 1;
        var totalSamples = packed & 0x0F_FFFF_FFFFUL;
        if (sampleRate == 0)
            throw AudioProbeSupport.Reject(source, "FLAC STREAMINFO declares a zero sample rate");
        if (bitsPerSample < 4)
            throw AudioProbeSupport.Reject(source, "FLAC STREAMINFO declares invalid bit depth");
        if (totalSamples == 0)
            throw AudioProbeSupport.Reject(source,
                "FLAC STREAMINFO declares zero total samples; the stream has no known duration");
        return new StreamInfo(sampleRate, channels, bitsPerSample, minBlockSize, maxBlockSize,
            totalSamples);
    }

    /// <summary>
    /// Walks the audio frames in file order. The first frame must start exactly where the metadata
    /// ends; each following frame is located by scanning forward for the next CRC-8-valid header.
    /// The walk is accepted only when the summed block sizes equal the declared sample count.
    /// </summary>
    private static void WalkFrames(byte[] bytes, int start, StreamInfo info, string source)
    {
        if (start >= bytes.Length)
            throw AudioProbeSupport.Reject(source,
                "FLAC stream has no audio frames after the metadata blocks");
        if (!TryReadFrameHeader(bytes, start, info, out var headerEnd, out var blockSize))
            throw AudioProbeSupport.Reject(source,
                $"FLAC stream has no valid audio frame header at byte offset {start}");

        ulong total = 0;
        var frames = 0;
        var frameOffset = start;
        while (true)
        {
            if (++frames > MaxFrames)
                throw AudioProbeSupport.Reject(source, "FLAC frame count is implausible");
            total += (ulong)blockSize;
            if (total > info.TotalSamples)
                throw AudioProbeSupport.Reject(source,
                    $"FLAC frame at byte offset {frameOffset} overruns the declared sample count");
            if (total == info.TotalSamples)
                return;
            if (headerEnd >= bytes.Length)
                throw AudioProbeSupport.Reject(source,
                    $"FLAC stream ends after {total} of {info.TotalSamples} declared samples " +
                    "(truncated file)");
            var next = FindFrameHeader(bytes, headerEnd, info);
            if (next < 0)
                throw AudioProbeSupport.Reject(source,
                    $"FLAC audio data ends after {total} of {info.TotalSamples} declared samples " +
                    $"(truncated or corrupt stream at byte offset {headerEnd})");
            frameOffset = next;
            if (!TryReadFrameHeader(bytes, next, info, out headerEnd, out blockSize))
                throw AudioProbeSupport.Reject(source,
                    $"invalid FLAC frame header at byte offset {next}");
        }
    }

    private static int FindFrameHeader(byte[] bytes, int from, StreamInfo info)
    {
        for (var candidate = from; candidate + 6 <= bytes.Length; candidate++)
        {
            if (bytes[candidate] != 0xFF || (bytes[candidate + 1] & 0xFE) != 0xF8)
                continue;
            if (TryReadFrameHeader(bytes, candidate, info, out _, out _))
                return candidate;
        }
        return -1;
    }

    /// <summary>
    /// Parses and CRC-8-verifies one frame header. Returns the offset just past the header and the
    /// number of samples the frame block carries.
    /// </summary>
    private static bool TryReadFrameHeader(byte[] bytes, int offset, StreamInfo info,
        out int headerEnd, out int blockSize)
    {
        headerEnd = 0;
        blockSize = 0;
        if (offset + 6 > bytes.Length)
            return false;
        if (bytes[offset] != 0xFF || (bytes[offset + 1] & 0xFE) != 0xF8)
            return false;
        // Reserved bit after the blocking-strategy bit must be 0, and the sync code is 14 bits.
        if (bytes[offset + 1] != 0xF8 && bytes[offset + 1] != 0xF9)
            return false;

        var cursor = offset + 2;
        var blockSizeCode = bytes[cursor] >> 4;
        var sampleRateCode = bytes[cursor] & 0x0F;
        cursor++;
        var channelAssignment = bytes[cursor] >> 4;
        var sampleSizeCode = (bytes[cursor] >> 1) & 0x07;
        if ((bytes[cursor] & 0x01) != 0 || blockSizeCode == 0 || sampleRateCode == 0x0F ||
            channelAssignment > 10 || sampleSizeCode is 3 or 7)
            return false;
        cursor++;

        var channels = channelAssignment <= 7 ? channelAssignment + 1 : 2;
        if (channels != info.Channels)
            return false;
        var depth = sampleSizeCode switch
        {
            0 => info.BitsPerSample,
            1 => 8,
            2 => 12,
            4 => 16,
            5 => 20,
            6 => 24,
            _ => 32,
        };
        if (depth != info.BitsPerSample)
            return false;

        // UTF-8-style coded frame or sample number: 1 to 7 bytes.
        var lead = bytes[cursor];
        cursor++;
        var continuation = lead switch
        {
            < 0x80 => 0,
            >= 0xC0 and < 0xE0 => 1,
            >= 0xE0 and < 0xF0 => 2,
            >= 0xF0 and < 0xF8 => 3,
            >= 0xF8 and < 0xFC => 4,
            >= 0xFC and < 0xFE => 5,
            _ => -1,
        };
        if (continuation < 0 || cursor + continuation > bytes.Length)
            return false;
        for (var i = 0; i < continuation; i++)
        {
            if ((bytes[cursor + i] & 0xC0) != 0x80)
                return false;
        }
        cursor += continuation;

        switch (blockSizeCode)
        {
            case 6:
                if (cursor + 1 > bytes.Length)
                    return false;
                blockSize = bytes[cursor] + 1;
                cursor += 1;
                break;
            case 7:
                if (cursor + 2 > bytes.Length)
                    return false;
                blockSize = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(cursor, 2)) + 1;
                cursor += 2;
                break;
            default:
                blockSize = BlockSizeTable[blockSizeCode];
                break;
        }
        if (blockSize <= 0 || blockSize > info.MaxBlockSize)
            return false;

        var sampleRate = sampleRateCode switch
        {
            0 => 0,
            12 => -1,
            13 => -1,
            14 => -1,
            _ => SampleRateTable[sampleRateCode],
        };
        switch (sampleRateCode)
        {
            case 12:
                if (cursor + 1 > bytes.Length)
                    return false;
                sampleRate = bytes[cursor] * 1000;
                cursor += 1;
                break;
            case 13:
                if (cursor + 2 > bytes.Length)
                    return false;
                sampleRate = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(cursor, 2));
                cursor += 2;
                break;
            case 14:
                if (cursor + 2 > bytes.Length)
                    return false;
                sampleRate = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(cursor, 2)) * 10;
                cursor += 2;
                break;
        }
        // Sample rate code 0 means "same as STREAMINFO"; anything else must match it.
        if (sampleRate != 0 && sampleRate != info.SampleRate)
            return false;

        if (cursor >= bytes.Length)
            return false;
        if (Crc8(bytes.AsSpan(offset, cursor - offset)) != bytes[cursor])
            return false;
        headerEnd = cursor + 1;
        return true;
    }

    /// <summary>FLAC header CRC-8: polynomial x^8 + x^2 + x + 1 (0x07), initial value 0.</summary>
    private static byte Crc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x07) : (byte)(crc << 1);
        }
        return crc;
    }
}
