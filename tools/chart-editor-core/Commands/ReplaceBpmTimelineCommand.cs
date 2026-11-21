using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Replaces one difficulty's tempo and audio alignment as a single undo step.</summary>
public sealed class ReplaceBpmTimelineCommand : IEditorCommand
{
    private readonly double _audioOffsetSec;
    private readonly EditableBpm[] _after;
    private EditableBpm[]? _before;
    private ChartOffsetPosition _oldOffset;

    public ReplaceBpmTimelineCommand(IEnumerable<V2BpmEvent> points, double audioOffsetSec)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!double.IsFinite(audioOffsetSec)) throw new ArgumentOutOfRangeException(nameof(audioOffsetSec));
        _audioOffsetSec = audioOffsetSec;
        _after = points.Select(p => new EditableBpm { Time = p.Time, Bpm = p.Bpm }).ToArray();
        if (_after.Length == 0 || _after[0].Time != ExactBarTime.Zero)
            throw new ArgumentException("A BPM timeline requires a base event at bar 0.", nameof(points));
        var audioSecond = audioOffsetSec;
        for (var i = 0; i < _after.Length; i++)
        {
            var current = _after[i];
            EditorCommandRules.ValidateTime(current.Time, "BPM time");
            if (!double.IsFinite(current.Bpm) || current.Bpm <= 0 || !double.IsFinite(240.0 / current.Bpm))
                throw new ArgumentException("BPM must be finite and have a representable beat duration.", nameof(points));
            if (i == 0) continue;
            var previous = _after[i - 1];
            if (current.Time <= previous.Time)
                throw new ArgumentException("BPM times must be strictly increasing.", nameof(points));
            // In particular, validate a manually supplied offset that was unknown at parse time.
            var next = audioSecond + (current.Time - previous.Time).ToDouble() * (240.0 / previous.Bpm);
            if (current.Time.ToDouble() <= previous.Time.ToDouble() || next <= audioSecond || !double.IsFinite(next))
                throw new ArgumentException("The BPM points or audio offset exceed UV playback precision.", nameof(points));
            audioSecond = next;
        }
    }

    public void Execute(EditorDocument document)
    {
        var chart = document.SelectedChart;
        _before = chart.Bpms.ToArray();
        _oldOffset = chart.OffsetPosition;
        Replace(chart, _after);
        if (chart.AudioOffsetSec != _audioOffsetSec) chart.AudioOffsetSec = _audioOffsetSec;
        if (document.Selection.Kind == EditorSelectionKind.Bpm) document.Select(EditorSelection.None);
    }

    public void Undo(EditorDocument document)
    {
        var chart = document.SelectedChart;
        Replace(chart, _before ?? throw new InvalidOperationException("BPM replacement has not been applied."));
        chart.OffsetPosition = _oldOffset;
    }

    private static void Replace(EditableChart chart, EditableBpm[] points)
    {
        chart.Bpms.Clear();
        chart.Bpms.AddRange(points);
    }
}
