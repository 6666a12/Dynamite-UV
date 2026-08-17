using ChartTool.IsolatedV2;

namespace ChartTool;

internal sealed class ConvertedPack
{
    public required string Id { get; init; }
    public required long Revision { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required string Audio { get; init; }
    public string? Cover { get; init; }
    public required IReadOnlyList<ConvertedChartEntry> Charts { get; init; }
}

internal sealed class ConvertedChartEntry
{
    public required string Id { get; init; }
    public required string Difficulty { get; init; }
    public string? DifficultyKey { get; init; }
    public int? Level { get; init; }
    public bool Unrated { get; init; }
    public required string Charter { get; init; }
    public required string File { get; init; }
    public required ConvertedChart Chart { get; init; }
    public required AuditMetrics Audit { get; init; }
}

internal sealed class ConvertedChart
{
    public required string ChartId { get; init; }
    public required double AudioOffsetSec { get; init; }
    public required IReadOnlyList<ConvertedBpm> Bpms { get; init; }
    public required IReadOnlyList<ConvertedScroll> ScrollSpeeds { get; init; }
    public required IReadOnlyList<ConvertedNote> NotesLeft { get; init; }
    public required IReadOnlyList<ConvertedNote> NotesCenter { get; init; }
    public required IReadOnlyList<ConvertedNote> NotesRight { get; init; }
}

internal sealed class ConvertedBpm
{
    public required RationalBarTime Time { get; init; }
    public required double Bpm { get; init; }
}

internal sealed class ConvertedScroll
{
    public required RationalBarTime Time { get; init; }
    public required double Value { get; init; }
}

internal sealed class ConvertedNote
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required RationalBarTime Time { get; init; }
    public required double Center { get; init; }
    public required double Width { get; init; }
    public required IReadOnlyList<ConvertedNode> Nodes { get; init; }
}

internal sealed class ConvertedNode
{
    public required string Id { get; init; }
    public required RationalBarTime Time { get; init; }
    public required double Center { get; init; }
    public required double Width { get; init; }
    public bool? Judge { get; init; }
}

internal sealed class AuditMetrics
{
    public int BakedTotal { get; init; }
    public int? DerivedTotal { get; init; }
    public int BakedSyncCount { get; init; }
    public int DerivableSyncCount { get; init; }
    public int SyncMismatchCount { get; init; }
    public int ContinuityErrorCount { get; init; }
    public int CrossBpmSustainCount { get; init; }
}
