using DuxShared.Judge;

namespace DuxShared.Chart.V2;

/// <summary>Literal identifiers frozen by Dynamite UV Chart Format v2.</summary>
public static class V2Format
{
    public const string PackFormat = "dynamite-uv-pack";
    public const string ChartFormat = "dynamite-uv-chart";
    public const int FormatVersion = 2;
    public const string RulesetId = "dynamite-uv-ruleset-2.0";
    public const string GameplayDigestAlgorithm = "gameplay-v1";
}

public enum V2Difficulty
{
    Casual,
    Normal,
    Hard,
    Mega,
    Giga,
    Tech,
    Custom,
}

public enum V2NoteType
{
    Tap,
    Drag,
    ExTap,
    Hold,
    Mixer,
    Mine,
    BarLine,
}

public enum V2PathCurve
{
    Linear,
    Hold,
    EaseInQuad,
    EaseOutQuad,
    EaseInOutCubic,
    Smooth,
}

public enum V2ScrollCurve
{
    Linear,
    Hold,
    EaseInQuad,
    EaseOutQuad,
    EaseInOutCubic,
}

/// <summary>An optional package or chart-entry preview interval in decoded audio seconds.</summary>
public sealed record V2Preview(double StartSec, double DurationSec);

/// <summary>A chart declaration from v2 <c>meta.json</c>.</summary>
public sealed record V2ChartEntry
{
    public required string Id { get; init; }
    public required V2Difficulty Difficulty { get; init; }
    public string? DifficultyKey { get; init; }
    public int? Level { get; init; }
    public bool Unrated { get; init; }
    public required IReadOnlyList<string> Charters { get; init; }
    public required string File { get; init; }
    public string? Audio { get; init; }
    public V2Preview? Preview { get; init; }

    public JudgePreset JudgePreset => Difficulty switch
    {
        V2Difficulty.Casual => JudgePreset.Casual,
        V2Difficulty.Normal => JudgePreset.Normal,
        _ => JudgePreset.Hard,
    };
}

/// <summary>Strictly decoded semantic model for v2 <c>meta.json</c>.</summary>
public sealed record V2Pack
{
    public string Format => V2Format.PackFormat;
    public int FormatVersion => V2Format.FormatVersion;
    public required string Id { get; init; }
    public required long Revision { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string? Audio { get; init; }
    public string? Cover { get; init; }
    public V2Preview? Preview { get; init; }
    public required IReadOnlyList<V2ChartEntry> Charts { get; init; }

    public string ResolveAudio(V2ChartEntry chart) => chart.Audio ?? Audio ??
        throw new InvalidOperationException($"Chart '{chart.Id}' has no resolved audio path.");

    public V2Preview? ResolvePreview(V2ChartEntry chart) => chart.Preview ?? Preview;
}

/// <summary>A BPM change at an exact bar position.</summary>
public sealed record V2BpmEvent(ExactBarTime Time, double Bpm);

/// <summary>A visual scroll-speed control point.</summary>
public sealed record V2ScrollEvent
{
    public required ExactBarTime Time { get; init; }
    public required double Value { get; init; }
    public V2ScrollCurve? CurveToNext { get; init; }
}

/// <summary>A path control point embedded in a Hold or Mixer.</summary>
public sealed record V2PathNode
{
    public required string Id { get; init; }
    public required ExactBarTime Time { get; init; }
    public required double Center { get; init; }
    public required double Width { get; init; }
    public V2PathCurve? CurveToNext { get; init; }
    /// <summary>Resolved Hold judgement state. Mixer nodes always leave this null.</summary>
    public bool? Judge { get; init; }
    /// <summary>True only when a Hold node explicitly contained <c>judge</c>.</summary>
    public bool JudgeWasExplicit { get; init; }
}

/// <summary>Base type for a parent note. Track is represented by its containing chart array.</summary>
public abstract record V2Note
{
    public required string Id { get; init; }
    public required V2NoteType Type { get; init; }
    public required ExactBarTime Time { get; init; }
    public required double Center { get; init; }
    public required double Width { get; init; }

    public double Left => Center - Width / 2.0;
    public double Right => Center + Width / 2.0;
}

/// <summary>A non-path Tap, Drag, EX-Tap, Mine, or BarLine.</summary>
public sealed record V2BasicNote : V2Note;

/// <summary>A Hold or Mixer with one or more shape nodes.</summary>
public sealed record V2PathNote : V2Note
{
    public V2PathCurve? CurveToNext { get; init; }
    public required IReadOnlyList<V2PathNode> Nodes { get; init; }
    public ExactBarTime EndTime => Nodes[^1].Time;
}

/// <summary>Strictly decoded semantic model for a single v2 chart file.</summary>
public sealed record V2Chart
{
    public string Format => V2Format.ChartFormat;
    public int FormatVersion => V2Format.FormatVersion;
    public required string ChartId { get; init; }
    public required double AudioOffsetSec { get; init; }
    public required IReadOnlyList<V2BpmEvent> Bpms { get; init; }
    /// <summary>An empty list carries the specified implicit constant speed of 1.</summary>
    public IReadOnlyList<V2ScrollEvent> ScrollSpeeds { get; init; } = [];
    public required IReadOnlyList<V2Note> NotesLeft { get; init; }
    public required IReadOnlyList<V2Note> NotesCenter { get; init; }
    public required IReadOnlyList<V2Note> NotesRight { get; init; }

    public IReadOnlyList<V2Note> NotesOf(Track track) => track switch
    {
        Track.Left => NotesLeft,
        Track.Center => NotesCenter,
        _ => NotesRight,
    };

    public IEnumerable<(Track Track, V2Note Note)> AllNotes
    {
        get
        {
            foreach (var note in NotesLeft)
                yield return (Track.Left, note);
            foreach (var note in NotesCenter)
                yield return (Track.Center, note);
            foreach (var note in NotesRight)
                yield return (Track.Right, note);
        }
    }
}
