using DynamiteUniverse.Shared.Chart.V2;
using NAudio.Flac;
using NAudio.Vorbis;
using NAudio.Wave;
using NLayer;

namespace DynamiteUniverse.ChartEditor.Audio;

public sealed record EditorAudioProbeResult(
    EditorAudioFormatDescriptor Format,
    TimeSpan Duration,
    int SampleRate,
    int Channels);

public interface IEditorAudioProbe : IV2PackageAudioProbe
{
    EditorAudioProbeResult Probe(string absolutePath);
}

/// <summary>Validates editor audio with the same decoder family used for playback.</summary>
public sealed class EditorAudioProbe : IEditorAudioProbe
{
    public static EditorAudioProbe Instance { get; } = new();

    public EditorAudioProbeResult Probe(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        var fullPath = Path.GetFullPath(absolutePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Audio file does not exist.", fullPath);
        var format = EditorAudioFormatRegistry.GetRequired(fullPath);
        try
        {
            var result = format.Format switch
            {
                EditorAudioFormat.Wave => ProbeWave(fullPath, format),
                EditorAudioFormat.Mp3 => ProbeMp3(fullPath, format),
                EditorAudioFormat.Flac => ProbeFlac(fullPath, format),
                EditorAudioFormat.OggVorbis => ProbeVorbis(fullPath, format),
                EditorAudioFormat.OggOpus => OpusWaveStream.Probe(fullPath, format),
                EditorAudioFormat.M4aAac or EditorAudioFormat.RawAac =>
                    ProbeMediaFoundation(fullPath, format),
                _ => throw new NotSupportedException($"No decoder is registered for {format.DisplayName}."),
            };
            Validate(result);
            ValidateCanDecode(fullPath);
            return result;
        }
        catch (Exception exception) when (exception is not FileNotFoundException and
            not PlatformNotSupportedException and not NotSupportedException)
        {
            throw new InvalidDataException(
                $"{format.DisplayName} audio could not be decoded: {exception.Message}", exception);
        }
    }

    V2PackageAudioInfo IV2PackageAudioProbe.Probe(string absolutePath, string packageRelativePath)
    {
        var result = Probe(absolutePath);
        return new V2PackageAudioInfo(result.Duration.TotalSeconds);
    }

    private static EditorAudioProbeResult ProbeWave(string path,
        EditorAudioFormatDescriptor format)
    {
        using var reader = new WaveFileReader(path);
        ValidateWavePcm(reader.WaveFormat);
        return Result(format, reader.TotalTime, reader.WaveFormat);
    }

    private static EditorAudioProbeResult ProbeMp3(string path,
        EditorAudioFormatDescriptor format)
    {
        using var reader = new MpegFile(path);
        if (!reader.CanSeek || reader.Length <= 0)
            throw new InvalidDataException("MP3 decoder could not determine a seekable duration.");
        return new EditorAudioProbeResult(format, reader.Duration, reader.SampleRate, reader.Channels);
    }

    private static EditorAudioProbeResult ProbeFlac(string path,
        EditorAudioFormatDescriptor format)
    {
        using var reader = new FlacReader(path);
        if (!reader.CanSeek || reader.Length <= 0)
            throw new InvalidDataException("FLAC decoder could not determine a seekable duration.");
        return Result(format, reader.TotalTime, reader.WaveFormat);
    }

    private static EditorAudioProbeResult ProbeVorbis(string path,
        EditorAudioFormatDescriptor format)
    {
        using var reader = new VorbisWaveReader(path);
        if (!reader.CanSeek || reader.Length <= 0)
            throw new InvalidDataException("Vorbis decoder could not determine a seekable duration.");
        return Result(format, reader.TotalTime, reader.WaveFormat);
    }

    private static EditorAudioProbeResult ProbeMediaFoundation(string path,
        EditorAudioFormatDescriptor format)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                $"{format.DisplayName} requires Windows Media Foundation; this platform fails closed.");
        using var reader = new MediaFoundationReader(path);
        if (!reader.CanSeek || reader.Length <= 0)
            throw new InvalidDataException("Media Foundation could not determine a seekable duration.");
        return Result(format, reader.TotalTime, reader.WaveFormat);
    }

    private static EditorAudioProbeResult Result(EditorAudioFormatDescriptor format,
        TimeSpan duration, WaveFormat waveFormat) =>
        new(format, duration, waveFormat.SampleRate, waveFormat.Channels);

    private static void ValidateCanDecode(string path)
    {
        using var stream = EditorWaveStreamFactory.Open(path);
        var buffer = new byte[Math.Max(stream.WaveFormat.BlockAlign, 4096)];
        var read = stream.Read(buffer, 0, buffer.Length);
        if (read <= 0)
            throw new InvalidDataException("Audio decoder produced no samples.");
    }

    private static void Validate(EditorAudioProbeResult result)
    {
        if (!double.IsFinite(result.Duration.TotalSeconds) || result.Duration <= TimeSpan.Zero)
            throw new InvalidDataException("Decoded audio duration must be finite and positive.");
        if (result.SampleRate <= 0 || result.Channels <= 0)
            throw new InvalidDataException("Decoded audio format has invalid channels or sample rate.");
    }

    internal static void ValidateWavePcm(WaveFormat format)
    {
        if (format.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat) ||
            format.SampleRate <= 0 || format.Channels <= 0 || format.AverageBytesPerSecond <= 0)
            throw new InvalidDataException("WAVE audio must contain PCM or IEEE-float samples.");
    }
}
