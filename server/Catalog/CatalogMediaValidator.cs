using System.Diagnostics;
using System.Text.Json;
using DynamiteUniverse.Shared.Audio;

namespace DynamiteUniverse.Server.Catalog;

/// <summary>Operator-installed FFmpeg, no shell or remote protocols. Publication fails closed when unavailable.</summary>
internal static class CatalogMediaValidator
{
    internal const string Profile = "desktop-mobile-media-1";
    internal static void Validate(string path, bool image, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try { ValidateCore(path, image, ct); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or OverflowException)
        { throw new InvalidDataException("Unsupported media metadata", ex); }
    }
    private static void ValidateCore(string path, bool image, CancellationToken ct)
    {
        if (image && new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Cover exceeds budget");
        var json = Run("ffprobe", ["-v", "error", "-protocol_whitelist", "file", "-show_streams", "-of", "json", path], 64 * 1024, ct);
        using var document = JsonDocument.Parse(json);
        var streams = document.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() != 1) throw new InvalidDataException("Single media stream required");
        var stream = streams[0];
        var codec = stream.GetProperty("codec_name").GetString();
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (image)
        {
            if (!((extension == ".png" && codec == "png") || (extension is ".jpg" or ".jpeg" && codec == "mjpeg") ||
                (extension == ".webp" && codec == "webp"))) throw new InvalidDataException("Unsupported cover codec");
            var width = stream.GetProperty("width").GetInt32(); var height = stream.GetProperty("height").GetInt32();
            if (width is < 1 or > 4096 || height is < 1 or > 4096 || (long)width * height > 16_000_000)
                throw new InvalidDataException("Cover dimensions exceed budget");
        }
        else
        {
            if (!((extension == ".wav" && codec is "pcm_s16le" or "pcm_u8" or "pcm_f32le") ||
                (extension == ".mp3" && codec == "mp3") || (extension == ".ogg" && codec == "vorbis") ||
                (extension == ".flac" && codec == "flac"))) throw new InvalidDataException("Unsupported audio codec");
            var channels = stream.GetProperty("channels").GetInt32();
            var rate = int.Parse(stream.GetProperty("sample_rate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var duration = double.Parse(stream.GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            if (channels is < 1 or > 2 || rate is < 8000 or > 96000 || !double.IsFinite(duration) || duration <= 0 ||
                duration * rate * channels * 4 > 256L * 1024 * 1024) throw new InvalidDataException("Audio decode exceeds budget");
            if (extension == ".flac" && (new FileInfo(path).Length > FlacDecoderOptions.GamePlayback.MaxInputBytes ||
                duration * rate * channels * 2 > FlacDecoderOptions.GamePlayback.MaxPcmBytes))
                throw new InvalidDataException("FLAC exceeds game playback budget");
        }
        _ = Run("ffmpeg", ["-v", "error", "-nostdin", "-xerror", "-max_alloc", "268435456", "-threads", "1",
            "-protocol_whitelist", "file", "-i", path, "-threads", "1", "-f", "null", "-"], 4096, ct);
    }
    private static string Run(string tool, string[] args, int maxOutput, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception) { throw new InvalidDataException("Media validation tool unavailable"); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        async Task<string> ReadBounded(StreamReader reader, int limit)
        {
            var result = new System.Text.StringBuilder(); var buffer = new char[2048]; int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), deadline.Token)) > 0)
            {
                if (result.Length + count > limit) throw new InvalidDataException("Media tool response exceeds budget");
                result.Append(buffer, 0, count);
            }
            return result.ToString();
        }
        try
        {
            var output = ReadBounded(process.StandardOutput, maxOutput);
            var errors = ReadBounded(process.StandardError, 8192);
            Task.WhenAll(output, errors, process.WaitForExitAsync(deadline.Token)).GetAwaiter().GetResult();
            if (process.ExitCode != 0 || errors.Result.Length != 0) throw new InvalidDataException("Media decode failed");
            return output.Result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is OperationCanceledException or JsonException or FormatException)
        { throw new InvalidDataException("Media validation failed", ex); }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}
