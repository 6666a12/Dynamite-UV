namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>A single held O/P key. The live chart uses the cumulative millisecond delta from
/// the original offset, preserving its sub-millisecond component and exact undo value.</summary>
public sealed class ChartOffsetNudge
{
    private EditorDocument? _document;
    private readonly EditableChart _chart;
    private readonly ChartOffsetPosition _before;

    internal ChartOffsetNudge(EditorDocument document, EditableChart chart)
    {
        _document = document;
        _chart = chart;
        _before = chart.OffsetPosition;
    }

    internal bool HasChange => _chart.AudioOffsetSec != _before.Seconds;
    internal string ChartId => _chart.Id;

    /// <summary>Apply one coarse or fine step immediately. A closed handle ignores stale repeats.</summary>
    public bool Step(double deltaSeconds)
    {
        if (_document is null) return false;
        int step = deltaSeconds switch
        {
            .010 => 10, -.010 => -10, .001 => 1, -.001 => -1,
            _ => throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Use a signed 10 ms or 1 ms offset step."),
        };
        var target = _chart.OffsetPosition.Move(step);
        if (_chart.AudioOffsetSec == target.Seconds) return false;
        _chart.OffsetPosition = target;
        _document.OffsetStepApplied();
        return true;
    }

    public void End() => _document?.EndChartOffsetNudge();

    internal IEditorCommand? Finish()
    {
        _document = null;
        return HasChange ? new OffsetChange(_before, _chart.OffsetPosition) : null;
    }

    // History selects the owning chart ID before executing either direction.
    private sealed record OffsetChange(ChartOffsetPosition Before, ChartOffsetPosition After) : IEditorCommand
    {
        public void Execute(EditorDocument document) => document.SelectedChart.OffsetPosition = After;
        public void Undo(EditorDocument document) => document.SelectedChart.OffsetPosition = Before;
    }
}
