using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Editor.Shell.VideoExport;

public sealed record VideoExportSource(string Title, string Difficulty, bool HasUnsavedChanges,
    double PreviewMaximumSeconds, string SuggestedOutputPath, int FallSpeedLevel = GameplayFallSpeed.DefaultLevel);

/// <summary>Codec IDs are h264, hevc and av1; errors are displayable probe diagnostics.</summary>
public sealed record VideoExportCapabilities(string? FfmpegPath, IReadOnlyCollection<string> Codecs, string? Error = null);

/// <summary>Preset IDs are slow, medium and high, in order of increasing encoding speed.</summary>
public sealed record VideoExportOptions(string OutputPath, string FfmpegPath, string Codec, string Preset,
    int Crf, double PreviewSeconds, double ResultHoldSeconds, int FallSpeedLevel = GameplayFallSpeed.DefaultLevel);

public enum VideoExportPhase { Prepare, Render, Encode, Validate }

/// <summary>Fraction, when known, describes this phase rather than the entire export.</summary>
public sealed record VideoExportProgress(VideoExportPhase Phase, double? Fraction = null, string? Detail = null);

internal sealed record VideoExportCodec(string Id, string Label, int MinCrf, int MaxCrf, int DefaultCrf);

internal static class VideoExportChoices
{
    public const decimal MaximumSegmentSeconds = 60m;
    public static IReadOnlyList<VideoExportCodec> Codecs { get; } =
    [
        new("h264", "VideoExport.H264", 0, 51, 16),
        new("hevc", "VideoExport.Hevc", 0, 51, 20),
        new("av1", "VideoExport.Av1", 1, 63, 24),
    ];
    public static IReadOnlyList<string> Presets { get; } = ["slow", "medium", "high"];
}
