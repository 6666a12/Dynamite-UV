using ChartTool.IsolatedV2;

namespace ChartTool;

internal sealed class LegacyPack
{
    public required string SourceDirectory { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required string Charter { get; init; }
    public required string AudioPath { get; init; }
    public string? CoverPath { get; init; }
    public required IReadOnlyList<LegacyChartEntry> Charts { get; init; }
}

internal sealed class LegacyChartEntry
{
    public required string Diff { get; init; }
    public required int Level { get; init; }
    public required string File { get; init; }
    public required string FullPath { get; init; }
    public required string Pointer { get; init; }
}

internal sealed class LegacyChart
{
    public required string File { get; init; }
    public required string Name { get; init; }
    public required int BakedTotalMainNote { get; init; }
    public required IReadOnlyList<LegacyBpmSection> Sections { get; init; }
    public required IReadOnlyList<LegacyScrollEvent> ScrollEvents { get; init; }
    public required IReadOnlyList<LegacyNote> NotesLeft { get; init; }
    public required IReadOnlyList<LegacyNote> NotesCenter { get; init; }
    public required IReadOnlyList<LegacyNote> NotesRight { get; init; }

    public IEnumerable<LegacyNote> AllNotes =>
        NotesLeft.Concat(NotesCenter).Concat(NotesRight);

    public IReadOnlyList<LegacyNote> NotesOf(LegacyTrack track) => track switch
    {
        LegacyTrack.Left => NotesLeft,
        LegacyTrack.Center => NotesCenter,
        _ => NotesRight,
    };
}

internal sealed class LegacyBpmSection
{
    public required double Bpm { get; init; }
    public required double BarTimeValue { get; init; }
    public required RationalBarTime BarTime { get; init; }
    public required double Seconds { get; init; }
    public required string Pointer { get; init; }
}

internal sealed class LegacyScrollEvent
{
    public required double BarTimeValue { get; init; }
    public required RationalBarTime BarTime { get; init; }
    public required double Value { get; init; }
    public required int SourceIndex { get; init; }
    public required string Pointer { get; init; }
}

internal sealed class LegacyNote
{
    public required int Id { get; init; }
    public required int SubNoteId { get; init; }
    public required int Type { get; init; }
    public required LegacyTrack Track { get; init; }
    public required double BarTimeValue { get; init; }
    public required RationalBarTime BarTime { get; init; }
    public required double Position { get; init; }
    public required double Width { get; init; }
    public required double BakedSecond { get; init; }
    public required int BakedSyncNote { get; init; }
    public required string Pointer { get; init; }
}

internal enum LegacyTrack
{
    Left,
    Center,
    Right,
}
