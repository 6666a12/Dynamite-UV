using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>
/// View/controller adapter that turns canvas authoring gestures into document commands.
/// The <see cref="EditorDocument"/> stays the single authoring authority: the canvas only
/// renders a projection and every mutation flows through undoable commands and dirty tracking.
/// </summary>
public sealed class CanvasAuthoringController
{
    public CanvasAuthoringController(EditorDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public EditorDocument Document { get; }
    public EditableChart Chart => Document.SelectedChart;
    public bool CanUndo => Document.CanUndo;
    public bool CanRedo => Document.CanRedo;

    /// <summary>Adds a note and returns its document-assigned id.</summary>
    public string AddNote(V2NoteType type, EditorTrack track, ExactBarTime time,
        double center, double width, ExactBarTime? tailTime = null,
        double scrollSpeedMultiplier = 1.0)
    {
        var id = Document.AllocateId(IdPrefix(type));
        var note = new EditableNote
        {
            Id = id,
            Type = type,
            Track = track,
            Time = time,
            Center = center,
            Width = width,
            ScrollSpeedMultiplier = scrollSpeedMultiplier,
        };
        if (type is V2NoteType.Hold or V2NoteType.Mixer)
        {
            var tail = tailTime ?? time;
            if (tail <= time)
                throw new InvalidOperationException("Path tail must be strictly after the head.");
            note.Nodes.Add(new EditablePathNode
            {
                Id = $"{id}-tail",
                Time = tail,
                Center = center,
                Width = width,
                Judge = type == V2NoteType.Hold ? true : null,
            });
        }
        Document.Execute(new AddNoteCommand(note));
        return id;
    }

    public void MoveNote(string noteId, ExactBarTime time, double center, double width)
    {
        var note = Document.RequireNote(noteId);
        if (note.Time == time && note.Center == center && note.Width == width)
            return;
        Document.Execute(note.Nodes.Count == 0
            ? new MoveNoteCommand(noteId, time, center, width)
            : new MovePathNoteCommand(noteId, time, center, width));
    }

    public void SetNoteScrollSpeedMultiplier(string noteId, double multiplier) =>
        Document.Execute(new SetNoteScrollSpeedMultiplierCommand(noteId, multiplier));

    /// <summary>在既有 Hold/Mixer 路径上插入一个控制节点（画布双击路径身体、菜单 ADD NODE
    /// 的公共入口）。center/width 由调用方按路径求值结果给出，所以插入点几何与路径逐位一致。
    /// 返回新节点 id；文档选中态切到该节点。</summary>
    public string AddPathNode(string noteId, ExactBarTime time, double center, double width,
        V2PathCurve? curveToNext = null, bool? judge = null)
    {
        var id = Document.AllocateId($"{noteId}-node");
        Document.Execute(new AddPathNodeCommand(noteId, new EditablePathNode
        {
            Id = id,
            Time = time,
            Center = center,
            Width = width,
            CurveToNext = curveToNext,
            Judge = judge,
        }));
        return id;
    }

    public void EditPathNode(string noteId, string nodeId, ExactBarTime time, double center,
        double width)
    {
        var node = Document.RequirePathNode(noteId, nodeId);
        if (node.Time == time && node.Center == center && node.Width == width)
            return;
        Document.Execute(new EditPathNodeCommand(noteId, nodeId, time, center, width));
    }

    /// <summary>
    /// 右键菜单 TIME/CENTER/WIDTH 步进器的命令入口。<paramref name="nodeId"/> 为 null 时步进
    /// 头部对象（Hold/Mixer 会连同整条路径平移），否则步进该路径节点。
    /// 返回 false 表示这一步会违反 v2 规则（节点时间不再严格递增、已经顶到钳制边界），
    /// 此时不产生命令、不产生历史，也不改 dirty。
    /// </summary>
    public bool StepSelection(string noteId, string? nodeId, EditorStepProperty property,
        int direction, int gridDivisor, bool fine)
    {
        if (direction == 0)
            return false;
        var note = Document.RequireNote(noteId);
        var index = nodeId is null ? -1 : note.Nodes.FindIndex(node => node.Id == nodeId);
        if (nodeId is not null && index < 0)
            throw new KeyNotFoundException($"Path node '{nodeId}' was not found in note '{noteId}'.");

        var time = note.Time;
        var center = note.Center;
        var width = note.Width;
        if (index >= 0)
        {
            time = note.Nodes[index].Time;
            center = note.Nodes[index].Center;
            width = note.Nodes[index].Width;
        }

        var steppedTime = time;
        var steppedCenter = center;
        var steppedWidth = width;
        switch (property)
        {
            case EditorStepProperty.Time:
                steppedTime = EditorPropertySteps.StepTime(time, direction, gridDivisor);
                break;
            case EditorStepProperty.Center:
                steppedCenter = EditorPropertySteps.StepCenter(center, width, direction, fine);
                break;
            default:
                steppedWidth = EditorPropertySteps.StepWidth(width, center, direction, fine);
                break;
        }

        if (index >= 0)
        {
            // A path node keeps strictly increasing times: the step is rejected at a neighbour
            // instead of being silently clamped, so the menu never rewrites an exact value.
            var previous = index == 0 ? note.Time : note.Nodes[index - 1].Time;
            var next = index + 1 < note.Nodes.Count
                ? (ExactBarTime?)note.Nodes[index + 1].Time
                : null;
            if (steppedTime <= previous || (next is { } upper && steppedTime >= upper))
                return false;
        }

        if (steppedTime == time && steppedCenter == center && steppedWidth == width)
            return false;
        if (index >= 0)
            EditPathNode(noteId, nodeId!, steppedTime, steppedCenter, steppedWidth);
        else
            MoveNote(noteId, steppedTime, steppedCenter, steppedWidth);
        return true;
    }

    /// <summary>Sets the outgoing curve of one path node; the tail node has none.</summary>
    public void SetPathNodeCurve(string noteId, string nodeId, V2PathCurve? curve) =>
        Document.Execute(new SetPathCurveCommand(noteId, nodeId, curve));

    /// <summary>Sets the Hold judge flag of one path node (the tail stays locked ON).</summary>
    public void SetHoldJudge(string noteId, string nodeId, bool judge) =>
        Document.Execute(new SetHoldJudgeCommand(noteId, nodeId, judge));

    public void DeletePathNode(string noteId, string nodeId) =>
        Document.Execute(new DeletePathNodeCommand(noteId, nodeId));

    public void DeleteNotes(IReadOnlyList<string> noteIds)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        if (noteIds.Count == 0)
            return;
        var commands = noteIds.Select(id => (IEditorCommand)new DeleteNoteCommand(id)).ToArray();
        Document.Execute(commands.Length == 1 ? commands[0] : new CompositeEditorCommand(commands));
    }

    public void AddBpm(ExactBarTime time, double bpm) =>
        Document.Execute(new AddBpmCommand(time, bpm));

    public void EditBpm(ExactBarTime originalTime, ExactBarTime newTime, double bpm) =>
        Document.Execute(new EditBpmCommand(originalTime, newTime, bpm));

    public void DeleteBpm(ExactBarTime time) =>
        Document.Execute(new DeleteBpmCommand(time));

    public void Undo() => Document.Undo();
    public void Redo() => Document.Redo();

    private static string IdPrefix(V2NoteType type) => type switch
    {
        V2NoteType.Hold => "hold",
        V2NoteType.Mixer => "mixer",
        V2NoteType.Drag => "drag",
        V2NoteType.ExTap => "extap",
        V2NoteType.Mine => "mine",
        V2NoteType.BarLine => "barline",
        _ => "tap",
    };
}
