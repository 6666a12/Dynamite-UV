using System.Numerics;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class CanvasGesturePreviewTests
{
    internal static void Run()
    {
        TestMove();
        TestWidths();
        TestNodeTime();
        TestPreviewLifecycle();
    }

    private static void TestMove()
    {
        var document = CreateCleanDocument("gesture-preview-move", "hard", "Preview", "Community");
        var bpms = document.SelectedChart.Bpms;
        var time = ExactBarTime.FromFraction(1, 3);
        var end = ExactBarTime.FromFraction(4, 3);
        var seconds = EditorTime.BarToSeconds(time, bpms);
        var anchor = new CanvasDragAnchor(time, seconds, 2.5, seconds, 2.5);
        var stationary = CanvasGesturePreview.MoveNote(anchor, seconds + .005, bpms, 8, true,
            .01, time, end, .1);
        Check(stationary.Time == time && stationary.Delta == ExactBarTime.Zero,
            "spatial-only preview preserves an exact fraction even beyond the duration limit");

        var pointer = EditorTime.BarToSeconds(ExactBarTime.FromFraction(3, 5), bpms);
        var snapped = CanvasGesturePreview.MoveNote(anchor, pointer, bpms, 8, true, 0, time, time, 10);
        var free = CanvasGesturePreview.MoveNote(anchor, pointer, bpms, 8, false, 0, time, time, 10);
        Check(snapped.Time == ExactBarTime.FromFraction(5, 8) &&
              free.Time == ExactBarTime.FromFraction(3, 5),
            "ordinary move uses the existing anchor's snapped and free time rules");

        var upper = CanvasGesturePreview.MoveNote(anchor, 20, bpms, 8, false, 0, time, end, 3.2);
        var lower = CanvasGesturePreview.MoveNote(anchor, -20, bpms, 8, false, 0, time, end, 3.2);
        var oversized = CanvasGesturePreview.MoveNote(anchor, 20, bpms, 8, false, 0, time, end, .8);
        Check(upper.Time == ExactBarTime.One && lower.Time == ExactBarTime.Zero &&
              oversized.Time == ExactBarTime.Zero,
            "path duration limits the head and the negative latest-head limit still clamps to zero");
        var shifted = CanvasGesturePreview.ShiftPathTime(end, upper.Delta, bpms);
        var returned = CanvasGesturePreview.MoveNote(anchor, seconds, bpms, 8, true, .01,
            upper.Time, shifted.Time, 3.2);
        var restored = CanvasGesturePreview.ShiftPathTime(shifted.Time, returned.Delta, bpms);
        Check(shifted.Time == ExactBarTime.FromFraction(2, 1) && returned.Time == time &&
              restored.Time == end && restored.Seconds == EditorTime.BarToSeconds(end, bpms),
            "repeated head previews shift the entire path by the current exact delta without drift");
    }

    private static void TestWidths()
    {
        var leading = CanvasGesturePreview.ResizeNoteWidth(true, 1.47, 2, 2, 1);
        var trailing = CanvasGesturePreview.ResizeNoteWidth(false, 3.47, 2, 2, 1);
        var minimum = CanvasGesturePreview.ResizeNoteWidth(true, 5, 2, 2, 1);
        var maximum = CanvasGesturePreview.ResizeNoteWidth(false, 8, 2, 2, 1);
        Check(Math.Abs(leading.Left - 1.45) < 1e-12 && Math.Abs(leading.Width - 1.55) < 1e-12 &&
              Math.Abs(leading.Left + leading.Width - 3) < 1e-12 &&
              trailing.Left == 2 && Math.Abs(trailing.Width - 1.45) < 1e-12 &&
              minimum.Width == .1 && maximum.Left + maximum.Width == 5,
            "leading and trailing width previews retain their opposite edge, width snap and bounds");
        var absolute = CanvasGesturePreview.ResizeNodeWidth(3.47, 2, false, 1, 3);
        var relative = CanvasGesturePreview.ResizeNodeWidth(3.47, 2, true, .8, 2.5);
        Check(Math.Abs(absolute - 1.45) < 1e-12 && Math.Abs(relative - 1.75) < 1e-12 &&
              CanvasGesturePreview.ResizeNodeWidth(0, 2, false, 1, 3) == .1 &&
              CanvasGesturePreview.ResizeNodeWidth(8, 2, true, 1, 3) == 3,
            "node width supports absolute and insertion-relative drags with the same width limits");
    }

    private static void TestNodeTime()
    {
        var document = CreateCleanDocument("gesture-preview-node", "hard", "Preview", "Community");
        var bpms = document.SelectedChart.Bpms;
        var previous = ExactBarTime.FromFraction(1, 3);
        var time = ExactBarTime.FromFraction(4, 3);
        var next = ExactBarTime.FromFraction(7, 3);
        var gesture = Gesture(time, bpms, CanvasGestureKind.ResizeNode);
        var accepted = CanvasGesturePreview.MoveNodeTime(gesture, 2.4, bpms, 8, false, 0,
            previous, next, 10);
        Check(accepted is { } moved && moved.Time == ExactBarTime.FromFraction(3, 2),
            "node time preview accepts a time strictly between neighbours");
        Check(CanvasGesturePreview.MoveNodeTime(gesture, 0, bpms, 8, false, 0,
                  ExactBarTime.Zero, next, 10) is null &&
              CanvasGesturePreview.MoveNodeTime(gesture, 4, bpms, 8, false, 0,
                  previous, ExactBarTime.FromFraction(5, 2), 10) is null &&
              CanvasGesturePreview.MoveNodeTime(gesture, 4, bpms, 8, false, 0, previous, null, 3) is null,
            "invalid node times reject the update rather than resetting the last accepted preview");
        var widthGesture = Gesture(time, bpms, CanvasGestureKind.ResizeNode, resizeNodeWidth: true);
        var widthOnly = CanvasGesturePreview.MoveNodeTime(widthGesture, 20, bpms, 8, true, 0,
            previous, next, .1);
        Check(widthOnly is { } width && width.Time == time,
            "node-width drag retains its exact anchor time independently of pointer time and duration");
    }

    private static void TestPreviewLifecycle()
    {
        var document = CreateCleanDocument("gesture-preview-history", "hard", "Preview", "Community");
        var controller = new CanvasAuthoringController(document);
        var time = ExactBarTime.FromFraction(1, 3);
        var end = ExactBarTime.FromFraction(4, 3);
        var id = controller.AddNote(V2NoteType.Hold, EditorTrack.Center, time, 2.5, 1, end);
        document.MarkSaved(document.PackageDirectory!);
        var projection = new CanvasProjection();
        projection.Rebuild(document.SelectedChart);
        var note = projection.Notes.Single();
        var gesture = Gesture(time, document.SelectedChart.Bpms);
        Check(!gesture.Advance(2.99f) && gesture.Advance(3),
            "preview begins at the existing three-pixel threshold");
        var preview = CanvasGesturePreview.MoveNote(gesture.Anchor, 1.6, document.SelectedChart.Bpms,
            8, true, 0, note.TimeExact, note.EndExact, 10);
        note.TimeExact = preview.Time;
        (note.Nodes[0].TimeExact, note.Nodes[0].Time) = CanvasGesturePreview.ShiftPathTime(
            note.Nodes[0].TimeExact, preview.Delta, document.SelectedChart.Bpms);
        Check(!document.HasUnsavedChanges && document.SelectedChart.Notes.Single().Time == time &&
              gesture.Commit(controller, id, null, note.TimeExact, note.Center, note.Width) &&
              document.SelectedChart.Notes.Single().Nodes[0].Time == note.Nodes[0].TimeExact &&
              !gesture.Commit(controller, id, null, note.TimeExact, note.Center, note.Width),
            "preview leaves the document alone until the whole path commits exactly once");
        controller.Undo();
        Check(!document.HasUnsavedChanges && document.SelectedChart.Notes.Single().Time == time &&
              document.SelectedChart.Notes.Single().Nodes[0].Time == end,
            "one undo restores the exact pre-preview path");

        var cancelled = Gesture(time, document.SelectedChart.Bpms);
        cancelled.Advance(3);
        cancelled.Cancel();
        projection.Rebuild(document.SelectedChart);
        Check(!cancelled.Commit(controller, id, null, preview.Time, 2, 1) &&
              projection.Notes.Single().Nodes[0].TimeExact == end && !document.HasUnsavedChanges,
            "cancellation discards projection edits without creating document history");
        var noop = Gesture(time, document.SelectedChart.Bpms);
        noop.Advance(3);
        var original = CanvasGesturePreview.MoveNote(noop.Anchor, noop.Anchor.PointerSeconds,
            document.SelectedChart.Bpms, 8, true, .01, time, end, 10);
        Check(!noop.Commit(controller, id, null, original.Time, 2, 1) && !document.HasUnsavedChanges,
            "returning a preview to its anchor remains a no-op");
    }

    private static CanvasEditGesture Gesture(ExactBarTime time, IReadOnlyList<EditableBpm> bpms,
        CanvasGestureKind kind = CanvasGestureKind.Move, bool resizeNodeWidth = false)
    {
        var seconds = EditorTime.BarToSeconds(time, bpms);
        return new CanvasEditGesture
        {
            Kind = kind, NoteIndex = 0, NodeIndex = kind == CanvasGestureKind.ResizeNode ? 0 : -1,
            StartPoint = Vector2.Zero, Anchor = new(time, seconds, 2.5, seconds, 2.5),
            OriginalCenter = 2, OriginalWidth = 1, OriginalNodeCenter = 2, OriginalNodeWidth = 1,
            ResizeNodeWidth = resizeNodeWidth,
        };
    }
}
