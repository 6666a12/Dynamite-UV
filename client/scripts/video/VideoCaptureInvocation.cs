using System.Globalization;
using Godot;

namespace DynamiteUniverse.Video;

/// <summary>Export-only invocation and filesystem preflight. Never loads the normal game profile.</summary>
internal sealed record VideoCaptureInvocation(string UserDirectory, string OutputDirectory, string ChartId,
    int PreviewFrames, int ResultHoldFrames, GodotObject? StreamWriter)
{
    internal const int FramesPerSecond = VideoCaptureTiming.FramesPerSecond;
    internal static bool IsDedicatedBuild => OS.HasFeature("uv_video_capture");
    internal static bool Active { get; private set; }

    internal static VideoCaptureInvocation Read()
    {
        if (!IsDedicatedBuild || !OS.GetCmdlineUserArgs().Contains("--uv-video-export", StringComparer.Ordinal) || !OS.HasFeature("movie"))
            throw new InvalidDataException("UV Video Capture requires --write-movie and -- --uv-video-export");
        if (DisplayServer.GetName() == "headless")
            throw new InvalidDataException("UV Video Capture requires a graphics display driver");

        var user = ReadDirectory("DU_CAPTURE_USER");
        var output = ReadDirectory("DU_CAPTURE_OUT");
        var actual = Path.TrimEndingDirectorySeparator(Path.GetFullPath(OS.GetUserDataDir()));
        if (!string.Equals(user, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DU_CAPTURE_USER does not match the actual Godot user directory");
        VideoCaptureFolders.Validate(user, output);
        var writer = GetStreamWriter(output);
        if (AudioServer.GetDriverName() != "Dummy")
            throw new InvalidDataException("UV Video Capture requires --audio-driver Dummy");
        if (File.Exists(Path.Combine(output, "capture-result.json")))
            throw new InvalidDataException("capture output already contains capture-result.json");

        var chart = OS.GetEnvironment("DU_CAPTURE_CHART_ID");
        if (string.IsNullOrWhiteSpace(chart))
            throw new InvalidDataException("DU_CAPTURE_CHART_ID is required");
        var preview = ReadFrames("DU_CAPTURE_PREVIEW_FRAMES");
        var hold = ReadFrames("DU_CAPTURE_RESULT_HOLD_FRAMES");
        Active = true;
        return new(user, output, chart, preview, hold, writer);
    }

    private static GodotObject? GetStreamWriter(string output)
    {
        const string pipePrefix = @"\\.\pipe\";
        var movie = Engine.GetWriteMoviePath();
        if (!Path.IsPathFullyQualified(movie))
            throw new InvalidDataException("A MovieWriter did not accept the requested capture output path");
        movie = Path.GetFullPath(movie);
        var pipe = OS.GetEnvironment("DU_CAPTURE_PIPE");
        if (pipe.Length == 0 && movie.Equals(Path.Combine(output, "capture.avi"), StringComparison.OrdinalIgnoreCase))
            return null; // Explicit isolated developer evidence only; the Shell uses the streaming path.
        if (!movie.Equals(Path.Combine(output, "capture.uvstream"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("--write-movie must target DU_CAPTURE_OUT/capture.uvstream for streaming");
        if (pipe.Length <= pipePrefix.Length || pipe.Length > 256 ||
            !pipe.StartsWith(pipePrefix, StringComparison.OrdinalIgnoreCase) ||
            pipe[pipePrefix.Length..].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.')))
            throw new InvalidDataException("DU_CAPTURE_PIPE must name a private local Windows pipe");
        if (!Engine.HasSingleton("UVVideoStream") || Engine.GetSingleton("UVVideoStream") is not { } writer ||
            !writer.HasMethod("arm"))
            throw new InvalidDataException("UVVideoStream is missing or incompatible with the capture worker");
        return writer;
    }

    internal void ConfirmNoSavedData()
    {
        if (File.Exists(Path.Combine(UserDirectory, "settings.json")) ||
            File.Exists(Path.Combine(UserDirectory, "scores.json")))
            throw new InvalidDataException("recording unexpectedly wrote settings or scores");
    }

    private static string ReadDirectory(string name)
    {
        var value = OS.GetEnvironment(name);
        if (!Path.IsPathFullyQualified(value))
            throw new InvalidDataException($"{name} must be an absolute directory path");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private static int ReadFrames(string name)
    {
        if (!int.TryParse(OS.GetEnvironment(name), NumberStyles.None, CultureInfo.InvariantCulture, out var frames) ||
            frames > VideoCaptureTiming.MaximumSegmentFrames)
            throw new InvalidDataException($"{name} must be an integer in 0..3600");
        return frames;
    }
}
