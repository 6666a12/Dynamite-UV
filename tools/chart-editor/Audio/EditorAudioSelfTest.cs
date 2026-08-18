using System.Buffers.Binary;
using DynamiteUniverse.Shared.Chart.V2;
using NAudio.Wave;

namespace DynamiteUniverse.ChartEditor.Audio;

/// <summary>Small dependency-free checks for format admission, WAV probing and injectable writer probing.</summary>
public static class EditorAudioSelfTest
{
    public static void Run()
    {
        CheckRegistry();
        var root = Path.Combine(Environment.CurrentDirectory,
            ".dynamite-universe-editor-audio-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var wav = Path.Combine(root, "tone.wav");
            WriteSilentWave(wav, 48_000, 1, 0.25);
            var result = EditorAudioProbe.Instance.Probe(wav);
            Check(result.Format.Format == EditorAudioFormat.Wave, "WAV probe format");
            Check(Math.Abs(result.Duration.TotalSeconds - 0.25) < 0.001, "WAV probe duration");
            Check(result.SampleRate == 48_000 && result.Channels == 1, "WAV probe stream format");
            var packageInfo = ((IV2PackageAudioProbe)EditorAudioProbe.Instance).Probe(wav, "audio.wav");
            Check(Math.Abs(packageInfo.DurationSeconds - 0.25) < 0.001, "writer audio probe adapter");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckRegistry()
    {
        string[] extensions = [".wav", ".mp3", ".flac", ".ogg", ".opus", ".m4a", ".aac"];
        foreach (var extension in extensions)
            Check(EditorAudioFormatRegistry.TryGet("audio" + extension, out _), "registry " + extension);
        Check(!EditorAudioFormatRegistry.TryGet("audio.xyz", out _), "registry rejects unknown format");
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                _ = EditorAudioFormatRegistry.GetRequired("audio.m4a");
                throw new InvalidOperationException("Media Foundation format was accepted off Windows");
            }
            catch (PlatformNotSupportedException)
            {
            }
        }
    }

    private static void WriteSilentWave(string path, int sampleRate, int channels, double seconds)
    {
        var sampleCount = checked((int)Math.Round(sampleRate * seconds));
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, channels));
        writer.Write(new byte[sampleCount * channels * sizeof(short)], 0,
            sampleCount * channels * sizeof(short));
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("Audio self-test failed: " + label);
    }
}
