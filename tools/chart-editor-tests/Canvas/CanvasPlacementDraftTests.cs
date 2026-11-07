using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class CanvasPlacementDraftTests
{
    internal static void Run()
    {
        var document = CreateCleanDocument("placement", "hard", "Placement", "Community");
        var controller = new CanvasAuthoringController(document);
        var draft = NewDraft(AuthoringNoteKind.Hold);
        Check(!draft.UpdateWidth(3.5, 2.99f) && draft.Width == 1 && !document.HasUnsavedChanges,
            "sub-threshold width drag keeps preview and document unchanged");
        Check(draft.UpdateWidth(3.5, 3f) && draft.Width == 2 && draft.Center == 1.5,
            "positive width drag retains the existing squared-delta curve");
        Check(draft.UpdateWidth(1.5, 0f) && draft.Width == .1 && Math.Abs(draft.Center - 2.45) < 1e-10,
            "active drag stays active below threshold and negative delta clamps to minimum width");
        draft.UpdateWidth(20, 5f);
        Check(draft.Width > 5 && Math.Abs(draft.Center + draft.Width / 2 - 2.5) < 1e-12,
            "wide overscan draft preserves its center anchor");
        draft.BeginTail();
        Check(draft.TailStage && draft.EndTime == draft.Time && !draft.UpdateWidth(3, 20),
            "Hold tail stage freezes head width and begins at the head time");
        draft.UpdateTail(-1, 16);
        Check(draft.EndTime == draft.Time, "tail cannot move before its head");
        draft.UpdateTail(100, 16);
        Check(draft.EndTime == 16 && !document.HasUnsavedChanges,
            "tail clamps to duration without committing a partial note");
        var head = ExactBarTime.FromFraction(1, 3);
        var tail = ExactBarTime.FromFraction(4, 3);
        var width = draft.Commit(controller, head, tail);
        var note = document.SelectedChart.Notes.Single();
        Check(width == draft.Width && width > 5 && note.Type == V2NoteType.Hold && note.Track == EditorTrack.Left &&
              note.Center == 2.5 && note.Time == head && note.Nodes.Single().Time == tail,
            "placement commits exact times and left-to-center geometry through the document command");
        controller.Undo();
        Check(!document.HasUnsavedChanges && document.SelectedChart.Notes.Count == 0,
            "a two-stage Hold is one undo step");

        var abandoned = NewDraft(AuthoringNoteKind.Chain);
        abandoned.UpdateWidth(4, 10);
        Check(!document.HasUnsavedChanges && document.SelectedChart.Notes.Count == 0,
            "discarding a placement draft creates no history or note");
        var tap = NewDraft(AuthoringNoteKind.Normal);
        tap.Commit(controller, head, null);
        Check(document.SelectedChart.Notes.Single().Type == V2NoteType.Tap &&
              document.SelectedChart.Notes.Single().Nodes.Count == 0,
            "single-click Tap does not require a width drag or create a tail");
    }

    private static CanvasPlacementDraft NewDraft(AuthoringNoteKind kind) => new()
    {
        Kind = kind, Side = 1, Time = 1, AnchorCenter = 2.5,
        BaseWidth = 1, Center = 2, Width = 1, EndTime = 1,
    };
}
