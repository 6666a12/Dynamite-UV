using System.Buffers.Binary;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// MPEG-1/2/2.5 Layer I-III (MP3) frame walker. An optional Xing/Info or VBRI header is honoured
/// for frame counts; otherwise every frame is parsed, and consecutive frame offsets must match their
/// declared frame length, which rejects corrupt or non-audio content.
/// </summary>
internal static class Mp3AudioFormat
{
    private const int MaxLeadingSkippedBytes = 1 << 20;

    internal static double Probe(Stream stream, string source)
    {
        var length = stream.Length;
        var searchStart = AudioProbeSupport.SkipId3v2(stream, source);
        Span<byte> buffer = stackalloc byte[4];
        if (!TryFindFirstFrame(stream, source, searchStart, length, buffer, out var firstOffset,
                out var first))
        {
            throw AudioProbeSupport.Reject(source,
                "no valid MPEG audio frame header found (not a usable MP3 stream)");
        }

        var walked = WalkFrames(stream, source, firstOffset, length, first);
        var declared = TryReadVbrDuration(stream, firstOffset, length, first);
        if (declared is null)
            return walked;
        if (declared.Value > walked + 0.5)
            throw AudioProbeSupport.Reject(source,
                $"MP3 VBR header declares {declared.Value:R}s but only {walked:R}s of frames exist");
        // A Xing/VBRI frame count excludes trailing encoder-padding frames, so the declared value
        // may be slightly shorter than the physical frame walk; prefer the shorter of the two.
        return Math.Min(declared.Value, walked);
    }

    private static bool TryFindFirstFrame(Stream stream, string source, long start, long length,
        Span<byte> buffer, out long offset, out AudioProbeSupport.MpegFrame frame)
    {
        offset = start;
        frame = default!;
        var limit = Math.Min(length - 4, start + MaxLeadingSkippedBytes);
        for (var candidate = start; candidate <= limit; candidate++)
        {
            if (AudioProbeSupport.ReadAt(stream, candidate, buffer) != buffer.Length)
                return false;
            if (!AudioProbeSupport.TryParseMpegFrame(buffer, out var parsed))
                continue;
            if (!HasValidSuccessor(stream, source, candidate, parsed.FrameBytes, length, buffer))
                continue;
            offset = candidate;
            frame = parsed;
            return true;
        }
        return false;
    }

    /// <summary>
    /// A frame header is trusted only when the next frame begins exactly where this frame's length
    /// claims, or when the frame is the last block before a known trailing tag or zero padding.
    /// </summary>
    private static bool HasValidSuccessor(Stream stream, string source, long offset, int frameBytes,
        long length, Span<byte> buffer)
    {
        var next = offset + frameBytes;
        if (next > length)
            return false;
        if (next == length)
            return true;
        if (AudioProbeSupport.ReadAt(stream, next, buffer) == buffer.Length &&
            AudioProbeSupport.TryParseMpegFrame(buffer, out var successor))
        {
            return next + successor.FrameBytes <= length;
        }
        return AudioProbeSupport.IsTrailingBlock(stream, source, next, length);
    }

    private static double WalkFrames(Stream stream, string source, long start, long length,
        AudioProbeSupport.MpegFrame first)
    {
        var samples = 0L;
        var position = start;
        var frame = first;
        Span<byte> buffer = stackalloc byte[4];
        while (true)
        {
            var next = position + frame.FrameBytes;
            if (next > length)
                throw AudioProbeSupport.Reject(source,
                    $"truncated MPEG audio frame at byte offset {position}");
            samples += frame.SamplesPerFrame;
            if (next == length)
                break;
            if (AudioProbeSupport.ReadAt(stream, next, buffer) == buffer.Length &&
                AudioProbeSupport.TryParseMpegFrame(buffer, out var successor))
            {
                if (successor.SampleRate != first.SampleRate ||
                    successor.LayerBits != first.LayerBits)
                    throw AudioProbeSupport.Reject(source,
                        $"MPEG audio frame chain changes format at byte offset {next}");
                if (next + successor.FrameBytes > length)
                    throw AudioProbeSupport.Reject(source,
                        $"truncated MPEG audio frame at byte offset {next}");
                position = next;
                frame = successor;
                continue;
            }
            if (AudioProbeSupport.IsTrailingBlock(stream, source, next, length))
                break;
            throw AudioProbeSupport.Reject(source,
                $"corrupt MPEG audio frame chain at byte offset {next}");
        }
        if (samples == 0)
            throw AudioProbeSupport.Reject(source, "MP3 stream contains no audio frames");
        return samples / (double)first.SampleRate;
    }

    /// <summary>
    /// Reads a Xing/Info or VBRI header from the first frame. Returns the declared duration in
    /// seconds, or null when the stream carries no usable VBR header.
    /// </summary>
    private static double? TryReadVbrDuration(Stream stream, long offset, long length,
        AudioProbeSupport.MpegFrame frame)
    {
        if (frame.LayerBits != 1)
            return null;
        // MPEG-1 Layer III carries 32 side-information bytes (17 for mono); MPEG-2/2.5 carries
        // 17 (9 for mono). Layer I/II streams never hold a VBR header in this form.
        var mono = frame.ChannelModeBits == 3;
        var sideInfoBytes = frame.VersionBits == 3 ? (mono ? 17 : 32) : (mono ? 9 : 17);
        var headerOffset = offset + 4 + sideInfoBytes;

        Span<byte> header = stackalloc byte[12];
        if (headerOffset + header.Length <= length &&
            AudioProbeSupport.ReadAt(stream, headerOffset, header) == header.Length &&
            (AudioProbeSupport.StartsWithAscii(header, "Xing") ||
             AudioProbeSupport.StartsWithAscii(header, "Info")))
        {
            var flags = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            if ((flags & 0x01) == 0)
                return null;
            Span<byte> countBytes = stackalloc byte[4];
            if (AudioProbeSupport.ReadAt(stream, headerOffset + 8, countBytes) != countBytes.Length)
                return null;
            var frames = BinaryPrimitives.ReadUInt32BigEndian(countBytes);
            return frames == 0 ? null : frames * (double)frame.SamplesPerFrame / frame.SampleRate;
        }

        const int vbriOffset = 4 + 32;
        if (offset + vbriOffset + 26 > length)
            return null;
        Span<byte> vbri = stackalloc byte[26];
        if (AudioProbeSupport.ReadAt(stream, offset + vbriOffset, vbri) != vbri.Length ||
            !AudioProbeSupport.StartsWithAscii(vbri, "VBRI"))
            return null;
        var vbriFrames = BinaryPrimitives.ReadUInt32BigEndian(vbri[14..]);
        return vbriFrames == 0 ? null : vbriFrames * (double)frame.SamplesPerFrame / frame.SampleRate;
    }
}
