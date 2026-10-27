using static EditorTestSupport;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

internal static class EditorCommandTests
{

    internal static void TestScrollEditingCommands()
    {
        var document = CreateCleanDocument("editor.scroll", "hard", "Scroll", "Community");
        AddTap(document, "scroll-main-note");
        Check(document.SelectedChart.ScrollSpeeds.Count == 0,
            "empty Scroll timeline keeps implicit speed one");
        ExpectException<InvalidOperationException>(() => document.Execute(
                new AddScrollCommand(ExactBarTime.One, 1)),
            "first explicit Scroll must start at bar zero");

        document.Execute(new AddScrollCommand(ExactBarTime.Zero, 1));
        document.Execute(new AddScrollCommand(ExactBarTime.FromFraction(2, 1), .75));
        document.Execute(new EditScrollCommand(ExactBarTime.Zero, ExactBarTime.Zero, 1,
            V2ScrollCurve.EaseInOutCubic));
        document.Execute(new AddScrollCommand(ExactBarTime.FromFraction(1, 1), 1.5,
            V2ScrollCurve.Hold));
        Check(document.SelectedChart.ScrollSpeeds.Select(item => item.Time).SequenceEqual(
                [ExactBarTime.Zero, ExactBarTime.One, ExactBarTime.FromFraction(2, 1)]),
            "Scroll commands maintain strict chronological order");
        Check(document.Selection.Kind == EditorSelectionKind.Scroll,
            "Scroll command selects Scroll kind");

        document.Execute(new EditScrollCommand(ExactBarTime.One,
            ExactBarTime.FromFraction(3, 2), 2, V2ScrollCurve.EaseOutQuad));
        var snapshot = document.BuildChartSnapshot("hard");
        Check(snapshot.ScrollSpeeds[1].Time == ExactBarTime.FromFraction(3, 2) &&
              snapshot.ScrollSpeeds[1].Value == 2 &&
              snapshot.ScrollSpeeds[1].CurveToNext == V2ScrollCurve.EaseOutQuad,
            "mutable EditableScroll edits reach strict snapshot");
        V2SemanticValidator.ValidateChart(snapshot, "scroll.json");
        var loaded = EditorDocument.FromPackage(document.PackageDirectory, document.BuildPackSnapshot(),
            new Dictionary<string, V2Chart>(StringComparer.Ordinal) { ["hard"] = snapshot });
        Check(loaded.SelectedChart.ScrollSpeeds is List<EditableScroll> &&
              loaded.SelectedChart.ScrollSpeeds[1].Time == ExactBarTime.FromFraction(3, 2) &&
              loaded.SelectedChart.ScrollSpeeds[1].Value == 2,
            "strict load creates mutable EditableScroll values");
        loaded.SelectedChart.ScrollSpeeds[1].Value = 2.25;
        Check(loaded.BuildChartSnapshot("hard").ScrollSpeeds[1].Value == 2.25,
            "loaded EditableScroll mutation reaches snapshot without immutable aliases");

        document.Execute(new DeleteScrollCommand(ExactBarTime.FromFraction(2, 1)));
        Check(document.SelectedChart.ScrollSpeeds[^1].CurveToNext is null,
            "deleting Scroll tail clears promoted tail curve");
        document.Undo();
        Check(document.SelectedChart.ScrollSpeeds[^2].CurveToNext == V2ScrollCurve.EaseOutQuad &&
              document.SelectedChart.ScrollSpeeds[^1].Time == ExactBarTime.FromFraction(2, 1),
            "undo Scroll tail deletion restores curve and event");
        document.Redo();
        document.Undo();
        document.Execute(new EditScrollCommand(ExactBarTime.FromFraction(3, 2),
            ExactBarTime.FromFraction(5, 4), 1.25, V2ScrollCurve.Linear));
        Check(!document.CanRedo && document.IsDirty,
            "new Scroll edit after undo clears redo and remains dirty");

        ExpectException<InvalidOperationException>(() => document.Execute(
                new EditScrollCommand(ExactBarTime.Zero, ExactBarTime.FromFraction(1, 4),
                    1, V2ScrollCurve.Linear)),
            "bar zero Scroll cannot move");
        ExpectException<InvalidOperationException>(() => document.Execute(
                new DeleteScrollCommand(ExactBarTime.Zero)),
            "bar zero Scroll cannot leave later events orphaned");
        ExpectException<InvalidOperationException>(() => document.Execute(
                new EditScrollCommand(ExactBarTime.FromFraction(2, 1),
                    ExactBarTime.FromFraction(2, 1), 65, null)),
            "Scroll rejects value above strict v2 maximum");

        var pack = document.BuildPackSnapshot();
        var secondEntry = pack.Charts[0] with { Id = "mega", File = "charts/mega.json" };
        var twoChart = EditorDocument.FromPackage(null, pack with
        {
            Charts = [pack.Charts[0], secondEntry],
        }, new Dictionary<string, V2Chart>(StringComparer.Ordinal)
        {
            ["hard"] = document.BuildChartSnapshot("hard"),
            ["mega"] = document.BuildChartSnapshot("hard") with { ChartId = "mega", ScrollSpeeds = [] },
        });
        twoChart.Execute(new EditScrollCommand(ExactBarTime.Zero, ExactBarTime.Zero, 1.1,
            V2ScrollCurve.Linear));
        twoChart.SelectChart("mega");
        twoChart.Undo();
        Check(twoChart.SelectedChartId == "hard" &&
              twoChart.SelectedChart.ScrollSpeeds[0].Value == 1,
            "Scroll undo follows owning chart");
        twoChart.Redo();
        Check(twoChart.SelectedChartId == "hard" &&
              twoChart.SelectedChart.ScrollSpeeds[0].Value == 1.1,
            "Scroll redo follows owning chart");
    }

    internal static void TestBpmCollisionIsRejected()
    {
        var document = CreateCleanDocument("editor.bpm", "hard", "BPM", "Community");
        document.Execute(new AddBpmCommand(ExactBarTime.FromFraction(1, 1), 180));
        var original = document.SelectedChart.Bpms.Single(item => item.Time == ExactBarTime.Zero);
        try
        {
            document.Execute(new EditBpmCommand(original.Time, ExactBarTime.FromFraction(1, 1), 200));
        }
        catch (InvalidOperationException)
        {
            Check(document.SelectedChart.Bpms.Count(item => item.Time == ExactBarTime.Zero) == 1 &&
                document.SelectedChart.Bpms.Single(item => item.Time == ExactBarTime.Zero).Bpm == 150,
                "BPM collision leaves original event unchanged");
            return;
        }
        throw new InvalidOperationException("BPM collision was accepted");
    }

    internal static void TestDirectEditingCommands()
    {
        var document = CreateCleanDocument(
            "editor.direct", "hard", "Direct Edit", "Community");
        document.Execute(new EditPackMetadataCommand("editor.renamed", "Renamed", "New Artist"));
        document.Execute(new EditPreviewCommand(true, null, 1.5, 8));
        Check(document.Preview is { StartSec: 1.5, DurationSec: 8 },
            "pack preview command edits interval");
        document.Undo();
        Check(document.Preview is null, "undo pack preview restores absent interval");
        document.Redo();
        ExpectException<V2DiagnosticException>(() => document.Execute(
            new EditPreviewCommand(true, null, -1, 8)),
            "preview command rejects negative start");
        Check(document.Preview is { StartSec: 1.5, DurationSec: 8 },
            "invalid preview edit restores prior interval");
        document.Undo();
        document.SelectedChart.Audio = "audio/override.wav";
        document.SelectedChart.Preview = new V2Preview(2, 3);
        document.Execute(new ClearChartOverridesCommand(document.SelectedChartId, true, true));
        Check(document.SelectedChart.Audio is null && document.SelectedChart.Preview is null,
            "clear chart overrides restores Pack inheritance");
        document.Undo();
        Check(document.SelectedChart.Audio == "audio/override.wav" &&
              document.SelectedChart.Preview is { StartSec: 2, DurationSec: 3 },
            "undo clear chart overrides restores explicit media");
        Check(document.PackId == "editor.renamed" && document.Title == "Renamed" &&
              document.Artist == "New Artist",
            "pack metadata command edits v2 metadata");
        document.Undo();
        Check(document.PackId == "editor.direct" && document.Title == "Direct Edit" &&
              document.Artist == "Community",
            "pack metadata undo restores v2 metadata");
        document.Redo();
        Check(document.PackId == "editor.renamed",
            "pack metadata redo restores edited identity");
        ExpectException<V2DiagnosticException>(() => document.Execute(
                new EditPackMetadataCommand("invalid pack id", "Broken", "Broken")),
            "pack metadata command rejects invalid v2 identity");
        Check(document.PackId == "editor.renamed" && document.Title == "Renamed",
            "invalid pack metadata edit restores prior document state");
        var hold = new EditableNote
        {
            Id = document.AllocateId("hold"),
            Type = V2NoteType.Hold,
            Track = EditorTrack.Center,
            Time = ExactBarTime.FromFraction(1, 1),
            Center = 1.5,
            Width = .75,
        };
        hold.Nodes.Add(new EditablePathNode
        {
            Id = document.AllocateId("hold-node"),
            Time = ExactBarTime.FromFraction(2, 1),
            Center = 2.0,
            Width = 1.0,
            Judge = true,
        });
        document.Execute(new AddNoteCommand(hold));
        document.Execute(new EditPathTerminalCommand(
            hold.Id, ExactBarTime.FromFraction(5, 2), 2.75, 1.25));
        var edited = document.SelectedChart.Notes.Single();
        Check(edited.Nodes[^1].Time == ExactBarTime.FromFraction(5, 2) &&
              edited.Nodes[^1].Center == 2.75 && edited.Nodes[^1].Width == 1.25,
            "hold terminal edit command");
        document.Undo();
        Check(edited.Nodes[^1].Time == ExactBarTime.FromFraction(2, 1) &&
              edited.Nodes[^1].Center == 2.0 && edited.Nodes[^1].Width == 1.0,
            "undo hold terminal edit");
        document.Redo();
        Check(edited.Nodes[^1].Time == ExactBarTime.FromFraction(5, 2),
            "redo hold terminal edit");

        document.Execute(new EditChartOffsetCommand(.015));
        Check(document.SelectedChart.AudioOffsetSec == .015,
            "chart offset command marks new value");
        document.Undo();
        Check(document.SelectedChart.AudioOffsetSec == 0,
            "undo chart offset command");
        document.Redo();
        Check(document.SelectedChart.AudioOffsetSec == .015,
            "redo chart offset command");

        document.Execute(new CompositeEditorCommand(
            new MoveNoteCommand(hold.Id, ExactBarTime.FromFraction(3, 2), 2.25, .875),
            new EditPathTerminalCommand(hold.Id,
                ExactBarTime.FromFraction(3, 1), 3.0, 1.5)));
        Check(edited.Time == ExactBarTime.FromFraction(3, 2) &&
              edited.Nodes[^1].Time == ExactBarTime.FromFraction(3, 1),
            "head and terminal update as one command");
        document.Undo();
        Check(edited.Time == ExactBarTime.FromFraction(1, 1) &&
              edited.Nodes[^1].Time == ExactBarTime.FromFraction(5, 2),
            "composite head and terminal undo together");

        try
        {
            document.Execute(new EditBpmCommand(ExactBarTime.Zero,
                ExactBarTime.FromFraction(1, 2), 200));
        }
        catch (InvalidOperationException)
        {
            Check(document.SelectedChart.Bpms.Count(item => item.Time == ExactBarTime.Zero) == 1 &&
                  document.SelectedChart.Bpms.Single(item => item.Time == ExactBarTime.Zero).Bpm == 150,
                "base BPM move is rejected without mutation");
        }

        try
        {
            document.Execute(new DeleteBpmCommand(ExactBarTime.Zero));
        }
        catch (InvalidOperationException)
        {
            Check(document.SelectedChart.Bpms.Count == 1 &&
                  document.SelectedChart.Bpms[0].Time == ExactBarTime.Zero,
                "base BPM deletion is rejected without mutation");
            return;
        }
        throw new InvalidOperationException("base BPM deletion was accepted");
    }

    internal static void TestCanvasAuthoringController()
    {
        var document = CreateCleanDocument("editor.canvas-adapter", "hard", "Adapter", "Community");
        var controller = new CanvasAuthoringController(document);

        var tapId = controller.AddNote(V2NoteType.Tap, EditorTrack.Center,
            ExactBarTime.FromFraction(1, 1), 2.5, 1);
        Check(document.SelectedChart.Notes.Count == 1 && document.IsDirty && document.CanUndo,
            "canvas add executes an undoable document command");

        controller.MoveNote(tapId, ExactBarTime.FromFraction(2, 1), 3.0, 2.0);
        var moved = document.SelectedChart.Notes.Single(note => note.Id == tapId);
        Check(moved.Time == ExactBarTime.FromFraction(2, 1) &&
              Math.Abs(moved.Center - 3.0) < 1e-12 && Math.Abs(moved.Width - 2.0) < 1e-12,
            "canvas move/resize updates the document note");

        var holdId = controller.AddNote(V2NoteType.Hold, EditorTrack.Left,
            ExactBarTime.FromFraction(3, 1), 1.0, 1.0, ExactBarTime.FromFraction(4, 1));
        var hold = document.SelectedChart.Notes.Single(note => note.Id == holdId);
        Check(hold.Nodes.Count == 1 && hold.Nodes[0].Time == ExactBarTime.FromFraction(4, 1) &&
              hold.Nodes[0].Judge == true,
            "canvas hold add creates a judged tail node");

        controller.EditPathNode(holdId, hold.Nodes[0].Id, ExactBarTime.FromFraction(4, 1), 2.0, 1.5);
        var resized = document.SelectedChart.Notes.Single(note => note.Id == holdId);
        Check(Math.Abs(resized.Nodes[0].Center - 2.0) < 1e-12 &&
              Math.Abs(resized.Nodes[0].Width - 1.5) < 1e-12,
            "canvas path resize updates the document tail node");

        controller.MoveNote(holdId, ExactBarTime.FromFraction(7, 1), 2.5, 1.5);
        var movedHold = document.SelectedChart.Notes.Single(note => note.Id == holdId);
        Check(movedHold.Time == ExactBarTime.FromFraction(7, 1) &&
              movedHold.Nodes[0].Time == ExactBarTime.FromFraction(8, 1),
            "canvas path move shifts the head and tail by the same exact delta");

        controller.AddBpm(ExactBarTime.FromFraction(5, 1), 180);
        controller.EditBpm(ExactBarTime.FromFraction(5, 1), ExactBarTime.FromFraction(6, 1), 200);
        Check(document.SelectedChart.Bpms.Any(item =>
                  item.Time == ExactBarTime.FromFraction(6, 1) && Math.Abs(item.Bpm - 200) < 1e-12),
            "canvas BPM add/edit flows through document commands");

        var snapshot = document.BuildChartSnapshot("hard");
        Check(snapshot.NotesCenter.Any(note => note.Id == tapId) &&
              snapshot.NotesLeft.Any(note => note.Id == holdId) &&
              snapshot.Bpms.Any(item => item.Time == ExactBarTime.FromFraction(6, 1) &&
                  Math.Abs(item.Bpm - 200) < 1e-12),
            "canvas edits feed BuildChartSnapshot serialization");

        controller.DeleteNotes([tapId]);
        Check(document.SelectedChart.Notes.All(note => note.Id != tapId),
            "canvas delete removes the document note");
        controller.Undo();
        Check(document.SelectedChart.Notes.Any(note => note.Id == tapId),
            "canvas undo restores the deleted note");
        controller.Redo();
        Check(document.SelectedChart.Notes.All(note => note.Id != tapId),
            "canvas redo removes the note again");
    }

    /// <summary>右键菜单步进器的步长与钳制规则（设计稿 Editor — Context Menu 的
    /// TIME/CENTER/WIDTH 行）。</summary>
    internal static void TestEditorPropertySteps()
    {
        var cursor = ExactBarTime.FromFraction(90, 1) + ExactBarTime.FromFraction(16, 32);
        Check(EditorPropertySteps.StepTime(cursor, 1, 32) == ExactBarTime.FromFraction(90 * 32 + 17, 32) &&
              EditorPropertySteps.StepTime(cursor, -1, 32) == ExactBarTime.FromFraction(90 * 32 + 15, 32),
            "menu TIME step advances and retreats exactly one grid cell");
        Check(EditorPropertySteps.StepTime(ExactBarTime.Zero, -1, 16) == ExactBarTime.Zero,
            "menu TIME step clamps at bar 0");
        Check(Math.Abs(EditorPropertySteps.StepCenter(2.5, .5, 1, false) - 2.6) < 1e-12 &&
              Math.Abs(EditorPropertySteps.StepCenter(2.5, .5, 1, true) - 2.51) < 1e-12 &&
              Math.Abs(EditorPropertySteps.StepCenter(2.5, .5, -1, false) - 2.4) < 1e-12,
            "menu CENTER step is 0.10, 0.01 while the fine step is held");
        Check(Math.Abs(EditorPropertySteps.StepCenter(.2, 1, -1, false) - .5) < 1e-12 &&
              Math.Abs(EditorPropertySteps.StepCenter(4.8, 1, 1, false) - 4.5) < 1e-12,
            "menu CENTER step keeps the object inside the chart coordinate domain");
        Check(Math.Abs(EditorPropertySteps.StepWidth(.5, 2.5, 1, false) - .55) < 1e-12 &&
              Math.Abs(EditorPropertySteps.StepWidth(.5, 2.5, -1, true) - .49) < 1e-12,
            "menu WIDTH step is 0.05, 0.01 while the fine step is held");
        Check(Math.Abs(EditorPropertySteps.StepWidth(.1, 2.5, -1, false) - .1) < 1e-12 &&
              Math.Abs(EditorPropertySteps.StepWidth(.5, .25, 1, false) - .5) < 1e-12,
            "menu WIDTH step stops at the 0.1 minimum and at the coordinate domain");
    }

    /// <summary>菜单步进/曲线/judge/删节点都必须是真实文档命令：一步一个可撤销历史，
    /// 违反 v2 路径规则的步进返回 false 且不改文档。</summary>
    internal static void TestMenuStepCommands()
    {
        var document = CreateCleanDocument("editor.menu-steps", "hard", "Menu", "Community");
        var controller = new CanvasAuthoringController(document);
        var tapId = controller.AddNote(V2NoteType.Tap, EditorTrack.Center,
            ExactBarTime.FromFraction(4, 1), 2.5, 1);
        var tap = document.SelectedChart.Notes.Single(note => note.Id == tapId);
        // A clean history baseline keeps "one step = one undo" observable.
        document.MarkSaved(Path.Combine(Path.GetTempPath(), "editor-menu-steps"));

        Check(controller.StepSelection(tapId, null, EditorStepProperty.Time, 1, 16, false),
            "menu TIME step runs a real move command");
        Check(tap.Time == ExactBarTime.FromFraction(65, 16),
            "menu TIME step moved the note one 1/16 grid cell");
        controller.Undo();
        Check(tap.Time == ExactBarTime.FromFraction(4, 1) && !document.IsDirty,
            "one undo after a menu TIME step returns to the saved note");
        Check(controller.StepSelection(tapId, null, EditorStepProperty.Center, 1, 16, false) &&
              Math.Abs(tap.Center - 2.6) < 1e-12,
            "menu CENTER step moves the note by 0.10 without touching time or width");
        Check(controller.StepSelection(tapId, null, EditorStepProperty.Width, 1, 16, false) &&
              Math.Abs(tap.Width - 1.05) < 1e-12,
            "menu WIDTH step resizes the note by 0.05 without touching time or center");
        Check(!controller.StepSelection(tapId, null, EditorStepProperty.Width, 0, 16, false),
            "menu step with direction 0 is a no-op instead of a command");

        var holdId = controller.AddNote(V2NoteType.Hold, EditorTrack.Center, ExactBarTime.Zero,
            1, 1, ExactBarTime.FromFraction(2, 1));
        var hold = document.SelectedChart.Notes.Single(note => note.Id == holdId);
        var tailId = hold.Nodes[0].Id;
        document.Execute(new AddPathNodeCommand(holdId, new EditablePathNode
        {
            Id = "hold-mid",
            Time = ExactBarTime.FromFraction(1, 1),
            Center = 1,
            Width = 1,
        }));
        var middle = hold.Nodes[0];
        Check(middle.Id == "hold-mid" && hold.Nodes.Count == 2,
            "menu node tests start from a two-node Hold path");
        Check(!controller.StepSelection(holdId, middle.Id, EditorStepProperty.Time, -1, 1, false) &&
              middle.Time == ExactBarTime.FromFraction(1, 1),
            "menu TIME step on a node is rejected instead of crossing the head");
        Check(!controller.StepSelection(holdId, middle.Id, EditorStepProperty.Time, 1, 1, false) &&
              middle.Time == ExactBarTime.FromFraction(1, 1),
            "menu TIME step on a node is rejected instead of crossing the next node");
        Check(controller.StepSelection(holdId, middle.Id, EditorStepProperty.Center, 1, 16, false) &&
              Math.Abs(middle.Center - 1.1) < 1e-12,
            "menu CENTER step moves the selected path node only");
        Check(controller.StepSelection(holdId, null, EditorStepProperty.Time, 1, 16, false) &&
              hold.Time == ExactBarTime.FromFraction(1, 16) &&
              middle.Time == ExactBarTime.FromFraction(17, 16) &&
              hold.Nodes[^1].Time == ExactBarTime.FromFraction(33, 16),
            "menu TIME step on a path head shifts the whole path like the drag gesture");

        controller.SetPathNodeCurve(holdId, middle.Id, V2PathCurve.Smooth);
        Check(middle.CurveToNext == V2PathCurve.Smooth && document.CanUndo,
            "menu CURVE TO NEXT runs a real curve command");
        controller.Undo();
        Check(middle.CurveToNext is null, "menu CURVE TO NEXT is undoable");
        ExpectException<InvalidOperationException>(
            () => controller.SetPathNodeCurve(holdId, tailId, V2PathCurve.Smooth),
            "tail node has no outgoing curve");

        controller.SetHoldJudge(holdId, middle.Id, false);
        Check(middle.Judge == false, "menu JUDGE toggle runs a real judge command on a Hold node");
        ExpectException<InvalidOperationException>(
            () => controller.SetHoldJudge(holdId, tailId, false),
            "Hold tail judge stays locked ON");

        controller.DeletePathNode(holdId, middle.Id);
        Check(hold.Nodes.Count == 1 && hold.Nodes[0].Id == tailId,
            "menu DELETE NODE runs a real path-node delete command");
        controller.Undo();
        Check(hold.Nodes.Count == 2 && hold.Nodes[0].Id == "hold-mid",
            "menu DELETE NODE is undoable");
        controller.DeletePathNode(holdId, middle.Id);
        ExpectException<InvalidOperationException>(() => controller.DeletePathNode(holdId, tailId),
            "a path keeps at least one node");
    }

    internal static void TestCanvasDragAnchor()
    {
        var document = CreateCleanDocument("editor.drag", "hard", "Drag", "Community");
        var controller = new CanvasAuthoringController(document);
        var original = ExactBarTime.FromFraction(1, 3);
        var bpms = document.SelectedChart.Bpms;
        var seconds = EditorTime.BarToSeconds(original, bpms);
        var id = controller.AddNote(V2NoteType.Tap, EditorTrack.Center, original, 2.5, 1);
        var note = document.SelectedChart.Notes.Single(item => item.Id == id);
        document.MarkSaved(Path.Combine(Path.GetTempPath(), "editor-drag-test"));
        // The pointer may grab anywhere inside the object rather than at its exact center/time.
        var anchor = new CanvasDragAnchor(original, seconds, 2.5, seconds + .01, 2.7);
        foreach (var snap in new[] { true, false })
        {
            var unchanged = anchor.TimeAt(seconds + .01, bpms, 32, snap, .001);
            controller.MoveNote(id, unchanged, anchor.CenterAt(2.7), 1);
            Check(note.Time == original && !document.IsDirty,
                $"canvas click preserves exact 1/3 and clean history with snap {snap}");
        }
        controller.MoveNote(id, anchor.TimeAt(seconds + .01, bpms, 32, true, .001),
            anchor.CenterAt(3.2), 1);
        Check(note.Time == original && Math.Abs(note.Center - 3.0) < 1e-12,
            "spatial drag preserves the original exact time and grab offset");
        controller.Undo();
        Check(!document.IsDirty && note.Center == 2.5,
            "one undo after spatial drag returns to the saved note");

        var holdId = controller.AddNote(V2NoteType.Hold, EditorTrack.Left,
            ExactBarTime.Zero, 1, 1, original);
        var tail = document.SelectedChart.Notes.Single(item => item.Id == holdId).Nodes[0];
        var tailAnchor = new CanvasDragAnchor(tail.Time, seconds, tail.Center, seconds + .02, 1.1);
        document.MarkSaved(Path.Combine(Path.GetTempPath(), "editor-drag-test"));
        controller.EditPathNode(holdId, tail.Id,
            tailAnchor.TimeAt(seconds + .02, bpms, 32, true, .001), tail.Center, tail.Width);
        Check(!document.IsDirty && tail.Time == original,
            "path-node click preserves its own exact time and clean history");
        controller.EditPathNode(holdId, tail.Id,
            tailAnchor.TimeAt(seconds + .42, bpms, 32, true, .001),
            tailAnchor.CenterAt(1.6), tail.Width);
        Check(tail.Time == ExactBarTime.FromFraction(19, 32) &&
              Math.Abs(tail.Center - 1.5) < 1e-12,
            "path-node drag applies pointer displacement to the node rather than the head");
    }
}
