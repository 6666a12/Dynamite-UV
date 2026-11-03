
namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// Built-in staged-audio probe used by the v2 package writer when no host probe is supplied.
/// <para>
/// Each supported extension selects one real container/frame parser that validates the stream
/// structure and derives the duration from the stream itself: RIFF/WAVE (PCM or IEEE float),
/// MPEG-1/2/2.5 Layer I-III (MP3), native FLAC (STREAMINFO) and Ogg Vorbis (page checksums, serial,
/// identification header and final granule). A file is never accepted on its extension alone.
/// AAC/ADTS, MP4/M4A and Ogg Opus are not decoded and are rejected with an explicit reason.
/// No decoding of audio samples happens, so duration accuracy is limited to the container's own
/// timing data (plus one encoder padding frame for CBR-MP3 Xing headers).
/// </para>
/// </summary>
public sealed class V2BuiltInAudioProbe : IV2PackageAudioProbe
{
    /// <summary>Shared stateless instance; it holds no per-file state.</summary>
    public static V2BuiltInAudioProbe Instance { get; } = new();

    private V2BuiltInAudioProbe()
    {
    }

    public V2PackageAudioInfo Probe(string absolutePath, string packageRelativePath) =>
        ProbeFile(absolutePath, packageRelativePath);

    /// <summary>
    /// Validates one staged audio file and returns its duration. Failures raise
    /// <see cref="V2DiagnosticException"/> naming the package-relative path and the reason.
    /// </summary>
    public static V2PackageAudioInfo ProbeFile(string absolutePath, string packageRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRelativePath);
        var extension = Path.GetExtension(packageRelativePath).ToLowerInvariant();
        using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1 << 16);
        if (stream.Length == 0)
            throw AudioProbeSupport.Reject(packageRelativePath, "audio file is empty");

        var duration = extension switch
        {
            ".wav" => WaveAudioFormat.Probe(stream, packageRelativePath),
            ".mp3" => Mp3AudioFormat.Probe(stream, packageRelativePath),
            ".flac" => FlacAudioFormat.Probe(stream, packageRelativePath),
            ".ogg" => OggVorbisAudioFormat.Probe(stream, packageRelativePath),
            _ => throw AudioProbeSupport.Reject(packageRelativePath,
                AudioProbeSupport.Unsupported(extension switch
                {
                    ".opus" => "(.opus) Ogg Opus",
                    ".m4a" or ".aac" => $"{extension} (AAC/MP4)",
                    _ => $"'{extension}'",
                })),
        };
        return AudioProbeSupport.Finish(packageRelativePath, duration);
    }
}

/// <summary>Low-level helpers shared by the built-in audio format parsers.</summary>
internal static class AudioProbeSupport
{
    internal const string SupportedFormats =
        "the built-in writer probe supports .wav (PCM/IEEE float), .mp3 (MPEG Layer I-III), " +
        ".flac (native FLAC) and .ogg (Ogg Vorbis)";

    internal static string Unsupported(string what) =>
        $"{what} audio is not supported; {SupportedFormats}";

    internal static V2DiagnosticException Reject(string source, string reason,
        Exception? innerException = null) => new(source, "/", reason, innerException);

    internal static V2PackageAudioInfo Finish(string source, double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw Reject(source, "audio stream declares a non-positive or invalid duration");
        return new V2PackageAudioInfo(durationSeconds);
    }

    /// <summary>Reads at most <paramref name="buffer"/>.Length bytes; returns the byte count.</summary>
    internal static int ReadAt(Stream stream, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset >= stream.Length)
            return 0;
        stream.Position = offset;
        var wanted = (int)Math.Min(buffer.Length, stream.Length - offset);
        return stream.ReadAtLeast(buffer[..wanted], wanted, throwOnEndOfStream: false);
    }

    internal static void ReadExactlyAt(Stream stream, long offset, Span<byte> buffer,
        string source, string reason)
    {
        if (ReadAt(stream, offset, buffer) != buffer.Length)
            throw Reject(source, reason);
    }

    internal static bool AllZero(Stream stream, long offset, long end)
    {
        Span<byte> buffer = stackalloc byte[512];
        for (var position = offset; position < end; position += buffer.Length)
        {
            var wanted = (int)Math.Min(buffer.Length, end - position);
            if (ReadAt(stream, position, buffer[..wanted]) != wanted)
                return false;
            foreach (var value in buffer[..wanted])
            {
                if (value != 0)
                    return false;
            }
        }
        return true;
    }

    internal static bool StartsWithAscii(ReadOnlySpan<byte> bytes, string prefix)
    {
        if (bytes.Length < prefix.Length)
            return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != (byte)prefix[i])
                return false;
        }
        return true;
    }


    /// <summary>
    /// Returns the byte offset of the first audio payload after an optional leading ID3v2 tag.
    /// Several consecutive tags are tolerated because taggers chain them.
    /// </summary>
    internal static long SkipId3v2(Stream stream, string source, long start = 0)
    {
        var position = start;
        Span<byte> header = stackalloc byte[10];
        for (var tags = 0; tags < 4; tags++)
        {
            if (stream.Length - position < header.Length ||
                ReadAt(stream, position, header) != header.Length ||
                !StartsWithAscii(header, "ID3"))
                return position;
            if (header[3] < 2 || header[3] > 4 || header[4] == 0xFF ||
                (header[6] | header[7] | header[8] | header[9]) >= 0x80)
                throw Reject(source, $"invalid ID3v2 tag header at byte offset {position}");
            var size = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) |
                ((header[8] & 0x7F) << 7) | (header[9] & 0x7F);
            long total = 10L + size + ((header[5] & 0x10) != 0 ? 10 : 0);
            if (position + total > stream.Length)
                throw Reject(source, $"truncated ID3v2 tag at byte offset {position}");
            position += total;
        }
        return position;
    }

    // ---- MPEG audio (MP3) header tables -------------------------------------------------

    private static readonly int[] Mpeg1SampleRates = [44100, 48000, 32000];
    private static readonly int[] Mpeg2SampleRates = [22050, 24000, 16000];
    private static readonly int[] Mpeg25SampleRates = [11025, 12000, 8000];

    private static readonly int[] Layer1Bitrates =
        [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448];
    private static readonly int[] Layer2Bitrates =
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384];
    private static readonly int[] Layer3Bitrates =
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] LowRateLayer1Bitrates =
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256];
    private static readonly int[] LowRateLayer23Bitrates =
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];

    /// <summary>One parsed MPEG audio frame header.</summary>
    internal readonly record struct MpegFrame(
        int VersionBits, int LayerBits, int ChannelModeBits, int SampleRate, int FrameBytes,
        int SamplesPerFrame);

    /// <summary>
    /// Parses a 4-byte MPEG audio frame header. Returns false when the bytes are not a usable
    /// frame header (no sync, reserved version/layer/rate, or free-format bitrate).
    /// </summary>
    internal static bool TryParseMpegFrame(ReadOnlySpan<byte> bytes, out MpegFrame frame)
    {
        frame = default;
        if (bytes.Length < 4 || bytes[0] != 0xFF || (bytes[1] & 0xE0) != 0xE0)
            return false;
        var version = (bytes[1] >> 3) & 0x03;
        var layer = (bytes[1] >> 1) & 0x03;
        if (version == 1 || layer == 0)
            return false;
        var bitrateIndex = bytes[2] >> 4;
        var sampleRateIndex = (bytes[2] >> 2) & 0x03;
        var padding = (bytes[2] >> 1) & 0x01;
        var channelMode = (bytes[3] >> 6) & 0x03;
        if (bitrateIndex is 0 or 15 || sampleRateIndex == 3)
            return false;

        var rates = version switch
        {
            3 => Mpeg1SampleRates,
            2 => Mpeg2SampleRates,
            _ => Mpeg25SampleRates,
        };
        var bitrates = (version, layer) switch
        {
            (3, 3) => Layer1Bitrates,
            (3, 2) => Layer2Bitrates,
            (3, 1) => Layer3Bitrates,
            (_, 3) => LowRateLayer1Bitrates,
            _ => LowRateLayer23Bitrates,
        };
        var sampleRate = rates[sampleRateIndex];
        var bitrate = bitrates[bitrateIndex];
        var samplesPerFrame = layer switch
        {
            3 => 384,
            2 => 1152,
            _ => version == 3 ? 1152 : 576,
        };
        var frameBytes = layer == 3
            ? (12L * bitrate * 1000 / sampleRate + padding) * 4
            : samplesPerFrame / 8L * bitrate * 1000 / sampleRate + padding;
        if (frameBytes < 4 || frameBytes > 1 << 17)
            return false;
        frame = new MpegFrame(version, layer, channelMode, sampleRate, (int)frameBytes,
            samplesPerFrame);
        return true;
    }

    /// <summary>
    /// Accepts a well-known trailing block (ID3v1, ID3v2, APE tag) or zero padding at
    /// <paramref name="offset"/>. Used to terminate frame walking without accepting junk.
    /// </summary>
    internal static bool IsTrailingBlock(Stream stream, string source, long offset, long length)
    {
        var remaining = length - offset;
        if (remaining <= 0)
            return true;
        Span<byte> head = stackalloc byte[10];
        var read = ReadAt(stream, offset, head[..(int)Math.Min(head.Length, remaining)]);
        head = head[..read];
        if (StartsWithAscii(head, "TAG") && remaining >= 128)
            return true;
        if (StartsWithAscii(head, "APETAGEX"))
            return true;
        if (StartsWithAscii(head, "ID3"))
        {
            try
            {
                return SkipId3v2(stream, source, offset) >= length;
            }
            catch (V2DiagnosticException)
            {
                return false;
            }
        }
        return AllZero(stream, offset, length);
    }
}
