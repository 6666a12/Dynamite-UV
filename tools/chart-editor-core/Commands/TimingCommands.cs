using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public sealed class EditBpmCommand(ExactBarTime originalTime, ExactBarTime newTime, double newBpm) : IEditorCommand
{
    private double _oldBpm;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var bpm = document.RequireBpm(originalTime);
        EditorCommandRules.ValidateTime(newTime, "BPM time");
        if (!double.IsFinite(newBpm) || newBpm <= 0)
            throw new InvalidOperationException("BPM must be finite and greater than zero.");
        if (originalTime == ExactBarTime.Zero && newTime != ExactBarTime.Zero)
            throw new InvalidOperationException("The base BPM event at bar 0 cannot be moved.");
        if (newTime != originalTime && document.SelectedChart.Bpms.Any(item => item.Time == newTime))
            throw new InvalidOperationException($"BPM already exists at {newTime}.");
        if (!_captured)
        {
            _oldBpm = bpm.Bpm;
            _captured = true;
        }
        bpm.Time = newTime;
        bpm.Bpm = newBpm;
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, ExactBarTimeText.Format(newTime)));
    }

    public void Undo(EditorDocument document)
    {
        var bpm = document.RequireBpm(newTime);
        bpm.Time = originalTime;
        bpm.Bpm = _oldBpm;
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, ExactBarTimeText.Format(originalTime)));
    }
}

public sealed class AddBpmCommand(ExactBarTime time, double bpm) : IEditorCommand
{
    private readonly EditorEntityKey _key = EditorEntityKey.New();

    public void Execute(EditorDocument document)
    {
        EditorCommandRules.ValidateTime(time, "BPM time");
        if (!double.IsFinite(bpm) || bpm <= 0)
            throw new InvalidOperationException("BPM must be finite and greater than zero.");
        if (document.SelectedChart.Bpms.Any(item => item.Time == time))
            throw new InvalidOperationException($"BPM already exists at {time}.");
        document.SelectedChart.Bpms.Add(new EditableBpm { Key = _key, Time = time, Bpm = bpm });
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, ExactBarTimeText.Format(time)));
    }

    public void Undo(EditorDocument document)
    {
        document.SelectedChart.Bpms.RemoveAll(item => item.Time == time);
        document.Select(EditorSelection.None);
    }
}

public sealed class DeleteBpmCommand(ExactBarTime time) : IEditorCommand
{
    private EditableBpm? _deleted;
    private int _index;

    public void Execute(EditorDocument document)
    {
        if (time == ExactBarTime.Zero)
            throw new InvalidOperationException("The base BPM event at bar 0 cannot be deleted.");
        _index = document.SelectedChart.Bpms.FindIndex(item => item.Time == time);
        if (_index < 0)
            throw new KeyNotFoundException($"BPM at '{time}' was not found.");
        _deleted = document.SelectedChart.Bpms[_index];
        document.SelectedChart.Bpms.RemoveAt(_index);
        document.Select(EditorSelection.None);
    }

    public void Undo(EditorDocument document)
    {
        if (_deleted is null)
            throw new InvalidOperationException("Delete BPM command has not executed.");
        document.SelectedChart.Bpms.Insert(_index, _deleted);
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, ExactBarTimeText.Format(_deleted.Time)));
    }
}

public sealed class AddScrollCommand : IEditorCommand
{
    private readonly EditableScroll _scroll;
    private int _index;

    public AddScrollCommand(ExactBarTime time, double value, V2ScrollCurve? curveToNext = null)
        : this(new EditableScroll { Time = time, Value = value, CurveToNext = curveToNext })
    {
    }

    public AddScrollCommand(EditableScroll scroll)
    {
        ArgumentNullException.ThrowIfNull(scroll);
        _scroll = scroll.Clone();
    }

    public void Execute(EditorDocument document)
    {
        var timeline = document.SelectedChart.ScrollSpeeds;
        if (timeline.Any(item => item.Time == _scroll.Time))
            throw new InvalidOperationException($"Scroll event already exists at {_scroll.Time}.");
        var proposed = timeline.Select(item => item.Clone()).Append(_scroll.Clone())
            .OrderBy(item => item.Time).ToList();
        EditorCommandRules.ValidateScrollTimeline(proposed);
        _index = proposed.FindIndex(item => item.Time == _scroll.Time);
        timeline.Insert(_index, _scroll.Clone());
        document.Select(new EditorSelection(EditorSelectionKind.Scroll,
            ExactBarTimeText.Format(_scroll.Time)));
    }

    public void Undo(EditorDocument document)
    {
        var timeline = document.SelectedChart.ScrollSpeeds;
        var index = timeline.FindIndex(item => item.Time == _scroll.Time);
        if (index < 0)
            throw new InvalidOperationException("Added Scroll event is no longer present.");
        timeline.RemoveAt(index);
        document.Select(EditorSelection.None);
    }
}

public sealed class EditScrollCommand(
    ExactBarTime originalTime,
    ExactBarTime newTime,
    double newValue,
    V2ScrollCurve? newCurveToNext) : IEditorCommand
{
    private EditableScroll? _before;

    public void Execute(EditorDocument document)
    {
        var timeline = document.SelectedChart.ScrollSpeeds;
        var scroll = document.RequireScroll(originalTime);
        if (originalTime == ExactBarTime.Zero && newTime != ExactBarTime.Zero)
            throw new InvalidOperationException("The base Scroll event at bar 0 cannot be moved.");
        if (newTime != originalTime && timeline.Any(item => item.Time == newTime))
            throw new InvalidOperationException($"Scroll event already exists at {newTime}.");

        var proposed = timeline.Select(item => item.Time == originalTime
                ? new EditableScroll { Time = newTime, Value = newValue, CurveToNext = newCurveToNext }
                : item.Clone())
            .OrderBy(item => item.Time).ToList();
        EditorCommandRules.ValidateScrollTimeline(proposed);
        _before ??= scroll.Clone();
        scroll.Time = newTime;
        scroll.Value = newValue;
        scroll.CurveToNext = newCurveToNext;
        timeline.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Scroll,
            ExactBarTimeText.Format(newTime)));
    }

    public void Undo(EditorDocument document)
    {
        if (_before is null)
            throw new InvalidOperationException("Edit Scroll command has not executed.");
        var timeline = document.SelectedChart.ScrollSpeeds;
        var scroll = document.RequireScroll(newTime);
        scroll.Time = _before.Time;
        scroll.Value = _before.Value;
        scroll.CurveToNext = _before.CurveToNext;
        timeline.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Scroll,
            ExactBarTimeText.Format(_before.Time)));
    }
}

public sealed class DeleteScrollCommand(ExactBarTime time) : IEditorCommand
{
    private EditableScroll? _deleted;
    private int _index;
    private V2ScrollCurve? _promotedTailCurve;
    private bool _changedPromotedTail;

    public void Execute(EditorDocument document)
    {
        var timeline = document.SelectedChart.ScrollSpeeds;
        _index = timeline.FindIndex(item => item.Time == time);
        if (_index < 0)
            throw new KeyNotFoundException($"Scroll event at '{time}' was not found.");
        if (_index == 0 && timeline.Count > 1)
            throw new InvalidOperationException("Bar 0 Scroll cannot be deleted while later Scroll events remain.");
        _deleted ??= timeline[_index].Clone();
        _changedPromotedTail = _index == timeline.Count - 1 && _index > 0;
        if (_changedPromotedTail)
        {
            _promotedTailCurve = timeline[_index - 1].CurveToNext;
            timeline[_index - 1].CurveToNext = null;
        }
        timeline.RemoveAt(_index);
        document.Select(EditorSelection.None);
    }

    public void Undo(EditorDocument document)
    {
        if (_deleted is null)
            throw new InvalidOperationException("Delete Scroll command has not executed.");
        var timeline = document.SelectedChart.ScrollSpeeds;
        timeline.Insert(_index, _deleted.Clone());
        if (_changedPromotedTail)
            timeline[_index - 1].CurveToNext = _promotedTailCurve;
        document.Select(new EditorSelection(EditorSelectionKind.Scroll,
            ExactBarTimeText.Format(_deleted.Time)));
    }
}

public sealed class EditChartOffsetCommand(double audioOffsetSec) : IEditorCommand
{
    private double _oldOffset;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        if (!double.IsFinite(audioOffsetSec))
            throw new InvalidOperationException("Chart audio offset must be finite.");
        if (!_captured)
        {
            _oldOffset = document.SelectedChart.AudioOffsetSec;
            _captured = true;
        }
        document.SelectedChart.AudioOffsetSec = audioOffsetSec;
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Chart offset command has not executed.");
        document.SelectedChart.AudioOffsetSec = _oldOffset;
    }
}
