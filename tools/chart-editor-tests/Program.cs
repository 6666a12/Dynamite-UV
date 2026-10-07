using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

internal static class Program
{
    private static int Main()
    {
        try
        {
            TestExactSnap();
            TestSideTrackStagePlacement();
            TestExactBarTimeText();
            TestPropertyCatalog();
            TestDraftCreationAndValidation();
            TestDraftPersistenceState();
            TestCommandsAndSnapshots();
            TestAllSevenNoteTypes();
            TestPathEditingCommands();
            TestScrollEditingCommands();
            TestUndoFollowsOwningChart();
            TestChartLifecycleCommands();
            TestBpmCollisionIsRejected();
            TestDirectEditingCommands();
            TestCanvasAuthoringController();
            TestCanvasDragAnchor();
            TestEditorTimeConversion();
            TestGoldenRoundTrip();
            TestStrictPackageOpen();
            TestPackageWriter();
            TestSameDirectoryResourceReplacement();
            TestSameDirectoryPublicationRollback();
            TestSameDirectoryCleanupFailure();
            TestFirstPackageCreation();
            Console.WriteLine("EDITOR TESTS PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("EDITOR TESTS FAIL: " + exception);
            return 1;
        }
    }

    private static void TestExactSnap()
    {
        var input = ExactBarTime.FromJsonComponents(2, 1, 7);
        var snapped = EditorGeometry.Snap(input, 16);
        Check(snapped == ExactBarTime.FromJsonComponents(2, 1, 8), "exact arbitrary-denominator snap");
    }

    private static void TestSideTrackStagePlacement()
    {
        const double previewBar = 3d;
        const double visibleBars = 8d;
        var leftTime = EditorStagePlacement.TimeAt(EditorTrack.Left,
            GameplayStageGeometry.LeftLineX + GameplayStageGeometry.SidePreviewTravelPx / 2d,
            GameplayStageGeometry.SideY0, previewBar, visibleBars, 16, true);
        var rightTime = EditorStagePlacement.TimeAt(EditorTrack.Right,
            GameplayStageGeometry.RightLineX - GameplayStageGeometry.SidePreviewTravelPx / 2d,
            GameplayStageGeometry.SideY0, previewBar, visibleBars, 16, true);
        Check(leftTime == ExactBarTime.FromFraction(7, 1) && rightTime == leftTime,
            "left/right canvas placement maps horizontal travel to the same snapped time");

        var leftCenter = EditorStagePlacement.CenterAt(EditorTrack.Left,
            GameplayStageGeometry.LeftLineX, GameplayStageGeometry.SideY0 - 2.5d * GameplayStageGeometry.SideUnitPx);
        var rightCenter = EditorStagePlacement.CenterAt(EditorTrack.Right,
            GameplayStageGeometry.RightLineX, GameplayStageGeometry.SideY0 - 2.5d * GameplayStageGeometry.SideUnitPx);
        var sideWidth = EditorStagePlacement.WidthFromDrag(EditorTrack.Right, 0, 120, 0, 350);
        Check(Math.Abs(leftCenter - 2.5d) < .00001 && Math.Abs(rightCenter - 2.5d) < .00001 &&
              Math.Abs(sideWidth - 2d) < .00001,
            "left/right canvas placement maps vertical position and drag span to center and width");

        var centerResize = EditorStagePlacement.ResizeFromEdge(2.5d, 2d, .5d, true);
        var sideResize = EditorStagePlacement.ResizeFromEdge(2.5d, 2d, 4.5d, false);
        var minimumResize = EditorStagePlacement.ResizeFromEdge(2.5d, 2d, 3.4d, true);
        Check(Math.Abs(centerResize.Center - 2d) < .00001 && Math.Abs(centerResize.Width - 3d) < .00001 &&
              Math.Abs(sideResize.Center - 3d) < .00001 && Math.Abs(sideResize.Width - 3d) < .00001 &&
              Math.Abs(minimumResize.Center - 3.375d) < .00001 && Math.Abs(minimumResize.Width - .25d) < .00001,
            "width handles keep the opposite geometry edge fixed and enforce the minimum width");

        var document = CreateCleanDocument("editor.side-placement", "hard", "Side placement", "Community");
        document.Execute(new AddNoteCommand(new EditableNote
        {
            Id = "left-stage-note", Type = V2NoteType.Tap, Track = EditorTrack.Left,
            Time = leftTime, Center = leftCenter, Width = sideWidth,
        }));
        document.Execute(new AddNoteCommand(new EditableNote
        {
            Id = "right-stage-note", Type = V2NoteType.Drag, Track = EditorTrack.Right,
            Time = rightTime, Center = rightCenter, Width = sideWidth,
        }));
        var snapshot = document.BuildChartSnapshot("hard");
        Check(snapshot.NotesLeft.Single().Id == "left-stage-note" &&
              snapshot.NotesRight.Single().Id == "right-stage-note",
            "side-track placements serialize to notesLeft and notesRight rather than center");
    }

    private static void TestExactBarTimeText()
    {
        Check(ExactBarTimeText.Parse("42") == ExactBarTime.FromFraction(42, 1),
            "exact time parses integer");
        Check(ExactBarTimeText.Parse("2+3/7") == ExactBarTime.FromJsonComponents(2, 3, 7),
            "exact time parses mixed fraction");
        Check(ExactBarTimeText.Parse("17/7") == ExactBarTime.FromFraction(17, 7),
            "exact time parses improper fraction");
        Check(ExactBarTimeText.Parse("9007199254740990.125") ==
              ExactBarTime.FromFraction(
                  System.Numerics.BigInteger.Parse("72057594037927921"), 8),
            "finite decimal parses without binary64 loss");
        Check(ExactBarTimeText.Parse(".5") == ExactBarTime.FromFraction(1, 2) &&
              ExactBarTimeText.Parse("1.") == ExactBarTime.One,
            "finite decimal accepts omitted edge digits");
        var exact = ExactBarTime.FromJsonComponents(12, 5, 13);
        Check(ExactBarTimeText.Parse(ExactBarTimeText.Format(exact)) == exact &&
              ExactBarTimeText.Format(ExactBarTime.FromFraction(5, 13)) == "5/13",
            "exact time format round-trips canonical fractions");
        Check(!ExactBarTimeText.TryParse("-1", out _) &&
              !ExactBarTimeText.TryParse("1/0", out _) &&
              !ExactBarTimeText.TryParse("NaN", out _) &&
              !ExactBarTimeText.TryParse("Infinity", out _) &&
              !ExactBarTimeText.TryParse("9007199254740992", out _),
            "exact time rejects negative non-finite and non-v2-safe text");
    }

    private static void TestPropertyCatalog()
    {
        EditorPropertyCatalog.Validate();
        var expected = new[]
        {
            "pack.format", "pack.formatVersion", "pack.id", "pack.revision", "pack.title",
            "pack.artist", "pack.audio", "pack.cover", "pack.preview.startSec",
            "pack.preview.durationSec", "pack.charts[]", "charts[].id", "charts[].difficulty",
            "charts[].difficultyKey", "charts[].level", "charts[].unrated", "charts[].charters[]",
            "charts[].file", "charts[].audio", "charts[].preview.startSec",
            "charts[].preview.durationSec", "chart.format", "chart.formatVersion", "chart.chartId",
            "chart.audioOffsetSec", "chart.bpms[].time", "chart.bpms[].bpm",
            "chart.scrollSpeeds[].time", "chart.scrollSpeeds[].value", "chart.scrollSpeeds[].curveToNext",
            "chart.notesLeft[]", "chart.notesCenter[]", "chart.notesRight[]", "chart.notes[].id",
            "chart.notes[].type", "chart.notes[].time", "chart.notes[].center", "chart.notes[].width",
            "chart.notes[].curveToNext", "chart.notes[].nodes[]", "chart.notes[].nodes[].id",
            "chart.notes[].nodes[].time", "chart.notes[].nodes[].center", "chart.notes[].nodes[].width",
            "chart.notes[].nodes[].curveToNext", "chart.notes[].nodes[].judge",
            "chart.notes[].nodes[].judgeWasExplicit",
        };
        Check(EditorPropertyCatalog.All.Select(item => item.FieldPath).Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal)),
            "property catalog covers every v2 persisted and derived editor field");
        Check(EditorPropertyCatalog.Require("pack.format").ReadOnlyReason is not null &&
              EditorPropertyCatalog.Require("chart.audioOffsetSec").EditRoute == EditorPropertyEditRoute.EditorCommand &&
              EditorPropertyCatalog.Require("pack.audio").EditRoute == EditorPropertyEditRoute.PackageResourceTransaction,
            "property catalog distinguishes fixed, command, and transactional fields");
    }

    private static void TestAllSevenNoteTypes()
    {
        var document = CreateCleanDocument("editor.types", "hard", "Types", "Community");
        var types = Enum.GetValues<V2NoteType>();
        Check(types.Length == 7, "v2 exposes seven note types");
        for (var index = 0; index < types.Length; index++)
        {
            var type = types[index];
            var note = new EditableNote
            {
                Id = $"type-{index}",
                Type = type,
                Track = (EditorTrack)(index % 3),
                Time = ExactBarTime.FromFraction(index, 2),
                Center = 1 + index * .25,
                Width = .75,
            };
            if (type is V2NoteType.Hold or V2NoteType.Mixer)
            {
                note.CurveToNext = V2PathCurve.EaseInQuad;
                note.Nodes.Add(new EditablePathNode
                {
                    Id = $"type-{index}-tail",
                    Time = note.Time + ExactBarTime.FromFraction(1, 2),
                    Center = note.Center + .25,
                    Width = 1,
                    Judge = type == V2NoteType.Hold ? true : null,
                });
            }
            document.Execute(new AddNoteCommand(note));
        }

        var snapshot = document.BuildChartSnapshot("hard");
        V2SemanticValidator.ValidateChart(snapshot, "seven-types.json");
        var roundTrip = V2JsonDecoder.DecodeChart(V2JsonEncoder.EncodeChart(snapshot), "seven-types.json");
        Check(roundTrip.AllNotes.Select(item => item.Note.Type).Order()
                .SequenceEqual(types.Order()),
            "strict snapshot keeps all seven note types");

        document.Execute(new ChangeNoteTypeCommand("type-0", V2NoteType.Hold,
            new EditablePathNode
            {
                Id = "type-0-tail",
                Time = ExactBarTime.FromFraction(1, 4),
                Center = 1.25,
                Width = 1,
            }));
        var changed = document.RequireTestNote("type-0");
        Check(changed.Type == V2NoteType.Hold && changed.Nodes.Count == 1 &&
              changed.Nodes[0].Judge == true,
            "basic note converts to Hold with valid terminal semantics");
        document.Undo();
        Check(document.RequireTestNote("type-0").Type == V2NoteType.Tap &&
              document.RequireTestNote("type-0").Nodes.Count == 0,
            "undo restores basic note type exactly");
        document.Redo();
        document.Execute(new ChangeNoteTypeCommand("type-0", V2NoteType.Mixer));
        Check(document.RequireTestNote("type-0").Type == V2NoteType.Mixer &&
              document.RequireTestNote("type-0").Nodes.All(node => node.Judge is null),
            "Hold converts to Mixer and strips judge state");
        document.Execute(new ChangeNoteTypeCommand("type-0", V2NoteType.ExTap));
        Check(document.RequireTestNote("type-0").Nodes.Count == 0 &&
              document.RequireTestNote("type-0").CurveToNext is null,
            "path note converts to basic and strips path data");
    }

    private static void TestPathEditingCommands()
    {
        var document = CreateCleanDocument("editor.path", "hard", "Paths", "Community");
        var hold = new EditableNote
        {
            Id = "hold-path",
            Type = V2NoteType.Hold,
            Track = EditorTrack.Center,
            Time = ExactBarTime.Zero,
            Center = 2,
            Width = 1,
            CurveToNext = V2PathCurve.Linear,
        };
        hold.Nodes.Add(new EditablePathNode
        {
            Id = "hold-tail",
            Time = ExactBarTime.FromFraction(2, 1),
            Center = 3,
            Width = 1,
            Judge = true,
        });
        document.Execute(new AddNoteCommand(hold));
        document.Execute(new AddPathNodeCommand(hold.Id, new EditablePathNode
        {
            Id = "hold-mid",
            Time = ExactBarTime.FromFraction(1, 1),
            Center = 2.5,
            Width = .8,
            CurveToNext = V2PathCurve.EaseOutQuad,
            Judge = false,
        }));
        Check(document.RequireTestNote(hold.Id).Nodes.Select(node => node.Id)
                .SequenceEqual(["hold-mid", "hold-tail"]),
            "add path node inserts in strict time order");
        Check(document.Selection.Kind == EditorSelectionKind.PathNode && document.Selection.Id == "hold-mid" &&
              document.Selection.Key == document.RequireTestNote(hold.Id).Nodes.Single(node => node.Id == "hold-mid").Key,
            "path node selection uses stable ID");
        var nodeKey = document.RequireTestNote(hold.Id).Nodes.Single(node => node.Id == "hold-mid").Key;
        document.Execute(new RenamePathNodeCommand(hold.Id, "hold-mid", "hold-control"));
        Check(document.RequireTestNote(hold.Id).Nodes.Single(node => node.Id == "hold-control").Key == nodeKey,
            "path node identity command preserves stable key");
        document.Undo();
        document.Redo();
        document.Undo();

        document.Execute(new EditPathNodeCommand(hold.Id, "hold-mid",
            ExactBarTime.FromFraction(3, 2), 2.75, .9));
        document.Execute(new SetPathCurveCommand(hold.Id, V2PathCurve.Smooth));
        document.Execute(new SetPathCurveCommand(hold.Id, "hold-mid", V2PathCurve.Hold));
        document.Execute(new SetHoldJudgeCommand(hold.Id, "hold-mid", true));
        var edited = document.RequireTestNote(hold.Id);
        Check(edited.Nodes[0].Time == ExactBarTime.FromFraction(3, 2) &&
              edited.CurveToNext == V2PathCurve.Smooth &&
              edited.Nodes[0].CurveToNext == V2PathCurve.Hold &&
              edited.Nodes[0].Judge == true,
            "edit path node curve and Hold judge commands");

        ExpectException<InvalidOperationException>(() => document.Execute(
                new EditPathNodeCommand(hold.Id, "hold-mid",
                    ExactBarTime.FromFraction(5, 2), 2, 1)),
            "path node cannot cross its successor");
        ExpectException<InvalidOperationException>(() => document.Execute(
                new SetPathCurveCommand(hold.Id, "hold-tail", V2PathCurve.Linear)),
            "tail path node cannot gain an outgoing curve");
        ExpectException<InvalidOperationException>(() => document.Execute(
                new SetHoldJudgeCommand(hold.Id, "hold-tail", false)),
            "Hold tail cannot disable judge");

        document.Execute(new DeletePathNodeCommand(hold.Id, "hold-tail"));
        edited = document.RequireTestNote(hold.Id);
        Check(edited.Nodes.Count == 1 && edited.Nodes[0].Id == "hold-mid" &&
              edited.Nodes[0].CurveToNext is null && edited.Nodes[0].Judge == true,
            "deleting path tail promotes a valid Hold tail");
        V2SemanticValidator.ValidateChart(document.BuildChartSnapshot("hard"), "path.json");
        document.Undo();
        edited = document.RequireTestNote(hold.Id);
        Check(edited.Nodes.Count == 2 && edited.Nodes[0].CurveToNext == V2PathCurve.Hold &&
              edited.Nodes[0].Judge == true && edited.Nodes[1].Judge == true,
            "undo tail deletion restores path semantics");
        document.Redo();
        Check(document.RequireTestNote(hold.Id).Nodes.Count == 1,
            "redo tail deletion is stable");
        ExpectException<InvalidOperationException>(() => document.Execute(
                new DeletePathNodeCommand(hold.Id, "hold-mid")),
            "path cannot delete its only terminal");
    }

    private static void TestScrollEditingCommands()
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

    private static void TestDraftCreationAndValidation()
    {
        WithExternalResources((_, audio, cover) =>
        {
            var request = BuildDraftRequest(audio, cover) with
            {
                Difficulty = V2Difficulty.Custom,
                DifficultyKey = "SIGNAL",
                Level = null,
                Unrated = true,
                InitialBpm = 172.5,
                AudioOffsetSec = -.025,
                GridDivisor = 24,
            };
            var draft = EditorProjectDraftFactory.Create(request);
            var document = draft.Document;
            var pack = document.BuildPackSnapshot();
            var chart = document.BuildChartSnapshot(request.ChartId);
            Check(pack.Id == request.PackId && pack.Title == request.Title &&
                  pack.Artist == request.Artist && pack.Revision == 1,
                "typed draft keeps pack identity");
            Check(pack.Charts.Single().Difficulty == V2Difficulty.Custom &&
                  pack.Charts.Single().DifficultyKey == "SIGNAL" &&
                  pack.Charts.Single().Unrated && pack.Charts.Single().Level is null &&
                  pack.Charts.Single().Charters.SequenceEqual([request.Charter]),
                "typed draft keeps chart metadata");
            Check(draft.ChartDestination == $"charts/{request.ChartId}.json" &&
                  pack.Charts.Single().File == draft.ChartDestination &&
                  draft.AudioDestination == "audio.wav" && pack.Audio == "audio.wav" &&
                  draft.CoverDestination == "cover.png" && pack.Cover == "cover.png",
                "draft uses deterministic safe package destinations");
            Check(document.GridDivisor == 24 && chart.AudioOffsetSec == -.025 &&
                  chart.Bpms.Count == 1 && chart.Bpms[0] == new V2BpmEvent(ExactBarTime.Zero, 172.5),
                "draft keeps timing and grid settings");
            Check(chart.NotesLeft.Count == 0 && chart.NotesCenter.Count == 0 &&
                  chart.NotesRight.Count == 0 && document.SelectedChart.Notes.Count == 0,
                "draft never synthesizes a note");
            Check(draft.ExternalResources.Count == 2 &&
                  draft.ExternalResources[draft.AudioDestination] == Path.GetFullPath(audio) &&
                  draft.ExternalResources[draft.CoverDestination!] == Path.GetFullPath(cover),
                "draft exposes external resource imports");

            ExpectDiagnostic(() => EditorProjectDraftFactory.Create(request with
                {
                    Difficulty = V2Difficulty.Hard,
                    DifficultyKey = "forbidden",
                }), "new-project", "/charts/0/difficultyKey",
                "draft applies standard difficulty semantics");
            ExpectDiagnostic(() => EditorProjectDraftFactory.Create(request with
                {
                    Level = 0,
                    Unrated = false,
                }), "new-project", "/charts/0/level",
                "draft applies level-or-unrated semantics");
            ExpectDiagnostic(() => EditorProjectDraftFactory.Create(request with
                {
                    GridDivisor = 0,
                }), "new-project", "/gridDivisor",
                "draft rejects invalid grid divisor");
            ExpectDiagnostic(() => EditorProjectDraftFactory.Create(request with
                {
                    ExternalAudioSource = Path.Combine(Path.GetDirectoryName(audio)!, "missing.wav"),
                }), "new-project", "/audioSource",
                "draft rejects missing external audio");
            ExpectDiagnostic(() => EditorProjectDraftFactory.Create(request with
                {
                    ExternalCoverSource = Path.Combine(Path.GetDirectoryName(audio)!, "missing.png"),
                }), "new-project", "/coverSource",
                "draft rejects missing external cover");
        });
    }

    private static void TestDraftPersistenceState()
    {
        WithExternalResources((root, audio, _) =>
        {
            var draft = EditorProjectDraftFactory.Create(BuildDraftRequest(audio));
            var document = draft.Document;
            Check(document.IsUnpersisted && document.HasUnsavedChanges && document.IsDirty &&
                  !document.CanUndo && document.PackageDirectory is null,
                "new draft is unsaved without history");
            document.MarkSaved(Path.Combine(root, "persisted"));
            Check(!document.IsUnpersisted && !document.HasUnsavedChanges && !document.IsDirty &&
                  document.PackageDirectory == Path.GetFullPath(Path.Combine(root, "persisted")),
                "MarkSaved makes draft persisted and clean");
            AddTap(document, "draft-tap");
            Check(document.HasUnsavedChanges && document.IsDirty,
                "edit after MarkSaved is unsaved");
            document.Undo();
            Check(!document.HasUnsavedChanges && !document.IsDirty,
                "undo to saved draft history is clean");
        });

        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack");
        var opened = EditorPackageRepository.Open(fixture);
        Check(!opened.IsUnpersisted && !opened.HasUnsavedChanges && !opened.IsDirty,
            "opened document retains clean history semantics");
        AddDirtyNote(opened);
        Check(opened.HasUnsavedChanges, "opened document edit is unsaved");
        opened.Undo();
        Check(!opened.HasUnsavedChanges, "opened document undo returns to clean history");
    }

    private static void TestCommandsAndSnapshots()
    {
        var document = CreateCleanDocument("editor.test", "hard", "Editor Test", "Community");
        var note = new EditableNote
        {
            Id = document.AllocateId("tap"),
            Type = V2NoteType.Tap,
            Track = EditorTrack.Center,
            Time = ExactBarTime.FromJsonComponents(0, 1, 5),
            Center = 2.5,
            Width = 1.0,
        };
        document.Execute(new AddNoteCommand(note));
        Check(document.SelectedChart.Notes.Count == 1 && document.CanUndo, "add note command");
        Check(document.Selection.Key == document.SelectedChart.Notes.Single().Key,
            "note selection resolves stable internal key");
        var noteKey = document.SelectedChart.Notes.Single().Key;
        document.Execute(new RenameNoteCommand(note.Id, "renamed-note"));
        Check(document.SelectedChart.Notes.Single().Id == "renamed-note" &&
              document.SelectedChart.Notes.Single().Key == noteKey,
            "note identity command preserves stable key");
        document.Undo();
        Check(document.SelectedChart.Notes.Single().Id == note.Id && document.SelectedChart.Notes.Single().Key == noteKey,
            "undo note identity restores serialized id");
        document.Redo();
        document.Execute(new SetNoteTrackCommand("renamed-note", EditorTrack.Left));
        Check(document.SelectedChart.Notes.Single().Track == EditorTrack.Left,
            "note track command edits owning track");
        document.Undo();
        Check(document.SelectedChart.Notes.Single().Track == EditorTrack.Center,
            "undo note track restores owning track");
        document.Redo();
        document.Undo();
        document.Undo();
        document.Execute(new MoveNoteCommand(note.Id, ExactBarTime.FromJsonComponents(1, 1, 7), 5.1, .6));
        Check(document.SelectedChart.Notes[0].Center == 5.1, "move command");
        document.Undo();
        Check(document.SelectedChart.Notes[0].Center == 2.5, "undo move command");
        document.Undo();
        Check(!document.IsDirty, "undo back to saved history is clean");
        document.Redo();
        document.Redo();
        Check(document.SelectedChart.Notes[0].Time == ExactBarTime.FromJsonComponents(1, 1, 7), "redo move command");

        var chart = document.BuildChartSnapshot("hard");
        V2SemanticValidator.ValidateChart(chart, "chart.json");
        Check(EditorValidationService.Validate(document).Any(item => item.Severity == V2DiagnosticSeverity.Warning),
            "overscan produces warning without changing v2 data");
    }

    private static void TestUndoFollowsOwningChart()
    {
        var first = CreateCleanDocument("editor.history", "hard", "History", "Community");
        var pack = first.BuildPackSnapshot();
        var secondEntry = pack.Charts[0] with { Id = "mega", File = "chart_mega.json" };
        var twoChartPack = pack with { Charts = [pack.Charts[0], secondEntry] };
        var charts = new Dictionary<string, V2Chart>(StringComparer.Ordinal)
        {
            ["hard"] = first.BuildChartSnapshot("hard"),
            ["mega"] = first.BuildChartSnapshot("hard") with { ChartId = "mega" },
        };
        var document = EditorDocument.FromPackage(null, twoChartPack, charts);
        var note = new EditableNote
        {
            Id = document.AllocateId("tap"),
            Type = V2NoteType.Tap,
            Track = EditorTrack.Center,
            Time = ExactBarTime.Zero,
            Center = 2.5,
            Width = 1.0,
        };
        document.Execute(new AddNoteCommand(note));
        document.SelectChart("mega");
        document.Undo();
        Check(document.SelectedChartId == "hard" && document.Charts.Single(chart => chart.Id == "hard").Notes.Count == 0,
            "undo switches to command-owning chart");
        document.SelectChart("mega");
        document.Redo();
        Check(document.SelectedChartId == "hard" && document.Charts.Single(chart => chart.Id == "hard").Notes.Count == 1,
            "redo switches to command-owning chart");
    }

    private static void TestBpmCollisionIsRejected()
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

    private static void TestChartLifecycleCommands()
    {
        var document = CreateCleanDocument("editor.charts", "hard", "Charts", "Community");
        document.Execute(new AddNoteCommand(new EditableNote
        {
            Id = "hard-tap", Type = V2NoteType.Tap, Track = EditorTrack.Center,
            Time = ExactBarTime.Zero, Center = 2.5, Width = 1,
        }));
        document.Execute(new EditChartDetailsCommand("hard", V2Difficulty.Mega, null, 15, false,
            ["Lead Charter", "Co-Charter"]));
        Check(document.SelectedChart.Difficulty == V2Difficulty.Mega && document.SelectedChart.Level == 15 &&
              !document.SelectedChart.Unrated && document.SelectedChart.Charters.Count == 2,
            "chart metadata command edits difficulty level and credits");
        document.Undo();
        Check(document.SelectedChart.Difficulty == V2Difficulty.Hard && document.SelectedChart.Unrated &&
              document.SelectedChart.Charters.SequenceEqual(["Unknown"]),
            "undo chart metadata restores entry details");
        document.Redo();
        ExpectException<V2DiagnosticException>(() => document.Execute(
            new EditChartDetailsCommand("hard", V2Difficulty.Custom, null, 15, false, ["Community"])),
            "chart metadata rejects custom difficulty without key");
        Check(document.SelectedChart.Difficulty == V2Difficulty.Mega && document.SelectedChart.Level == 15,
            "invalid chart metadata restores prior entry details");
        document.Execute(new RenameChartCommand("hard", "mega-chart", "charts/mega-chart.json"));
        var renamedKey = document.SelectedChart.Key;
        Check(document.SelectedChartId == "mega-chart" && document.SelectedChart.Id == "mega-chart" &&
              document.SelectedChart.File == "charts/mega-chart.json",
            "chart identity command edits id and storage path");
        document.Undo();
        Check(document.SelectedChartId == "hard" && document.SelectedChart.File == "charts/hard.json" &&
              document.SelectedChart.Key == renamedKey,
            "undo chart identity restores id and storage path");
        document.Redo();
        Check(document.SelectedChartId == "mega-chart" && document.SelectedChart.File == "charts/mega-chart.json" &&
              document.SelectedChart.Key == renamedKey,
            "redo chart identity restores renamed chart");
        document.Undo();
        WithExternalResources((root, audio, cover) =>
        {
            document.Execute(new ReplaceChartResourceCommand("hard", true, false, "audio/replaced.wav", audio));
            Check(document.Audio == "audio/replaced.wav" &&
                  document.PendingResourceReplacements.Any(item => item.PackageRelativePath == "audio/replaced.wav"),
                "resource replacement stages pack audio");
            document.Undo();
            Check(document.Audio == "audio.wav" && document.PendingResourceReplacements.Count == 0,
                "undo resource replacement restores pack audio");
            document.Redo();
            Check(document.Audio == "audio/replaced.wav",
                "redo resource replacement restores staged audio");
            document.Undo();
        });
        var normal = new EditableChart
        {
            Id = "normal", Difficulty = V2Difficulty.Normal, Unrated = true,
            File = "charts/normal.json", AudioOffsetSec = 0,
        };
        normal.Charters.Add("Community");
        normal.Bpms.Add(new EditableBpm { Time = ExactBarTime.Zero, Bpm = 150 });
        document.Execute(new AddChartCommand(normal));
        Check(document.Charts.Select(chart => chart.Id).SequenceEqual(["hard", "normal"]) &&
              document.SelectedChartId == "normal", "add chart command selects new chart");
        document.Undo();
        Check(document.Charts.Count == 1 && document.SelectedChartId == "hard",
            "undo add chart restores prior selection");
        document.Redo();
        Check(document.Charts.Count == 2 && document.SelectedChartId == "normal",
            "redo add chart restores new chart");

        document.Execute(new AddNoteCommand(new EditableNote
        {
            Id = "normal-tap", Type = V2NoteType.Tap, Track = EditorTrack.Center,
            Time = ExactBarTime.Zero, Center = 2.5, Width = 1,
        }));
        document.Execute(new CopyChartCommand("normal", "mega", "charts/mega.json"));
        var copy = document.Charts.Single(chart => chart.Id == "mega");
        Check(copy.Notes.Single().Id == "normal-tap" && document.SelectedChartId == "mega",
            "copy chart preserves authored data in a separate chart");
        document.Undo();
        Check(!document.Charts.Any(chart => chart.Id == "mega") && document.SelectedChartId == "normal",
            "undo copied chart restores source selection");
        document.Redo();
        Check(document.Charts.Any(chart => chart.Id == "mega") && document.SelectedChartId == "mega",
            "redo copied chart restores copied chart selection");
        document.Execute(new MoveChartCommand("mega", 0));
        Check(document.Charts[0].Id == "mega", "move chart command reorders metadata");
        document.Undo();
        Check(document.Charts.Select(chart => chart.Id).SequenceEqual(["hard", "normal", "mega"]),
            "undo chart reorder restores original order");
        document.Execute(new DeleteChartCommand("normal"));
        Check(!document.Charts.Any(chart => chart.Id == "normal") && document.Charts.Count == 2,
            "delete chart command removes selected chart");
        document.Undo();
        Check(document.Charts.Select(chart => chart.Id).SequenceEqual(["hard", "normal", "mega"]),
            "undo delete chart restores chart and order");
        ExpectException<InvalidOperationException>(() =>
        {
            var only = CreateCleanDocument("editor.only", "hard", "Only", "Community");
            only.Execute(new DeleteChartCommand("hard"));
        }, "cannot delete last chart");
        V2SemanticValidator.ValidatePack(document.BuildPackSnapshot());
        foreach (var chart in document.Charts)
            V2SemanticValidator.ValidateChart(document.BuildChartSnapshot(chart.Id), chart.File);
        Check(true, "chart lifecycle snapshots remain strict");
    }

    private static void TestDirectEditingCommands()
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

    private static void TestCanvasAuthoringController()
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

    private static void TestCanvasDragAnchor()
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

    private static void TestEditorTimeConversion()
    {
        var bpms = new List<EditableBpm>
        {
            new() { Time = ExactBarTime.Zero, Bpm = 150 },
            new() { Time = ExactBarTime.FromFraction(4, 1), Bpm = 300 },
        };
        Check(Math.Abs(EditorTime.BarToSeconds(ExactBarTime.FromFraction(2, 1), bpms) - 3.2) < 1e-12,
            "editor time converts bars to seconds before a BPM change");
        Check(Math.Abs(EditorTime.BarToSeconds(ExactBarTime.FromFraction(5, 1), bpms) - 7.2) < 1e-12,
            "editor time converts bars to seconds after a BPM change");
        Check(EditorTime.SecondsToBar(3.2, bpms) == ExactBarTime.FromFraction(2, 1),
            "editor time inverts seconds to an exact bar");
        Check(EditorTime.SnapSeconds(3.2, bpms, 32, true) == ExactBarTime.FromFraction(2, 1),
            "editor time snaps display seconds to the exact grid");

        var fastBpms = new List<EditableBpm>
        {
            new() { Time = ExactBarTime.Zero, Bpm = 300 },
        };
        Check(EditorTime.SnapSeconds(.026, fastBpms, 32, true) ==
              ExactBarTime.FromFraction(1, 32),
            "editor time uses the active BPM when snapping display seconds");
    }

    private static void TestGoldenRoundTrip()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack");
        var meta = Path.Combine(root, "meta.json");
        var pack = V2JsonDecoder.DecodePackFile(meta);
        var charts = pack.Charts.ToDictionary(entry => entry.Id,
            entry => V2JsonDecoder.DecodeChartFile(Path.Combine(root, entry.File)), StringComparer.Ordinal);
        var document = EditorDocument.FromPackage(root, pack, charts);
        var encodedPack = V2JsonEncoder.EncodePack(document.BuildPackSnapshot());
        var first = V2JsonEncoder.EncodeChart(document.BuildChartSnapshot("hard"));
        var second = V2JsonEncoder.EncodeChart(document.BuildChartSnapshot("hard"));
        Check(first.SequenceEqual(second), "deterministic chart encoding");
        var decodedPack = V2JsonDecoder.DecodePack(encodedPack, "meta.json");
        var decodedChart = V2JsonDecoder.DecodeChart(first, "chart_hard.json");
        V2SemanticValidator.ValidatePack(decodedPack, "meta.json");
        V2SemanticValidator.ValidateChart(decodedChart, "chart_hard.json");
        var entry = decodedPack.Charts.Single(item => item.Id == "hard");
        var audio = File.ReadAllBytes(Path.Combine(root, entry.Audio ?? decodedPack.Audio!));
        var digest = V2GameplayDigest.Compute(decodedChart, entry, audio);
        var original = V2GameplayDigest.Compute(charts["hard"], pack.Charts.Single(item => item.Id == "hard"), audio);
        Check(digest.Sha256 == original.Sha256, "no-op encode keeps gameplay digest");
    }

    private static void TestStrictPackageOpen()
    {
        var root = Path.Combine(Path.GetTempPath(), "dynamite-universe-editor-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), source);
            var document = EditorPackageRepository.Open(source);
            Check(document.Charts.Count > 0, "strict editor open accepts golden package");
            Check(!document.IsUnpersisted && !document.HasUnsavedChanges,
                "strict editor open is persisted and clean");

            var missingAudio = Path.Combine(root, "missing-audio");
            CopyDirectory(source, missingAudio);
            File.Delete(Path.Combine(missingAudio, "audio.wav"));
            ExpectDiagnostic(() => EditorPackageRepository.Open(missingAudio), "meta.json", "/audio",
                "strict editor open rejects missing audio");

            var missingCover = Path.Combine(root, "missing-cover");
            CopyDirectory(source, missingCover);
            var coverPack = V2JsonDecoder.DecodePackFile(Path.Combine(missingCover, "meta.json")) with
            {
                Cover = "missing-cover.png",
            };
            File.WriteAllBytes(Path.Combine(missingCover, "meta.json"), V2JsonEncoder.EncodePack(coverPack));
            ExpectDiagnostic(() => EditorPackageRepository.Open(missingCover), "meta.json", "/cover",
                "strict editor open rejects missing cover");

            var mismatchedChart = Path.Combine(root, "mismatched-chart");
            CopyDirectory(source, mismatchedChart);
            var chartPath = Path.Combine(mismatchedChart, "chart_hard.json");
            var chart = V2JsonDecoder.DecodeChartFile(chartPath) with { ChartId = "other" };
            File.WriteAllBytes(chartPath, V2JsonEncoder.EncodeChart(chart));
            ExpectDiagnostic(() => EditorPackageRepository.Open(mismatchedChart), "chart_hard.json", "/chartId",
                "strict editor open rejects chartId mismatch");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void TestPackageWriter()
    {
        var root = Path.Combine(Path.GetTempPath(), "dynamite-universe-editor-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), source);
            var document = LoadDocument(source);
            AddDirtyNote(document);

            var missingDestination = Path.Combine(root, "save-as-missing");
            V2PackageWriter.Write(BuildWriteRequest(document, source, missingDestination));
            Check(Directory.Exists(missingDestination) && File.Exists(Path.Combine(missingDestination, "meta.json")),
                "save as creates non-existing destination");
            Check(File.Exists(Path.Combine(missingDestination, "audio.wav")),
                "save as preserves package resources");

            var emptyDestination = Path.Combine(root, "save-as-empty");
            Directory.CreateDirectory(emptyDestination);
            V2PackageWriter.Write(BuildWriteRequest(document, source, emptyDestination));
            Check(File.Exists(Path.Combine(emptyDestination, "meta.json")) &&
                File.Exists(Path.Combine(emptyDestination, "chart_hard.json")),
                "save as publishes to allowed empty destination");

            var changedPathPack = document.BuildPackSnapshot() with
            {
                Charts = document.BuildPackSnapshot().Charts.Select(entry => entry.Id == "hard"
                    ? entry with { File = "charts/hard-renamed.json" }
                    : entry).ToArray(),
            };
            V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source,
                DestinationDirectory = source,
                Pack = changedPathPack,
                Charts = document.BuildAllChartSnapshots(),
            });
            Check(File.Exists(Path.Combine(source, "charts", "hard-renamed.json")) &&
                !File.Exists(Path.Combine(source, "chart_hard.json")),
                "save supports changed chart output path");

            var sourceMeta = File.ReadAllBytes(Path.Combine(source, "meta.json"));
            var sourceChart = File.ReadAllBytes(Path.Combine(source, "charts", "hard-renamed.json"));
            var failedDestination = Path.Combine(root, "save-as-failed");
            var missingCoverPack = changedPathPack with { Cover = "missing-cover.png" };
            ExpectDiagnostic(() => V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source,
                DestinationDirectory = failedDestination,
                Pack = missingCoverPack,
                Charts = document.BuildAllChartSnapshots(),
            }), "meta.json", "/cover", "missing cover diagnostic");
            Check(!Directory.Exists(failedDestination) &&
                sourceMeta.SequenceEqual(File.ReadAllBytes(Path.Combine(source, "meta.json"))) &&
                sourceChart.SequenceEqual(File.ReadAllBytes(Path.Combine(source, "charts", "hard-renamed.json"))) &&
                document.IsDirty,
                "failed save leaves JSON unchanged and document dirty");

            var nonWavePack = changedPathPack with { Audio = "audio.mp3" };
            File.Copy(Path.Combine(source, "audio.wav"), Path.Combine(source, "audio.mp3"));
            ExpectDiagnostic(() => V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source,
                DestinationDirectory = failedDestination,
                Pack = nonWavePack,
                Charts = document.BuildAllChartSnapshots(),
            }), "audio.mp3", "/", "non-WAV audio diagnostic");
            Check(!Directory.Exists(failedDestination) && document.IsDirty,
                "non-WAV failure leaves destination absent and document dirty");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void TestSameDirectoryResourceReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "dynamite-universe-editor-replace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), source);
            var document = LoadDocument(source);
            AddDirtyNote(document);
            var originalAudio = File.ReadAllBytes(Path.Combine(source, "audio.wav"));
            var replacementAudio = Path.Combine(root, "replacement.wav");
            File.WriteAllBytes(replacementAudio, MutateWave(originalAudio));

            V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source,
                DestinationDirectory = source,
                Pack = document.BuildPackSnapshot(),
                Charts = document.BuildAllChartSnapshots(),
                ExternalResources = [new V2ExternalResourceMapping(replacementAudio, "audio.wav")],
            });
            Check(!File.ReadAllBytes(Path.Combine(source, "audio.wav")).SequenceEqual(originalAudio),
                "same-directory save replaces an existing resource in place");

            var movedSource = Path.Combine(root, "moved-source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), movedSource);
            var movedDocument = LoadDocument(movedSource);
            AddDirtyNote(movedDocument);
            var movedAudio = Path.Combine(root, "moved.wav");
            File.WriteAllBytes(movedAudio, MutateWave(originalAudio));
            var movedPack = movedDocument.BuildPackSnapshot() with { Audio = "audio/moved.wav" };
            V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = movedSource,
                DestinationDirectory = movedSource,
                Pack = movedPack,
                Charts = movedDocument.BuildAllChartSnapshots(),
                ExternalResources = [new V2ExternalResourceMapping(movedAudio, "audio/moved.wav")],
            });
            Check(File.Exists(Path.Combine(movedSource, "audio", "moved.wav")) &&
                  !File.Exists(Path.Combine(movedSource, "audio.wav")),
                "same-directory save publishes a new resource path and removes the obsolete resource");
            var reopened = EditorPackageRepository.Open(movedSource);
            Check(reopened.SelectedChart.Notes.Count == movedDocument.SelectedChart.Notes.Count &&
                  reopened.SelectedChart.Notes.Any(note => note.Type == V2NoteType.Tap),
                "same-directory save with a new resource path strictly reopens");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void TestSameDirectoryPublicationRollback()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "dynamite-universe-editor-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), source);
            var document = LoadDocument(source);
            AddDirtyNote(document);
            var originalAudio = File.ReadAllBytes(Path.Combine(source, "audio.wav"));
            var originalMeta = File.ReadAllBytes(Path.Combine(source, "meta.json"));
            var originalChart = File.ReadAllBytes(Path.Combine(source, "chart_hard.json"));
            var replacementAudio = Path.Combine(root, "replacement.wav");
            File.WriteAllBytes(replacementAudio, MutateWave(originalAudio));
            var cover = Path.Combine(root, "cover.png");
            File.WriteAllBytes(cover, [137, 80, 78, 71, 13, 10, 26, 10]);
            var pack = document.BuildPackSnapshot() with { Cover = "zz-cover.png" };

            V2PackageWriter.PublicationInterceptor = relative =>
            {
                if (relative == "zz-cover.png")
                    throw new IOException("forced publication failure");
            };
            try
            {
                ExpectException<IOException>(() => V2PackageWriter.Write(new V2PackageWriteRequest
                {
                    SourceDirectory = source,
                    DestinationDirectory = source,
                    Pack = pack,
                    Charts = document.BuildAllChartSnapshots(),
                    ExternalResources =
                    [
                        new V2ExternalResourceMapping(replacementAudio, "audio.wav"),
                        new V2ExternalResourceMapping(cover, "zz-cover.png"),
                    ],
                }), "forced same-directory publication failure");
            }
            finally
            {
                V2PackageWriter.PublicationInterceptor = null;
            }

            Check(originalAudio.SequenceEqual(File.ReadAllBytes(Path.Combine(source, "audio.wav"))) &&
                  originalMeta.SequenceEqual(File.ReadAllBytes(Path.Combine(source, "meta.json"))) &&
                  originalChart.SequenceEqual(File.ReadAllBytes(Path.Combine(source, "chart_hard.json"))),
                "failed same-directory publication restores JSON and resources");
            Check(!FindStagingDirectories(root).Any(),
                "failed same-directory publication leaves no staging directory");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void TestSameDirectoryCleanupFailure()
    {
        // Windows refuses to delete a read-only backup; Unix file deletion ignores this bit.
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "dynamite-universe-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? protectedBackup = null;
        try
        {
            var source = Path.Combine(root, "source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), source);
            var document = LoadDocument(source);
            AddDirtyNote(document);
            var replacementAudio = Path.Combine(root, "replacement.wav");
            var expectedAudio = MutateWave(File.ReadAllBytes(Path.Combine(source, "audio.wav")));
            File.WriteAllBytes(replacementAudio, expectedAudio);
            var pack = document.BuildPackSnapshot() with { Title = "Saved before cleanup failed" };
            var charts = document.BuildAllChartSnapshots();
            V2PackageWriter.PublicationInterceptor = relative =>
            {
                if (relative != "meta.json") return;
                protectedBackup = Directory.EnumerateFiles(source,
                    "chart_hard.json.dynamite-universe-backup-*").Single();
                File.SetAttributes(protectedBackup, FileAttributes.ReadOnly);
            };
            ExpectException<AggregateException>(() => V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source, DestinationDirectory = source, Pack = pack, Charts = charts,
                ExternalResources = [new V2ExternalResourceMapping(replacementAudio, "audio.wav")],
            }), "backup cleanup failure is reported after publication");
            Check(File.ReadAllBytes(Path.Combine(source, "audio.wav")).SequenceEqual(expectedAudio) &&
                  File.ReadAllBytes(Path.Combine(source, "meta.json")).SequenceEqual(V2JsonEncoder.EncodePack(pack)) &&
                  pack.Charts.All(entry => File.ReadAllBytes(Path.Combine(source, entry.File))
                      .SequenceEqual(V2JsonEncoder.EncodeChart(charts[entry.Id]))),
                "cleanup failure preserves all published JSON and resource bytes");
            Check(EditorPackageRepository.Open(source).Title == pack.Title,
                "published package strictly reopens despite a leftover backup");
            Check(!FindStagingDirectories(root).Any(),
                "cleanup continues with staging after a backup delete fails");
        }
        finally
        {
            V2PackageWriter.PublicationInterceptor = null;
            if (protectedBackup is not null && File.Exists(protectedBackup))
                File.SetAttributes(protectedBackup, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] MutateWave(byte[] original)
    {
        var mutated = (byte[])original.Clone();
        mutated[^1] ^= 0x5A;
        return mutated;
    }

    private static void TestFirstPackageCreation()
    {
        WithExternalResources((root, audio, cover) =>
        {
            var draft = EditorProjectDraftFactory.Create(BuildDraftRequest(audio, cover));
            var emptyDestination = Path.Combine(root, "empty-chart-package");
            ExpectDiagnostic(() => V2PackageWriter.Create(BuildCreateRequest(draft, emptyDestination)),
                draft.ChartDestination, "/",
                "strict first create blocks an empty chart");
            Check(!Directory.Exists(emptyDestination) && !FindStagingDirectories(root).Any(),
                "empty strict create leaves no destination or staging");

            AddTap(draft.Document, "first-tap");
            var destination = Path.Combine(root, "created-package");
            var result = V2PackageWriter.Create(BuildCreateRequest(draft, destination));
            Check(result.Digests.Count == 1 && Directory.Exists(destination),
                "first create returns verified gameplay digest");
            Check(File.ReadAllBytes(audio).SequenceEqual(File.ReadAllBytes(
                      Path.Combine(destination, draft.AudioDestination))) &&
                  File.ReadAllBytes(cover).SequenceEqual(File.ReadAllBytes(
                      Path.Combine(destination, draft.CoverDestination!))),
                "first create imports external WAV and cover");
            var opened = EditorPackageRepository.Open(destination);
            Check(opened.SelectedChart.Notes.Count == 1 &&
                  opened.SelectedChart.Notes[0].Type == V2NoteType.Tap,
                "created package strictly reopens with authored Tap");
            draft.Document.MarkSaved(destination);
            Check(!draft.Document.IsUnpersisted && !draft.Document.HasUnsavedChanges,
                "MarkSaved cleans successfully created draft");

            var deterministicDestination = Path.Combine(root, "created-package-again");
            V2PackageWriter.Create(BuildCreateRequest(draft, deterministicDestination));
            Check(File.ReadAllBytes(Path.Combine(destination, "meta.json")).SequenceEqual(
                      File.ReadAllBytes(Path.Combine(deterministicDestination, "meta.json"))) &&
                  File.ReadAllBytes(Path.Combine(destination, draft.ChartDestination)).SequenceEqual(
                      File.ReadAllBytes(Path.Combine(deterministicDestination, draft.ChartDestination))),
                "first create JSON is deterministic");

            var allowedEmpty = Path.Combine(root, "allowed-empty");
            Directory.CreateDirectory(allowedEmpty);
            V2PackageWriter.Create(BuildCreateRequest(draft, allowedEmpty) with
            {
                AllowEmptyDestination = true,
            });
            Check(File.Exists(Path.Combine(allowedEmpty, "meta.json")) &&
                  !Directory.EnumerateFileSystemEntries(root, "allowed-empty.dynamite-universe-empty-*",
                      SearchOption.TopDirectoryOnly).Any(),
                "first create publishes to explicitly allowed empty destination");

            var nonempty = Path.Combine(root, "nonempty");
            Directory.CreateDirectory(nonempty);
            var sentinel = Path.Combine(nonempty, "keep.txt");
            File.WriteAllText(sentinel, "keep");
            ExpectException<IOException>(() => V2PackageWriter.Create(
                    BuildCreateRequest(draft, nonempty) with { AllowEmptyDestination = true }),
                "first create rejects nonempty destination");
            Check(File.ReadAllText(sentinel) == "keep" && !FindStagingDirectories(root).Any(),
                "nonempty destination failure preserves target and leaves no staging");

            var missingAudio = Path.Combine(root, "missing.wav");
            var missingResourceDestination = Path.Combine(root, "missing-resource");
            ExpectDiagnostic(() => V2PackageWriter.Create(BuildCreateRequest(
                    draft, missingResourceDestination,
                    [new V2ExternalResourceMapping(missingAudio, draft.AudioDestination),
                     new V2ExternalResourceMapping(cover, draft.CoverDestination!)])),
                "create-request", "/externalResources/0/externalSourcePath",
                "first create rejects missing external resource");
            Check(!Directory.Exists(missingResourceDestination) && !FindStagingDirectories(root).Any(),
                "missing external resource leaves no destination or staging");

            var missingMappingDestination = Path.Combine(root, "missing-mapping");
            ExpectDiagnostic(() => V2PackageWriter.Create(BuildCreateRequest(
                    draft, missingMappingDestination,
                    [new V2ExternalResourceMapping(audio, draft.AudioDestination)])),
                "create-request", "/externalResources",
                "first create requires referenced cover mapping");
            Check(!Directory.Exists(missingMappingDestination) && !FindStagingDirectories(root).Any(),
                "missing resource mapping leaves no destination or staging");

            var invalidWave = Path.Combine(root, "invalid.wav");
            File.WriteAllText(invalidWave, "not a wave");
            var invalidWaveDestination = Path.Combine(root, "invalid-wave");
            ExpectDiagnostic(() => V2PackageWriter.Create(BuildCreateRequest(
                    draft, invalidWaveDestination,
                    [new V2ExternalResourceMapping(invalidWave, draft.AudioDestination),
                     new V2ExternalResourceMapping(cover, draft.CoverDestination!)])),
                draft.AudioDestination, "/", "first create rejects invalid WAV");
            Check(!Directory.Exists(invalidWaveDestination) && !FindStagingDirectories(root).Any(),
                "invalid WAV failure rolls back staging and destination");
        });
    }

    private static EditorDocument LoadDocument(string root)
    {
        var pack = V2JsonDecoder.DecodePackFile(Path.Combine(root, "meta.json"));
        var charts = pack.Charts.ToDictionary(entry => entry.Id,
            entry => V2JsonDecoder.DecodeChartFile(Path.Combine(root, entry.File)), StringComparer.Ordinal);
        return EditorDocument.FromPackage(root, pack, charts);
    }

    private static void AddDirtyNote(EditorDocument document)
    {
        document.SelectChart("hard");
        document.Execute(new AddNoteCommand(new EditableNote
        {
            Id = document.AllocateId("tap"),
            Type = V2NoteType.Tap,
            Track = EditorTrack.Center,
            Time = ExactBarTime.FromJsonComponents(0, 1, 10),
            Center = 2.5,
            Width = 1,
        }));
    }

    private static V2PackageWriteRequest BuildWriteRequest(EditorDocument document, string source, string destination) => new()
    {
        SourceDirectory = source,
        DestinationDirectory = destination,
        Pack = document.BuildPackSnapshot(),
        Charts = document.BuildAllChartSnapshots(),
        AllowEmptyDestination = true,
    };

    private static V2PackageCreateRequest BuildCreateRequest(EditorProjectDraft draft,
        string destination, IReadOnlyList<V2ExternalResourceMapping>? resources = null)
    {
        var request = draft.BuildPackageCreateRequest(destination);
        return resources is null ? request : request with { ExternalResources = resources };
    }

    private static EditorProjectDraftRequest BuildDraftRequest(string audio, string? cover = null) => new(
        "editor.new-project",
        "New Project",
        "Community Artist",
        "hard",
        V2Difficulty.Hard,
        null,
        10,
        false,
        "Community Charter",
        150,
        0,
        16,
        audio,
        cover);

    private static EditorDocument CreateCleanDocument(string packId, string chartId,
        string title, string artist)
    {
        var entry = new V2ChartEntry
        {
            Id = chartId,
            Difficulty = V2Difficulty.Hard,
            Unrated = true,
            Charters = ["Unknown"],
            File = $"charts/{chartId}.json",
        };
        var pack = new V2Pack
        {
            Id = packId,
            Revision = 1,
            Title = title,
            Artist = artist,
            Audio = "audio.wav",
            Charts = [entry],
        };
        var chart = new V2Chart
        {
            ChartId = chartId,
            AudioOffsetSec = 0,
            Bpms = [new V2BpmEvent(ExactBarTime.Zero, 150)],
            NotesLeft = [],
            NotesCenter = [],
            NotesRight = [],
        };
        return EditorDocument.FromPackage(Path.Combine(Path.GetTempPath(), packId), pack,
            new Dictionary<string, V2Chart>(StringComparer.Ordinal) { [chartId] = chart });
    }

    private static EditableNote RequireTestNote(this EditorDocument document, string id) =>
        document.SelectedChart.Notes.Single(note => note.Id == id);

    private static void AddTap(EditorDocument document, string id)
    {
        document.Execute(new AddNoteCommand(new EditableNote
        {
            Id = id,
            Type = V2NoteType.Tap,
            Track = EditorTrack.Center,
            Time = ExactBarTime.Zero,
            Center = 2.5,
            Width = 1,
        }));
    }

    private static void WithExternalResources(Action<string, string, string> test)
    {
        var root = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory)!,
            "dynamite-universe-editor-draft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var audio = Path.Combine(root, "source.wav");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack", "audio.wav"), audio);
            var cover = Path.Combine(root, "source.png");
            File.WriteAllBytes(cover, [137, 80, 78, 71, 13, 10, 26, 10]);
            test(root, audio, cover);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static IEnumerable<string> FindStagingDirectories(string root) =>
        Directory.EnumerateDirectories(root, "*.dynamite-universe-staging-*", SearchOption.TopDirectoryOnly);

    private static void ExpectDiagnostic(Action action, string source, string pointer, string name)
    {
        try
        {
            action();
        }
        catch (V2DiagnosticException exception)
        {
            Check((exception.SourceName.EndsWith(source, StringComparison.Ordinal) ||
                   Path.GetFileName(exception.SourceName).Equals(source, StringComparison.Ordinal)) &&
                exception.JsonPointer == pointer, name);
            return;
        }
        throw new InvalidOperationException(name + " did not throw a v2 diagnostic");
    }

    private static void ExpectException<TException>(Action action, string name)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            Check(true, name);
            return;
        }
        throw new InvalidOperationException(name + $" did not throw {typeof(TException).Name}");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException(name);
        Console.WriteLine("PASS: " + name);
    }
}
