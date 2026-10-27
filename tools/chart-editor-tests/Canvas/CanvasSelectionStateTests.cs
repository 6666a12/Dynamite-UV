using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class CanvasSelectionStateTests
{
    internal static void Run()
    {
        var state = new CanvasSelectionState();
        var time = ExactBarTime.FromFraction(1, 3);
        state.SelectNode("a", "node-a");
        Check(state.SingleNoteId == "a" && state.SelectedNodeId == "node-a" && state.EventTime is null,
            "secondary node selection belongs to a single note");
        state.SelectEvent(time);
        Check(state.SingleNoteId is null && state.NoteIds.Count == 0 && state.SelectedNodeId is null && state.EventTime == time,
            "BPM selection replaces note and secondary node selection");
        state.ToggleNote("a");
        state.ToggleNote("b");
        Check(state.NoteIds.Count == 2 && state.SingleNoteId is null && state.EventTime is null,
            "Shift toggle adds notes and excludes BPM selection");
        state.ToggleNote("b");
        Check(state.SingleNoteId == "a" && state.PrimaryNoteId == "b",
            "Shift removal preserves the remaining selection and legacy primary note");
        state.SelectNote("c");
        Check(state.SingleNoteId == "c" && state.NoteIds.Count == 1,
            "ordinary note selection replaces the prior selection");

        state.BeginMarquee(false);
        state.UpdateMarquee(2.99);
        Check(state.MarqueeActive && !state.MarqueeMoved && !state.MarqueeAdditive &&
              !state.FinishMarquee(["b"]) && state.NoteIds.Count == 0,
            "replacement marquee clears immediately but sub-3px movement does not select hits");
        state.BeginMarquee(false);
        state.UpdateMarquee(3);
        state.UpdateMarquee(0);
        Check(state.MarqueeMoved && state.FinishMarquee(["b"]) && !state.MarqueeActive && !state.MarqueeMoved,
            "marquee movement latches at exactly 3px and resets on release");
        state.BeginMarquee(true);
        state.UpdateMarquee(3);
        state.FinishMarquee(["b", "c"]);
        Check(state.MarqueeAdditive && state.NoteIds.Count == 2 && state.NoteIds.Contains("b") && state.NoteIds.Contains("c"),
            "additive marquee appends hits without duplicating prior selection");
        state.BeginMarquee(true);
        state.CancelMarquee();
        Check(!state.MarqueeActive && state.NoteIds.Count == 2, "cancelling a marquee leaves the selection intact");

        var document = CreateCleanDocument("selection", "hard", "Selection", "Community");
        var controller = new CanvasAuthoringController(document);
        var id = controller.AddNote(V2NoteType.Hold, EditorTrack.Center, time, 2.5, 1, ExactBarTime.FromFraction(2, 1));
        controller.AddBpm(time, 180);
        var projection = new CanvasProjection();
        projection.Rebuild(document.SelectedChart);
        var nodeId = projection.Notes.Single().Nodes.Single().Id;
        state.SelectNode(id, nodeId);
        controller.AddNote(V2NoteType.Tap, EditorTrack.Left, ExactBarTime.Zero, 2.5, 1);
        projection.Rebuild(document.SelectedChart);
        state.Restore(projection.Notes, projection.BpmEvents);
        Check(state.SingleNoteId == id && state.SelectedNodeId == nodeId &&
              projection.Notes.Single(note => note.Id == id).Nodes.Single().Id == nodeId,
            "rebuilt projection restores note and secondary node selection by ID");
        projection.Notes.Single(note => note.Id == id).Nodes.Clear();
        state.Restore(projection.Notes, projection.BpmEvents);
        Check(state.SingleNoteId == id && state.SelectedNodeId is null,
            "missing secondary node invalidates only node selection");
        state.SelectEvent(time);
        projection.Rebuild(document.SelectedChart);
        projection.BpmEvents.Reverse();
        state.Restore(projection.Notes, projection.BpmEvents);
        Check(state.EventTime == time && state.NoteIds.Count == 0,
            "BPM restoration uses exact fractional time rather than projection order");
        controller.DeleteBpm(time);
        projection.Rebuild(document.SelectedChart);
        state.Restore(projection.Notes, projection.BpmEvents);
        Check(state.EventTime is null, "removed BPM selection is invalidated on rebuild");
        state.SelectNode(id, nodeId);
        controller.DeleteNotes([id]);
        projection.Rebuild(document.SelectedChart);
        state.Restore(projection.Notes, projection.BpmEvents);
        Check(state.SingleNoteId is null && state.SelectedNodeId is null,
            "removed note invalidates its secondary selection on rebuild");
        state.SelectNode("a", "node-a");
        state.Clear();
        Check(state.NoteIds.Count == 0 && state.EventTime is null && state.SelectedNodeId is null,
            "clear removes all selection levels");
    }
}
