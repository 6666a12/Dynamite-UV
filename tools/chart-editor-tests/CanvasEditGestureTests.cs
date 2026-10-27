using System.Numerics;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class CanvasEditGestureTests
{
    internal static void Run()
    {
        var document = CreateCleanDocument("gesture", "hard", "Gesture", "Community");
        var controller = new CanvasAuthoringController(document);
        var exact = ExactBarTime.FromFraction(1, 3);
        var id = controller.AddNote(V2NoteType.Hold, EditorTrack.Center, exact, 2.5, 1,
            ExactBarTime.FromFraction(4, 3));
        document.MarkSaved(document.PackageDirectory!);
        var note = document.SelectedChart.Notes.Single();
        var tail = note.Nodes.Single();
        var click = Gesture(exact);
        Check(!click.Advance(2.99f) && !click.Commit(controller, id, null, exact, 2, 1) &&
              !document.HasUnsavedChanges && note.Time == exact,
            "sub-threshold click preserves exact time and does not create history");

        var drag = Gesture(exact);
        Check(drag.Advance(3) && drag.Advance(0), "gesture crosses the existing 3px threshold and stays active");
        Check(drag.Commit(controller, id, null, exact, 2.2, 1) &&
              document.HasUnsavedChanges && note.Time == exact && note.Center == 2.7,
            "spatial drag commits once with exact original time and canvas left-to-center mapping");
        Check(!drag.Commit(controller, id, null, exact, 3, 1), "completed gesture cannot create a second undo step");
        controller.Undo();
        Check(!document.HasUnsavedChanges && note.Time == exact && note.Center == 2.5,
            "one undo restores the saved pre-gesture state");

        var noop = Gesture(exact);
        noop.Advance(8);
        Check(!noop.Commit(controller, id, null, exact, 2 + 5e-10, 1) && !document.HasUnsavedChanges,
            "returning to the original geometry within tolerance remains a no-op");
        var cancelled = Gesture(exact);
        cancelled.Advance(30);
        cancelled.Cancel();
        Check(!cancelled.Advance(40) && !cancelled.Commit(controller, id, null, exact, 3, 1) &&
              !document.HasUnsavedChanges, "cancelled gesture leaves the document and undo history unchanged");

        var nodeGesture = Gesture(tail.Time, node: true);
        nodeGesture.Advance(5);
        Check(nodeGesture.Commit(controller, id, tail.Id, tail.Time, 1.8, 1.4) &&
              tail.Width == 1.4 && tail.Center == 2.5 && note.Width == 1,
            "node width gesture only changes the selected node");
        controller.Undo();
        Check(!document.HasUnsavedChanges && tail.Width == 1,
            "node gesture commits a single reversible command");
        var timeDrag = Gesture(exact);
        timeDrag.Advance(5);
        var moved = ExactBarTime.FromFraction(2, 3);
        Check(timeDrag.Commit(controller, id, null, moved, 2, 1) &&
              note.Time == moved && note.Nodes[0].Time == ExactBarTime.FromFraction(5, 3),
            "path-head time drag still shifts the whole path through the existing command");
        controller.Undo();
        Check(!document.HasUnsavedChanges && note.Time == exact,
            "path time-drag undo returns to the exact saved fraction");
    }

    private static CanvasEditGesture Gesture(ExactBarTime time, bool node = false) => new()
    {
        Kind = node ? CanvasGestureKind.ResizeNode : CanvasGestureKind.Move,
        NoteIndex = 0, NodeIndex = node ? 0 : -1, StartPoint = Vector2.Zero,
        Anchor = new(time, 0, 2.5, 0, 2.5), OriginalCenter = 2, OriginalWidth = 1,
        OriginalNodeCenter = 2, OriginalNodeWidth = 1,
    };
}
