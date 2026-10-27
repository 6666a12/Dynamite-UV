using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public sealed class RenamePathNodeCommand(string noteId, string nodeId, string newId) : IEditorCommand
{
    public void Execute(EditorDocument document)
    {
        var node = document.RequirePathNode(noteId, nodeId);
        EditorCommandRules.ValidateId(newId, "Path node");
        document.RequireChartWideIdAvailable(newId, nodeId);
        node.Id = newId;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, newId, node.Key));
    }

    public void Undo(EditorDocument document)
    {
        var node = document.RequirePathNode(noteId, newId);
        document.RequireChartWideIdAvailable(nodeId, newId);
        node.Id = nodeId;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId, node.Key));
    }
}

public sealed class AddPathNodeCommand(string noteId, EditablePathNode node) : IEditorCommand
{
    private readonly EditablePathNode _node = EditorCommandRules.CloneNode(node);
    private int _index;

    public void Execute(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        document.RequireChartWideIdAvailable(_node.Id);
        EditorCommandRules.ValidateId(_node.Id, "Path node");
        EditorCommandRules.ValidateTime(_node.Time, "Path node time");
        EditorCommandRules.ValidatePoint(_node.Center, _node.Width, "Path node");

        _index = note.Nodes.FindIndex(existing => existing.Time > _node.Time);
        if (_index < 0)
            _index = note.Nodes.Count;
        var previousTime = _index == 0 ? note.Time : note.Nodes[_index - 1].Time;
        if (_node.Time <= previousTime || _index < note.Nodes.Count && _node.Time >= note.Nodes[_index].Time)
            throw new InvalidOperationException("Path node times must be strictly increasing.");

        var added = EditorCommandRules.CloneNode(_node);
        if (note.Type == V2NoteType.Hold)
            added.Judge ??= true;
        else if (added.Judge is not null)
            throw new InvalidOperationException("Mixer path nodes must not carry judge state.");
        if (_index == note.Nodes.Count)
        {
            if (added.CurveToNext is not null)
                throw new InvalidOperationException("Tail path node must omit curveToNext.");
            if (note.Type == V2NoteType.Hold)
                added.Judge = true;
        }

        var candidate = note.Clone();
        candidate.Nodes.Insert(_index, EditorCommandRules.CloneNode(added));
        EditorCommandRules.ValidateNote(candidate);
        note.Nodes.Insert(_index, added);
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, added.Id));
    }

    public void Undo(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        if (_index >= note.Nodes.Count || note.Nodes[_index].Id != _node.Id)
            throw new InvalidOperationException("Added path node is no longer at its command-owned position.");
        note.Nodes.RemoveAt(_index);
        document.Select(new EditorSelection(EditorSelectionKind.Note, noteId));
    }
}

public sealed class EditPathNodeCommand(
    string noteId,
    string nodeId,
    ExactBarTime time,
    double center,
    double width) : IEditorCommand
{
    private ExactBarTime _oldTime;
    private double _oldCenter;
    private double _oldWidth;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        var index = note.Nodes.FindIndex(node => node.Id == nodeId);
        if (index < 0)
            throw new KeyNotFoundException($"Path node '{nodeId}' was not found in note '{noteId}'.");
        EditorCommandRules.ValidateTime(time, "Path node time");
        EditorCommandRules.ValidatePoint(center, width, "Path node");
        var previousTime = index == 0 ? note.Time : note.Nodes[index - 1].Time;
        if (time <= previousTime || index + 1 < note.Nodes.Count && time >= note.Nodes[index + 1].Time)
            throw new InvalidOperationException("Path node times must remain strictly increasing.");

        var node = note.Nodes[index];
        var candidate = note.Clone();
        candidate.Nodes[index].Time = time;
        candidate.Nodes[index].Center = center;
        candidate.Nodes[index].Width = width;
        EditorCommandRules.ValidateNote(candidate);
        if (!_captured)
        {
            _oldTime = node.Time;
            _oldCenter = node.Center;
            _oldWidth = node.Width;
            _captured = true;
        }
        node.Time = time;
        node.Center = center;
        node.Width = width;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Edit path node command has not executed.");
        var node = document.RequirePathNode(noteId, nodeId);
        node.Time = _oldTime;
        node.Center = _oldCenter;
        node.Width = _oldWidth;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
    }
}

public sealed class DeletePathNodeCommand(string noteId, string nodeId) : IEditorCommand
{
    private EditablePathNode? _deleted;
    private int _index;
    private V2PathCurve? _promotedTailCurve;
    private bool? _promotedTailJudge;
    private bool _changedPromotedTail;

    public void Execute(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        if (note.Nodes.Count == 1)
            throw new InvalidOperationException("Hold and Mixer require at least one path node.");
        _index = note.Nodes.FindIndex(node => node.Id == nodeId);
        if (_index < 0)
            throw new KeyNotFoundException($"Path node '{nodeId}' was not found in note '{noteId}'.");
        _deleted ??= EditorCommandRules.CloneNode(note.Nodes[_index]);

        var candidate = note.Clone();
        if (_index == candidate.Nodes.Count - 1)
        {
            candidate.Nodes[^2].CurveToNext = null;
            if (candidate.Type == V2NoteType.Hold)
                candidate.Nodes[^2].Judge = true;
        }
        candidate.Nodes.RemoveAt(_index);
        EditorCommandRules.ValidateNote(candidate);

        _changedPromotedTail = _index == note.Nodes.Count - 1;
        if (_changedPromotedTail)
        {
            var promoted = note.Nodes[^2];
            _promotedTailCurve = promoted.CurveToNext;
            if (note.Type == V2NoteType.Hold)
                _promotedTailJudge = promoted.Judge;
            promoted.CurveToNext = null;
            if (note.Type == V2NoteType.Hold)
                promoted.Judge = true;
        }
        note.Nodes.RemoveAt(_index);
        document.Select(new EditorSelection(EditorSelectionKind.Note, noteId));
    }

    public void Undo(EditorDocument document)
    {
        if (_deleted is null)
            throw new InvalidOperationException("Delete path node command has not executed.");
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        note.Nodes.Insert(_index, EditorCommandRules.CloneNode(_deleted));
        if (_changedPromotedTail)
        {
            var restoredPrevious = note.Nodes[_index - 1];
            restoredPrevious.CurveToNext = _promotedTailCurve;
            restoredPrevious.Judge = _promotedTailJudge;
        }
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
    }
}

public sealed class SetPathCurveCommand : IEditorCommand
{
    private readonly string _noteId;
    private readonly string? _nodeId;
    private readonly V2PathCurve? _curveToNext;
    private V2PathCurve? _oldCurve;
    private bool _captured;

    public SetPathCurveCommand(string noteId, V2PathCurve? curveToNext)
        : this(noteId, null, curveToNext)
    {
    }

    public SetPathCurveCommand(string noteId, string? nodeId, V2PathCurve? curveToNext)
    {
        _noteId = noteId;
        _nodeId = nodeId;
        _curveToNext = curveToNext;
    }

    public void Execute(EditorDocument document)
    {
        if (_curveToNext is not null && !Enum.IsDefined(_curveToNext.Value))
            throw new InvalidOperationException($"Unknown path curve '{_curveToNext}'.");
        var note = EditorCommandRules.RequirePathNote(document, _noteId);
        if (_nodeId is null)
        {
            var candidate = note.Clone();
            candidate.CurveToNext = _curveToNext;
            EditorCommandRules.ValidateNote(candidate);
            if (!_captured)
            {
                _oldCurve = note.CurveToNext;
                _captured = true;
            }
            note.CurveToNext = _curveToNext;
            document.Select(new EditorSelection(EditorSelectionKind.Note, _noteId));
            return;
        }

        var index = note.Nodes.FindIndex(node => node.Id == _nodeId);
        if (index < 0)
            throw new KeyNotFoundException($"Path node '{_nodeId}' was not found in note '{_noteId}'.");
        if (index == note.Nodes.Count - 1)
            throw new InvalidOperationException("Tail path node has no outgoing curve.");
        var node = note.Nodes[index];
        var nodeCandidate = note.Clone();
        nodeCandidate.Nodes[index].CurveToNext = _curveToNext;
        EditorCommandRules.ValidateNote(nodeCandidate);
        if (!_captured)
        {
            _oldCurve = node.CurveToNext;
            _captured = true;
        }
        node.CurveToNext = _curveToNext;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, _nodeId));
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Set path curve command has not executed.");
        var note = EditorCommandRules.RequirePathNote(document, _noteId);
        if (_nodeId is null)
        {
            note.CurveToNext = _oldCurve;
            document.Select(new EditorSelection(EditorSelectionKind.Note, _noteId));
        }
        else
        {
            document.RequirePathNode(_noteId, _nodeId).CurveToNext = _oldCurve;
            document.Select(new EditorSelection(EditorSelectionKind.PathNode, _nodeId));
        }
    }
}

public sealed class SetHoldJudgeCommand(string noteId, string nodeId, bool judge) : IEditorCommand
{
    private bool? _oldJudge;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        if (note.Type != V2NoteType.Hold)
            throw new InvalidOperationException("Only Hold path nodes carry judge state.");
        var index = note.Nodes.FindIndex(node => node.Id == nodeId);
        if (index < 0)
            throw new KeyNotFoundException($"Path node '{nodeId}' was not found in note '{noteId}'.");
        if (index == note.Nodes.Count - 1 && !judge)
            throw new InvalidOperationException("Hold tail must keep judge:true.");
        var node = note.Nodes[index];
        var candidate = note.Clone();
        candidate.Nodes[index].Judge = judge;
        EditorCommandRules.ValidateNote(candidate);
        if (!_captured)
        {
            _oldJudge = node.Judge;
            _captured = true;
        }
        node.Judge = judge;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Set Hold judge command has not executed.");
        document.RequirePathNode(noteId, nodeId).Judge = _oldJudge;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
    }
}

public sealed class EditPathTerminalCommand(
    string noteId,
    ExactBarTime time,
    double center,
    double width) : IEditorCommand
{
    private ExactBarTime _oldTime;
    private double _oldCenter;
    private double _oldWidth;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        var terminal = note.Nodes[^1];
        var previousTime = note.Nodes.Count == 1 ? note.Time : note.Nodes[^2].Time;
        EditorCommandRules.ValidateTime(time, "Path terminal time");
        EditorCommandRules.ValidatePoint(center, width, "Path terminal");
        if (time <= previousTime)
            throw new InvalidOperationException("Path terminal must be later than the previous point.");

        var candidate = note.Clone();
        candidate.Nodes[^1].Time = time;
        candidate.Nodes[^1].Center = center;
        candidate.Nodes[^1].Width = width;
        EditorCommandRules.ValidateNote(candidate);
        if (!_captured)
        {
            _oldTime = terminal.Time;
            _oldCenter = terminal.Center;
            _oldWidth = terminal.Width;
            _captured = true;
        }
        terminal.Time = time;
        terminal.Center = center;
        terminal.Width = width;
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Path terminal command has not executed.");
        var terminal = EditorCommandRules.RequirePathNote(document, noteId).Nodes[^1];
        terminal.Time = _oldTime;
        terminal.Center = _oldCenter;
        terminal.Width = _oldWidth;
    }
}
