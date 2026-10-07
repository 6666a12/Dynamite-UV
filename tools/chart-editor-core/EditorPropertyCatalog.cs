namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>
/// The UI contract for every persisted v2 property handled by the editor. Paths are
/// canonical model paths rather than control names, so a desktop shell can change
/// without silently dropping a field.
/// </summary>
public sealed record EditorPropertyDescriptor(
    string FieldPath,
    string EditorPath,
    EditorPropertyEditRoute EditRoute,
    string? ReadOnlyReason = null);

public enum EditorPropertyEditRoute
{
    ReadOnly,
    DirectDocument,
    EditorCommand,
    PackageResourceTransaction,
    ChartManagement,
}

public static class EditorPropertyCatalog
{
    private const string FormatIsFixed = "Fixed by the v2 format.";
    private const string ChartIdIsDerived = "Must match the selected chart entry id.";
    private const string JudgeExplicitIsDerived = "Decode provenance; not serialized by editor snapshots.";

    public static IReadOnlyList<EditorPropertyDescriptor> All { get; } =
    [
        ReadOnly("pack.format", "Project > Format", FormatIsFixed),
        ReadOnly("pack.formatVersion", "Project > Format", FormatIsFixed),
        Edit("pack.id", "Project > Identity > Advanced", EditorPropertyEditRoute.DirectDocument),
        ReadOnly("pack.revision", "Project > Identity", "Incremented after a successful modified save."),
        Edit("pack.title", "Project > Metadata", EditorPropertyEditRoute.DirectDocument),
        Edit("pack.artist", "Project > Metadata", EditorPropertyEditRoute.DirectDocument),
        Edit("pack.audio", "Project > Media > Import/Replace", EditorPropertyEditRoute.PackageResourceTransaction),
        Edit("pack.cover", "Project > Media > Import/Replace", EditorPropertyEditRoute.PackageResourceTransaction),
        Edit("pack.preview.startSec", "Project > Media > Preview", EditorPropertyEditRoute.DirectDocument),
        Edit("pack.preview.durationSec", "Project > Media > Preview", EditorPropertyEditRoute.DirectDocument),
        Edit("pack.charts[]", "Project > Charts", EditorPropertyEditRoute.ChartManagement),

        Edit("charts[].id", "Project > Charts > Identity > Advanced", EditorPropertyEditRoute.ChartManagement),
        Edit("charts[].difficulty", "Project > Charts > Difficulty", EditorPropertyEditRoute.ChartManagement),
        Edit("charts[].difficultyKey", "Project > Charts > Difficulty", EditorPropertyEditRoute.ChartManagement),
        Edit("charts[].level", "Project > Charts > Difficulty", EditorPropertyEditRoute.ChartManagement),
        Edit("charts[].unrated", "Project > Charts > Difficulty", EditorPropertyEditRoute.ChartManagement),
        Edit("charts[].charters[]", "Project > Charts > Credits", EditorPropertyEditRoute.ChartManagement),
        Edit("charts[].file", "Project > Charts > Storage > Advanced", EditorPropertyEditRoute.PackageResourceTransaction),
        Edit("charts[].audio", "Project > Charts > Media", EditorPropertyEditRoute.PackageResourceTransaction),
        Edit("charts[].preview.startSec", "Project > Charts > Media", EditorPropertyEditRoute.ChartManagement),
        Edit("charts[].preview.durationSec", "Project > Charts > Media", EditorPropertyEditRoute.ChartManagement),

        ReadOnly("chart.format", "Chart > Format", FormatIsFixed),
        ReadOnly("chart.formatVersion", "Chart > Format", FormatIsFixed),
        ReadOnly("chart.chartId", "Project > Charts > Identity", ChartIdIsDerived),
        Edit("chart.audioOffsetSec", "Transport > Offset / Chart Inspector", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.bpms[].time", "Events > BPM / Canvas Marker", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.bpms[].bpm", "Events > BPM / Canvas Marker", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.scrollSpeeds[].time", "Events > Scroll / Canvas Marker", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.scrollSpeeds[].value", "Events > Scroll / Canvas Marker", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.scrollSpeeds[].curveToNext", "Events > Scroll / Canvas Marker", EditorPropertyEditRoute.EditorCommand),

        Edit("chart.notesLeft[]", "Canvas > Left", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notesCenter[]", "Canvas > Center", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notesRight[]", "Canvas > Right", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].id", "Note Inspector > Identity > Advanced", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].type", "Canvas Tool / Note Inspector > Type", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].time", "Canvas / Note Inspector > Exact BarTime", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].center", "Canvas / Note Inspector > Geometry", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].width", "Canvas / Note Inspector > Geometry", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].curveToNext", "Path Inspector > Head Segment", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].nodes[]", "Canvas Path / Path Inspector", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].nodes[].id", "Path Inspector > Node > Identity > Advanced", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].nodes[].time", "Canvas Path / Path Inspector", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].nodes[].center", "Canvas Path / Path Inspector", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].nodes[].width", "Canvas Path / Path Inspector", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].nodes[].curveToNext", "Path Inspector > Segment", EditorPropertyEditRoute.EditorCommand),
        Edit("chart.notes[].nodes[].judge", "Path Inspector > Judge", EditorPropertyEditRoute.EditorCommand),
        ReadOnly("chart.notes[].nodes[].judgeWasExplicit", "Path Inspector > Judge", JudgeExplicitIsDerived),
    ];

    public static EditorPropertyDescriptor Require(string fieldPath) =>
        All.Single(descriptor => string.Equals(descriptor.FieldPath, fieldPath, StringComparison.Ordinal));

    public static void Validate()
    {
        var duplicate = All.GroupBy(descriptor => descriptor.FieldPath, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate editor property catalog path '{duplicate.Key}'.");

        foreach (var descriptor in All)
        {
            if (string.IsNullOrWhiteSpace(descriptor.EditorPath))
                throw new InvalidOperationException($"Property '{descriptor.FieldPath}' has no editor path.");
            if (descriptor.EditRoute == EditorPropertyEditRoute.ReadOnly == string.IsNullOrWhiteSpace(descriptor.ReadOnlyReason))
                throw new InvalidOperationException($"Property '{descriptor.FieldPath}' has inconsistent edit metadata.");
        }
    }

    private static EditorPropertyDescriptor Edit(string fieldPath, string editorPath, EditorPropertyEditRoute route) =>
        new(fieldPath, editorPath, route);

    private static EditorPropertyDescriptor ReadOnly(string fieldPath, string editorPath, string reason) =>
        new(fieldPath, editorPath, EditorPropertyEditRoute.ReadOnly, reason);
}
