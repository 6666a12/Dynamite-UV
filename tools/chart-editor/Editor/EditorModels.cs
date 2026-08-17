namespace DuxCommunity.ChartEditor.Editor;

public enum EditorTrack
{
    Left,
    Center,
    Right,
    Events,
}

public enum EditorTool
{
    Select,
    Tap,
    Drag,
    Hold,
    Bpm,
}

public sealed record EditorBpmEvent(
    string Id,
    double Bar,
    double Second,
    double Bpm,
    string ExactTime);

public sealed record EditorPathPoint(
    double Bar,
    double Second,
    double Center,
    double Width);

public sealed record EditorNoteModel
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required EditorTrack Track { get; init; }
    public required double Bar { get; init; }
    public required double Second { get; init; }
    public required double Center { get; init; }
    public required double Width { get; init; }
    public required string ExactTime { get; init; }
    public required IReadOnlyList<EditorPathPoint> Path { get; init; }

    public bool IsPath => Path.Count > 1;
    public double EndBar => Path.Count == 0 ? Bar : Path[^1].Bar;
    public double EndSecond => Path.Count == 0 ? Second : Path[^1].Second;
}

public sealed record EditorChartDocument
{
    public required string Id { get; init; }
    public required string Difficulty { get; init; }
    public required string DifficultyDisplay { get; init; }
    public required int? Level { get; init; }
    public required IReadOnlyList<string> Charters { get; init; }
    public required string SourceFile { get; init; }
    public required string ResolvedAudio { get; init; }
    public required double AudioOffsetSec { get; init; }
    public required double DurationSec { get; init; }
    public required long MainJudgementCount { get; init; }
    public required IReadOnlyList<EditorBpmEvent> Bpms { get; init; }
    public required IReadOnlyList<EditorNoteModel> Notes { get; init; }
    public required Func<double, double> SecondsToBar { get; init; }
    public required Func<double, double> BarToSeconds { get; init; }
    public required Func<double, double> ScrollSpeedAtBar { get; init; }

    public string DisplayName =>
        $"{DifficultyDisplay} {Level?.ToString() ?? "—"} · {Id}";
    public int PathCount => Notes.Count(note => note.IsPath);

    public override string ToString() => DisplayName;
}

public sealed record EditorProject
{
    public required string RootPath { get; init; }
    public required string PackId { get; init; }
    public required long Revision { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required string MetaFile { get; init; }
    public required IReadOnlyList<EditorChartDocument> Charts { get; init; }
}

public sealed record EditorSelectionInfo(
    string Kind,
    string Id,
    string Track,
    string Time,
    string Position,
    string Width,
    string Detail);
