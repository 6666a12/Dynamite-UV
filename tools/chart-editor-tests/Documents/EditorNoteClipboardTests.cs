using static EditorTestSupport;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

internal static class EditorNoteClipboardTests
{
    internal static void Run()
    {
        var document = CreateCleanDocument("clipboard.test", "hard", "Clipboard", "Community");
        var source = new EditableNote { Id = "hold", Type = V2NoteType.Hold, Track = EditorTrack.Left,
            Time = ExactBarTime.FromFraction(1, 3), Center = 2, Width = 1, ScrollSpeedMultiplier = 1.3,
            CurveToNext = V2PathCurve.Linear };
        source.Nodes.Add(new() { Id = "middle", Time = ExactBarTime.FromFraction(2, 3), Center = 3, Width = 2, Judge = false, CurveToNext = V2PathCurve.Linear });
        source.Nodes.Add(new() { Id = "tail", Time = ExactBarTime.One, Center = 1, Width = 1, Judge = true });
        document.Execute(new AddNoteCommand(source));
        document.Execute(new AddNoteCommand(new() { Id = "tap", Type = V2NoteType.Tap, Track = EditorTrack.Right,
            Time = ExactBarTime.FromFraction(3, 5), Center = 4, Width = 1 }));
        var clipboard = new EditorNoteClipboard();
        Check(clipboard.Copy(document, new[] { "tap", "hold" }) == 2, "clipboard captures full selected parents");
        var anchor = ExactBarTime.FromFraction(7, 5);
        var ids = clipboard.Paste(document, anchor);
        var pasted = document.SelectedChart.Notes.Single(n => n.Id == ids[0]);
        Check(pasted.Key != document.SelectedChart.Notes[0].Key && pasted.Nodes[0].Key != document.SelectedChart.Notes[0].Nodes[0].Key,
            "paste creates new editor identities");
        Check(pasted.Time == anchor && pasted.Nodes[0].Time == anchor + ExactBarTime.FromFraction(1, 3) &&
            pasted.Nodes[1].Time == anchor + ExactBarTime.FromFraction(2, 3), "paste retains exact path timing");
        Check(pasted.Nodes[0].Judge == false && pasted.Nodes[0].Center == 3 && pasted.Nodes[0].Width == 2 && pasted.ScrollSpeedMultiplier == 1.3,
            "paste preserves node flags geometry and parent speed");
        Check(document.SelectedChart.Notes.Single(n => n.Id == ids[1]).Time == anchor + ExactBarTime.FromFraction(4, 15),
            "mixed grids retain exact relative timing");
        document.Undo();
        Check(document.SelectedChart.Notes.Count == 2, "whole paste is one undo step");
        document.Redo();
        Check(document.SelectedChart.Notes.Count == 4 && document.SelectedChart.Notes.Any(n => n.Id == ids[0]), "redo preserves pasted IDs");
        var again = clipboard.Paste(document, anchor);
        Check(!again.Intersect(ids).Any(), "repeated paste has unique IDs");
        document.SelectedChart.Notes[0].Nodes[0].Center = 99;
        var other = CreateCleanDocument("clipboard.other", "hard", "Other", "Community");
        var otherIds = clipboard.Paste(other, ExactBarTime.Zero);
        Check(other.SelectedChart.Notes.Single(n => n.Id == otherIds[0]).Nodes[0].Center == 3,
            "clipboard is a detached snapshot and works across documents");
        Check(clipboard.Copy(other, Array.Empty<string>()) == 0 && clipboard.Count == 2, "empty copy leaves clipboard intact");
    }
}
