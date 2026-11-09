using static EditorTestSupport;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

internal static class EditorBatchEditTests
{
    internal static void Run()
    {
        var doc = CreateCleanDocument("batch.test", "hard", "Batch", "Community");
        var path = new EditableNote { Id = "hold", Type = V2NoteType.Hold, Track = EditorTrack.Left,
            Time = ExactBarTime.One, Center = .6, Width = 1 };
        path.Nodes.Add(new() { Id = "tail", Time = ExactBarTime.FromFraction(7, 3), Center = 2, Width = .5, Judge = true });
        doc.Execute(new AddNoteCommand(path));
        doc.Execute(new AddNoteCommand(new() { Id = "tap", Type = V2NoteType.Tap, Track = EditorTrack.Right,
            Time = ExactBarTime.FromFraction(5, 3), Center = 3, Width = 2 }));
        EditableNote Note(string id) => doc.SelectedChart.Notes.Single(n => n.Id == id);
        var ids = new[] { "hold", "tap" };
        doc.Execute(new EditNoteSelectionCommand(ids, new() { Time = ExactBarTime.FromFraction(1, 3), PositionDelta = .1 }));
        Check(Note("hold").Time == ExactBarTime.FromFraction(4, 3) && Note("tap").Time == ExactBarTime.FromFraction(2, 1), "batch preserves exact relative timing");
        Check(Note("hold").Nodes[0].Time == ExactBarTime.FromFraction(8, 3) && Note("hold").Nodes[0].Center == 2.1 && Note("hold").Center == .7, "batch moves whole path using decimal spatial math");
        doc.Undo();
        Check(Note("hold").Time == ExactBarTime.One && Note("hold").Nodes[0].Center == 2, "batch undo restores whole selection");
        doc.Redo(); doc.Undo();
        ExpectException<InvalidOperationException>(() => doc.Execute(new EditNoteSelectionCommand(ids, new() { Time = -ExactBarTime.FromFraction(2, 1) })), "negative batch time rejected");
        Check(Note("hold").Time == ExactBarTime.One, "failed batch leaves all originals intact");
        doc.Execute(new EditNoteSelectionCommand(ids, new() { Width = 1.2 }));
        Check(Note("hold").Width == 1.2 && Note("hold").Nodes[0].Width == .5, "head-only width preserves path widths");
        doc.Undo();
        doc.Execute(new EditNoteSelectionCommand(ids, new() { Width = .2, AddWidth = true, WholePath = true, Track = EditorTrack.Center }));
        Check(Note("hold").Nodes[0].Width == .7 && Note("hold").Nodes[0].Center == 2 && Note("tap").Track == EditorTrack.Center, "whole path width delta and non-mirrored track change");
        doc.Undo();
        ExpectException<InvalidOperationException>(() => doc.Execute(new EditNoteSelectionCommand(ids, new() { Type = V2NoteType.Drag, Track = EditorTrack.Center })), "mixed incompatible type group rejected atomically");
        Check(Note("tap").Track == EditorTrack.Right, "type rejection cannot partially change track");
        ExpectException<InvalidOperationException>(() => doc.Execute(new EditNoteSelectionCommand(ids, new() { Width = -.6, AddWidth = true, WholePath = true })), "invalid tail width rejects group");
        doc.Execute(new EditNoteSelectionCommand(ids, new() { Time = ExactBarTime.FromFraction(5, 1), MoveStart = true }));
        Check(Note("hold").Time == ExactBarTime.FromFraction(5, 1) && Note("tap").Time == ExactBarTime.FromFraction(17, 3), "selection start relocation preserves gaps");
    }
}
