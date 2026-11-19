using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

/// <summary>Converts real stage hits to one canonical authoring target. A group contains
/// parent notes only; a single path handle keeps its own source ID as well as its parent.</summary>
internal sealed record StageSelection(string[] Sources, string Primary, string[] Parents, string? Node)
{
    public string Parent => Parents.Length == 0 ? "" : Node is null ? Primary : Parents[0];

    public static StageSelection Pick(V2RuntimeAdapterResult chart, HashSet<int> selected,
        int hit, bool toggle, bool path)
    {
        var target = chart.RuntimeChart.AllNotes.Single(n => n.Id == hit).V2Metadata!;
        if (!toggle && path && target.IsPathNode && !target.IsShapeSample)
        {
            selected.Clear();
            selected.Add(hit);
            return new([target.SourceId], target.SourceId, [target.ParentSourceId], target.SourceId);
        }
        var parent = chart.RuntimeIdsBySourceId[target.ParentSourceId];
        if (toggle) NormalizeParents(chart, selected);
        else selected.Clear();
        if (!toggle || !selected.Remove(parent)) selected.Add(parent);
        return Snapshot(chart, selected, parent);
    }

    public static StageSelection Box(V2RuntimeAdapterResult chart, HashSet<int> selected, IReadOnlySet<int> ids)
    {
        selected.Clear();
        selected.UnionWith(ids);
        NormalizeParents(chart, selected);
        return Snapshot(chart, selected, -1);
    }

    private static void NormalizeParents(V2RuntimeAdapterResult chart, HashSet<int> selected)
    {
        var parents = chart.RuntimeChart.AllNotes.Where(n => selected.Contains(n.Id))
            .Select(n => chart.RuntimeIdsBySourceId[n.V2Metadata!.ParentSourceId]).ToArray();
        selected.Clear();
        selected.UnionWith(parents);
    }

    private static StageSelection Snapshot(V2RuntimeAdapterResult chart, HashSet<int> selected, int preferred)
    {
        var ids = selected.Order().Select(id => chart.SourceIdsByRuntimeId[id]).ToArray();
        var primary = selected.Contains(preferred) ? chart.SourceIdsByRuntimeId[preferred] : ids.FirstOrDefault() ?? "";
        return new(ids, primary, ids, null);
    }
}
