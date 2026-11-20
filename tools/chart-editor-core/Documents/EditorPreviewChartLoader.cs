using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Stage-side reuse across JSON-lines loads. The shell sends original source JSON, never
/// trusted compiled knots. Keep only the active and previous difficulties, including for A/B/A.
/// Owner is a workbench chart-instance key, not the editable chart ID or its audio path.</summary>
public sealed class EditorPreviewChartLoader
{
    private sealed record Entry(string? Owner, CustomCurveCache Curves);
    private Entry? _current, _previous;

    public V2RuntimeAdapterResult Load(string chartJson, string? title, string? owner)
    {
        var previous = _current?.Owner == owner ? _current : _previous?.Owner == owner ? _previous : null;
        var curves = previous?.Curves.Fork() ?? new CustomCurveCache();
        var chart = V2JsonDecoder.DecodeChart(chartJson, "editor-preview", requireMainJudgement: false, curveCache: curves);
        var result = V2RuntimeAdapter.Adapt(chart, title, requireMainJudgement: false);
        // A rejected message has not evicted either successful load.
        if (_current?.Owner != owner) _previous = _current;
        _current = new(owner, curves);
        return result;
    }
}
