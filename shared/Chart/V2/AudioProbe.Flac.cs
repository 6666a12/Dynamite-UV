using System.Buffers.Binary;
using DynamiteUniverse.Shared.Audio;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// Native FLAC parser. Duration comes from STREAMINFO; every audio frame is parsed through its
/// subframes and verified with its header CRC-8 and frame CRC-16, without retaining decoded PCM.
/// </summary>
internal static class FlacAudioFormat
{
    internal const long MaxInputBytes = 64 * 1024 * 1024;
    private const int StreamInfoBytes = 34;
    private const int MaxMetadataBlocks = 4096;
    private const int MaxFrames = 4_000_000;
    private static readonly FlacDecoderOptions ProbeOptions = new()
    {
        MinSampleRate = 1,
        MaxSampleRate = (1 << 20) - 1,
        MaxChannels = 8,
        MaxFrameCount = MaxFrames,
    };

    internal static double Probe(Stream stream, string source)
    {
        var length = stream.Length;
        if (length < 4)
            throw AudioProbeSupport.Reject(source, "invalid FLAC signature (\"fLaC\" required)");
        if (length > MaxInputBytes)
            throw AudioProbeSupport.Reject(source,
                $"FLAC input exceeds the {MaxInputBytes}-byte size limit");
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

    private static void WalkFrames(byte[] bytes, int start, StreamInfo info, string source)
    {
        if (start >= bytes.Length)
            throw AudioProbeSupport.Reject(source,
                "FLAC stream has no audio frames after the metadata blocks");
        if (start + 2 > bytes.Length || bytes[start] != 0xFF ||
            (bytes[start + 1] != 0xF8 && bytes[start + 1] != 0xF9))
            throw AudioProbeSupport.Reject(source,
                $"FLAC stream has no valid audio frame header at byte offset {start}");

        var decoderInfo = new FlacStreamInfo(info.MinBlockSize, info.MaxBlockSize, info.SampleRate,
            info.Channels, info.BitsPerSample, (long)info.TotalSamples, HasMd5: false);
        var reader = new FlacBitReader(bytes.AsSpan(start));
        ulong total = 0;
        for (var frames = 1; ; frames++)
        {
            if (frames > MaxFrames)
                throw AudioProbeSupport.Reject(source, "FLAC frame count is implausible");
            var frameOffset = start + (reader.BitPosition >> 3);
            int blockSize;
            try
            {
                blockSize = FlacFrameWalker.ReadBlockSize(ref reader, decoderInfo, ProbeOptions);
            }
            catch (FlacDecodeException exception)
            {
                throw AudioProbeSupport.Reject(source,
                    $"invalid FLAC frame at byte offset {frameOffset}: {exception.Message}", exception);
            }
            total += (ulong)blockSize;
            if (total > info.TotalSamples)
                throw AudioProbeSupport.Reject(source,
                    $"FLAC frame at byte offset {frameOffset} overruns the declared sample count");
            if (total == info.TotalSamples)
                return;
            if (reader.BitsRemaining == 0)
                throw AudioProbeSupport.Reject(source,
                    $"FLAC stream ends after {total} of {info.TotalSamples} declared samples " +
                    "(truncated file)");
        }
    }
}
