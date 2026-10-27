using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class CanvasProjectionTests
{
    internal static void Run()
    {
        var document = CreateCleanDocument("projection", "hard", "Projection", "Community");
        var chart = document.SelectedChart;
        chart.Bpms.Clear();
        chart.Bpms.Add(new EditableBpm { Time = ExactBarTime.FromFraction(2, 1), Bpm = 60 });
        chart.Bpms.Add(new EditableBpm { Time = ExactBarTime.Zero, Bpm = 120 });
        var head = ExactBarTime.FromFraction(1, 3);
        var tail = ExactBarTime.FromFraction(7, 3);
        var controller = new CanvasAuthoringController(document);
        var id = controller.AddNote(V2NoteType.Hold, EditorTrack.Right, head, 2.5, 1, tail);
        var note = chart.Notes.Single();
        note.CurveToNext = V2PathCurve.EaseInQuad;
        note.Nodes[0].Center = 3;
        note.Nodes[0].Width = 2;
        note.Nodes[0].Judge = false;
        document.MarkSaved(document.PackageDirectory!);

        var projection = new CanvasProjection();
        var notes = projection.Notes;
        var bpms = projection.BpmEvents;
        projection.Rebuild(chart);
        var view = notes.Single();
        Check(view.Id == id && view.Kind == AuthoringNoteKind.Hold && view.Side == 2 &&
              view.TimeExact == head && view.EndExact == tail,
            "projection preserves identity, track and exact fractional head/tail times");
        Check(Math.Abs(view.Time - 2d / 3d) < 1e-10 && Math.Abs(view.EndTime - 16d / 3d) < 1e-10,
            "projection evaluates seconds across BPM changes");
        Check(view.Center == 2 && view.Width == 1 && view.Nodes[0].Center == 2 &&
              view.Nodes[0].Width == 2 && !view.Nodes[0].Judge &&
              view.CurveToNext == V2PathCurve.EaseInQuad,
            "projection keeps left-edge geometry and path curve/judge metadata");
        Check(bpms[0].TimeExact == ExactBarTime.Zero && bpms[0].Locked &&
              bpms[0].Id == "bpm-1" && !bpms[1].Locked && projection.DurationSeconds == 16,
            "BPM display is sorted and only its first event is locked; short charts retain 16s");

        view.Center = 0;
        view.Nodes[0].Width = .1;
        Check(note.Center == 2.5 && note.Nodes[0].Width == 2 && !document.HasUnsavedChanges,
            "preview mutations never change document data or dirty state");
        projection.Rebuild(chart);
        Check(ReferenceEquals(notes, projection.Notes) && ReferenceEquals(bpms, projection.BpmEvents) &&
              notes.Count == 1 && bpms.Count == 2 && notes[0].Center == 2,
            "rebuild reuses list identities and replaces stale preview values");

        controller.AddNote(V2NoteType.Tap, EditorTrack.Left, ExactBarTime.FromFraction(1000, 1), .5, 1);
        projection.Rebuild(chart);
        Check(projection.DurationSeconds == 600 && notes[1].Kind == AuthoringNoteKind.Normal &&
              notes[1].Side == 1, "long chart extent remains capped at 600s");
        controller.Undo();
        projection.Rebuild(chart);
        Check(projection.DurationSeconds == 16 && !document.HasUnsavedChanges,
            "rebuild after undo restores extent and leaves saved state intact");
    }
}
