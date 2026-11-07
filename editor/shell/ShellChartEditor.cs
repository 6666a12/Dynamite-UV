using System.Text.Json;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Translates stage and inspector gestures into atomic document commands.</summary>
internal sealed class ShellChartEditor(
    IWorkbenchLocalizer text,
    Action documentChanged,
    Action<string> report)
{
    public void PlaceNote(EditorDocument? document, JsonElement data)
    {
        if (document is null) return;
        try
        {
            var type = (V2NoteType)data.GetProperty("type").GetInt32();
            var track = (EditorTrack)data.GetProperty("track").GetInt32();
            if (!Enum.IsDefined(type) || !Enum.IsDefined(track))
                throw new InvalidOperationException("Invalid Note type or track.");
            var rawTime = ExactTime(data.GetProperty("bar").GetDouble());
            var rawTail = ExactTime(data.GetProperty("tailBar").GetDouble());
            var snap = !data.TryGetProperty("snap", out var snapValue) || snapValue.GetBoolean();
            var time = snap ? document.Snap(rawTime) : rawTime;
            var tail = snap ? document.Snap(rawTail) : rawTail;
            var id = new CanvasAuthoringController(document).AddNote(type, track, time,
                data.GetProperty("center").GetDouble(), data.GetProperty("width").GetDouble(),
                type is V2NoteType.Hold or V2NoteType.Mixer ? tail : null);
            documentChanged();
            report(text.Format("Message.NotePlaced", id, ExactBarTimeText.Format(time)));
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void AddPathNode(EditorDocument? document, JsonElement data)
    {
        if (document is null) return;
        try
        {
            var noteId = data.GetProperty("noteId").GetString()!;
            var note = document.SelectedChart.Notes.Single(item => item.Id == noteId);
            var time = document.Snap(ExactTime(data.GetProperty("bar").GetDouble()));
            var shape = EditorPathProjection.ShapeOf(note);
            var (center, width) = EditorPathProjection.Sample(shape, time);
            var previous = note.Nodes.LastOrDefault(node => node.Time < time);
            var curve = previous?.CurveToNext ?? (previous is null && note.Time < time ? note.CurveToNext : null);
            var id = new CanvasAuthoringController(document).AddPathNode(noteId, time, center, width,
                curve, note.Type == V2NoteType.Hold ? true : null);
            documentChanged();
            report(text.Format("Message.PathNodeAdded", id));
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void MovePathPoint(EditorDocument? document, JsonElement data)
    {
        if (document is null) return;
        try
        {
            var noteId = data.GetProperty("noteId").GetString()!;
            var nodeId = data.TryGetProperty("nodeId", out var node) ? node.GetString() : null;
            var note = document.SelectedChart.Notes.Single(item => item.Id == noteId);
            var pathNode = nodeId is null ? null : note.Nodes.Single(item => item.Id == nodeId);
            var delta = ExactTime(data.GetProperty("endBar").GetDouble()) -
                ExactTime(data.GetProperty("startBar").GetDouble());
            var originalTime = pathNode?.Time ?? note.Time;
            var originalCenter = pathNode?.Center ?? note.Center;
            var width = pathNode?.Width ?? note.Width;
            var snap = !data.TryGetProperty("snap", out var snapValue) || snapValue.GetBoolean();
            var time = originalTime + EditorStagePlacement.ResolveMoveDelta(originalTime, delta,
                document.GridDivisor, snap && document.SnapEnabled);
            var positionStep = data.TryGetProperty("positionStep", out var step) ? step.GetDouble() : 0d;
            var center = originalCenter + EditorStagePlacement.ResolvePositionDelta(originalCenter,
                data.GetProperty("endCenter").GetDouble() - data.GetProperty("startCenter").GetDouble(), positionStep);
            var controller = new CanvasAuthoringController(document);
            if (nodeId is null) controller.MoveNote(noteId, time, center, width);
            else controller.EditPathNode(noteId, nodeId, time, center, width);
            documentChanged();
            report(text.Format("Message.PathPointMoved", nodeId ?? noteId, ExactBarTimeText.Format(time)));
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void ApplySpeed(EditorDocument? document, IReadOnlyList<string> selectedIds,
        string id, double speed)
    {
        if (document is null) return;
        try
        {
            var ids = selectedIds.Contains(id) ? selectedIds : new[] { id };
            var commands = ids.Distinct().Select(noteId => document.SelectedChart.Notes.Single(note => note.Id == noteId))
                .Where(note => note.ScrollSpeedMultiplier != speed)
                .Select(note => (IEditorCommand)new SetNoteScrollSpeedMultiplierCommand(note.Id, speed)).ToArray();
            if (commands.Length == 0) return;
            document.Execute(commands.Length == 1 ? commands[0] : new CompositeEditorCommand(commands));
            documentChanged();
            report(text.Format("Message.NoteSpeedSent", commands.Length == 1 ? id : $"{commands.Length} Notes", speed));
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void ApplyCurve(EditorDocument? document, string noteId, string? nodeId, V2PathCurve curve)
    {
        if (document is null) return;
        try
        {
            document.Execute(new SetPathCurveCommand(noteId, nodeId, curve));
            documentChanged();
            report(text.Format("Message.CurveApplied", nodeId ?? noteId, curve));
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void ApplyHoldJudge(EditorDocument? document, string noteId, string nodeId, bool judge)
    {
        if (document is null) return;
        try
        {
            document.Execute(new SetHoldJudgeCommand(noteId, nodeId, judge));
            documentChanged();
            report(text.Format("Message.HoldJudgeApplied", nodeId, judge));
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void NudgeSelection(EditorDocument? document, string? selectedNoteId,
        IReadOnlyList<string> selectedIds, int snapDenominator, int timeDirection, int positionDirection)
    {
        if (document is null || selectedNoteId is null ||
            timeDirection == 0 && positionDirection == 0) return;
        try
        {
            var timeDelta = ExactBarTime.FromFraction(timeDirection, snapDenominator);
            var centerDelta = positionDirection * .1;
            if (document.Selection is { Kind: EditorSelectionKind.PathNode, Id: { } nodeId })
            {
                var node = document.SelectedChart.Notes.Single(note => note.Id == selectedNoteId)
                    .Nodes.Single(item => item.Id == nodeId);
                var time = timeDirection == 0 ? node.Time : document.Snap(node.Time + timeDelta);
                var center = node.Center + centerDelta;
                document.Execute(new EditPathNodeCommand(selectedNoteId, nodeId, time, center, node.Width));
            }
            else
            {
                var notes = selectedIds.Distinct()
                    .Select(id => document.SelectedChart.Notes.Single(note => note.Id == id)).ToArray();
                if (MoveSelection(document, notes, timeDelta, centerDelta) == 0) return;
            }
            documentChanged();
            report(text["Message.SelectionNudged"]);
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void DeleteSelection(EditorDocument? document, string? selectedNoteId,
        IReadOnlyList<string> selectedIds)
    {
        if (document is null || selectedNoteId is null) return;
        try
        {
            var count = selectedIds.Count;
            if (document.Selection is { Kind: EditorSelectionKind.PathNode, Id: { } nodeId })
                document.Execute(new DeletePathNodeCommand(selectedNoteId, nodeId));
            else new CanvasAuthoringController(document).DeleteNotes(selectedIds);
            documentChanged();
            report(text.Format("Message.Deleted", count == 1 ? selectedNoteId : $"{count} Notes"));
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void ApplyGeometry(EditorDocument? document, string? noteId, string? nodeId,
        ExactBarTime time, double center, double width)
    {
        if (document is null || noteId is null) return;
        try
        {
            var note = document.SelectedChart.Notes.Single(item => item.Id == noteId);
            document.Execute(nodeId is not null
                ? new EditPathNodeCommand(noteId, nodeId, time, center, width)
                : note.Type is V2NoteType.Hold or V2NoteType.Mixer
                    ? new MovePathNoteCommand(noteId, time, center, width)
                    : new MoveNoteCommand(noteId, time, center, width));
            documentChanged();
            report(text.Format("Message.NotePropertiesApplied", nodeId ?? noteId));
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void ApplyNoteKind(EditorDocument? document, string? noteId,
        V2NoteType type, EditorTrack track)
    {
        if (document is null || noteId is null) return;
        try
        {
            var note = document.SelectedChart.Notes.Single(item => item.Id == noteId);
            var commands = new List<IEditorCommand>();
            if (note.Type != type) commands.Add(new ChangeNoteTypeCommand(noteId, type));
            if (note.Track != track) commands.Add(new SetNoteTrackCommand(noteId, track));
            if (commands.Count == 0) return;
            document.Execute(commands.Count == 1 ? commands[0] : new CompositeEditorCommand(commands.ToArray()));
            documentChanged();
            report(text.Format("Message.NotePropertiesApplied", noteId));
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void MoveNote(EditorDocument? document, IReadOnlyList<string> selectedIds, JsonElement data)
    {
        if (document is null) return;
        try
        {
            var noteId = data.GetProperty("noteId").GetString()!;
            var delta = ExactTime(data.GetProperty("endBar").GetDouble()) -
                ExactTime(data.GetProperty("startBar").GetDouble());
            var centerDelta = data.GetProperty("endCenter").GetDouble() -
                data.GetProperty("startCenter").GetDouble();
            var ids = selectedIds.Contains(noteId) ? selectedIds.Distinct().ToArray() : new[] { noteId };
            var notes = ids.Select(id => document.SelectedChart.Notes.Single(note => note.Id == id)).ToArray();
            var anchor = notes.Single(note => note.Id == noteId);
            var positionStep = data.TryGetProperty("positionStep", out var step) ? step.GetDouble() : 0d;
            centerDelta = EditorStagePlacement.ResolvePositionDelta(anchor.Center, centerDelta, positionStep);
            var snap = !data.TryGetProperty("snap", out var snapValue) || snapValue.GetBoolean();
            delta = EditorStagePlacement.ResolveMoveDelta(anchor.Time, delta,
                document.GridDivisor, snap && document.SnapEnabled);
            var moved = MoveSelection(document, notes, delta, centerDelta);
            if (moved == 0) return;
            documentChanged();
            report(text.Format("Message.NoteMoved", moved == 1 ? noteId : $"{moved} Notes",
                ExactBarTimeText.Format(ExactTime(data.GetProperty("endBar").GetDouble()))));
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void ResizeNote(EditorDocument? document, JsonElement data)
    {
        if (document is null) return;
        try
        {
            var noteId = data.GetProperty("noteId").GetString()!;
            var note = document.SelectedChart.Notes.Single(item => item.Id == noteId);
            var moveStartEdge = data.GetProperty("moveStartEdge").GetBoolean();
            var span = EditorStagePlacement.ResizeSnappedFromEdge(note.Center, note.Width,
                data.GetProperty("edge").GetDouble(), moveStartEdge,
                data.TryGetProperty("widthStep", out var step) ? step.GetDouble() : .05d);
            MoveNote(document, noteId, note.Time, span.Center, span.Width);
        }
        catch (Exception ex) { report(ex.Message); }
    }

    public void MoveNote(EditorDocument? document, string noteId, ExactBarTime time,
        double center, double width)
    {
        if (document is null) return;
        try
        {
            var note = document.SelectedChart.Notes.Single(item => item.Id == noteId);
            var snapped = note.Time == time ? note.Time : document.Snap(time);
            if (note.Time == snapped && note.Center == center && note.Width == width) return;
            document.Execute(note.Type is V2NoteType.Hold or V2NoteType.Mixer
                ? new MovePathNoteCommand(noteId, snapped, center, width)
                : new MoveNoteCommand(noteId, snapped, center, width));
            documentChanged();
            report(text.Format("Message.NoteMoved", noteId, ExactBarTimeText.Format(snapped)));
        }
        catch (Exception ex) { report(ex.Message); }
    }

    private int MoveSelection(EditorDocument document, IReadOnlyList<EditableNote> notes,
        ExactBarTime timeDelta, double centerDelta)
    {
        if (notes.Count == 0) return 0;

        var earliest = notes.Min(note => note.Time);
        if (timeDelta < ExactBarTime.Zero && earliest + timeDelta < ExactBarTime.Zero)
            timeDelta = ExactBarTime.Zero - earliest;

        var commands = new List<IEditorCommand>();
        foreach (var note in notes)
        {
            var time = note.Time + timeDelta;
            var center = note.Center + centerDelta;
            if (note.Time == time && note.Center == center) continue;
            commands.Add(note.Type is V2NoteType.Hold or V2NoteType.Mixer
                ? new MovePathNoteCommand(note.Id, time, center, note.Width)
                : new MoveNoteCommand(note.Id, time, center, note.Width));
        }
        if (commands.Count == 0) return 0;
        document.Execute(commands.Count == 1 ? commands[0] : new CompositeEditorCommand(commands.ToArray()));
        return commands.Count;
    }

    public void ApplyTiming(EditorDocument? document, IEditorCommand command)
    {
        if (document is null) return;
        try
        {
            document.Execute(command);
            documentChanged();
            report(text["Message.TimingApplied"]);
        }
        catch (Exception ex) { report(ex.Message); }
    }

    private static ExactBarTime ExactTime(double bar) =>
        ExactBarTime.FromFraction((long)Math.Round(Math.Max(0, bar) * 1_000_000d), 1_000_000);
}
