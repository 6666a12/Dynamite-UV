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
            TestAuthoringAxisAndSnap();
            TestPlacementGuide();
            TestExactBarTimeText();
            TestPropertyCatalog();
            TestDraftCreationAndValidation();
            TestDraftPersistenceState();
            TestCommandsAndSnapshots();
            TestAllSevenNoteTypes();
            TestPathEditingCommands();
            TestPathProjection();
            TestPathNodeInsertion();
            TestScrollEditingCommands();
            TestUndoFollowsOwningChart();
            TestChartLifecycleCommands();
            TestBpmCollisionIsRejected();
            TestDirectEditingCommands();
            TestCanvasAuthoringController();
            TestEditorPropertySteps();
            TestMenuStepCommands();
            TestCanvasDragAnchor();
            TestEditorTimeConversion();
            TestGoldenRoundTrip();
            TestStrictPackageOpen();
            TestPackageWriter();
            TestSameDirectoryResourceReplacement();
            TestSameDirectoryPublicationRollback();
            TestSameDirectoryCleanupFailure();
            TestFirstPackageCreation();
            TestEditorSaveSequence();
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

    // The authoring stage scrolls at the gameplay fall speed, so a note placed at time t and
    // the stage point the shared geometry projects it onto must read back as t. The canvas
    // divides its screen distance by SideDistanceScale before calling the shared mapping, and
    // this test pins that round trip together with the moved snap steps.
    private static void TestAuthoringAxisAndSnap()
    {
        const double fallSpeed = 1026d;
        const double cursor = 0d;
        const double placed = .75d;
        var point = GameplayStageGeometry.PositionAt(Track.Left, 2.5d,
            (float)EditorStagePlacement.RemainingPxAtTime(placed, cursor, fallSpeed));
        var readBack = EditorStagePlacement.TimeAtRemainingPx(
            GameplayStageGeometry.StageDistancePx(Track.Left, point.X, point.Y) /
            GameplayStageGeometry.SideDistanceScale, cursor, fallSpeed);
        Check(Math.Abs(readBack - placed) < 1e-6 &&
              Math.Abs(EditorStagePlacement.RemainingPxAtTime(placed, cursor, fallSpeed) -
                  placed * fallSpeed) < 1e-9,
            "authoring fall-speed axis round-trips through the shared side-rail projection");

        var centerPoint = GameplayStageGeometry.PositionAt(Track.Center, 2.5d,
            (float)EditorStagePlacement.RemainingPxAtTime(placed, cursor, fallSpeed));
        Check(Math.Abs(EditorStagePlacement.TimeAtRemainingPx(
                  GameplayStageGeometry.StageDistancePx(Track.Center, centerPoint.X, centerPoint.Y),
                  cursor, fallSpeed) - placed) < 1e-6,
            "authoring fall-speed axis round-trips on the centre rail");

        Check(Math.Abs(EditorStagePlacement.SnapCenter(2.44d, false) - 2.4d) < 1e-12 &&
              Math.Abs(EditorStagePlacement.SnapCenter(2.45d, false) - 2.5d) < 1e-12 &&
              Math.Abs(EditorStagePlacement.SnapCenter(2.44d, true) - 2.44d) < 1e-12 &&
              Math.Abs(EditorStagePlacement.SnapCenter(9d, false) -
                  EditorStagePlacement.MaximumCenter) < 1e-12,
            "authoring centre snap keeps the 0.1 step, the fine step and the coordinate clamp");
        Check(Math.Abs(EditorStagePlacement.SnapWidth(.975d) - 1d) < 1e-12 &&
              Math.Abs(EditorStagePlacement.SnapWidth(.02d) - 0d) < 1e-12 &&
              Math.Abs(EditorGeometry.CenterSnap - .1d) < 1e-12 &&
              Math.Abs(EditorGeometry.FineCenterSnap - .01d) < 1e-12 &&
              Math.Abs(EditorGeometry.WidthSnapDivisor - 20d) < 1e-12,
            "authoring snaps stay 0.1/0.01 centre units and 1/20 width units");
        Check(CreateCleanDocument("editor.grid", "hard", "Grid", "Community").GridDivisor ==
                  EditorDocument.DefaultGridDivisor &&
              EditorDocument.DefaultGridDivisor == 16,
            "the canvas grid reads the document's own default divisor");
    }

    private static void TestPlacementGuide()
    {
        // 对齐提示：容差 = 吸附步长的一半（默认 SNAP 0.10 → 0.05）。
        const double tolerance = .05d;
        Check(EditorPlacementGuide.Classify(1.5d, .45d, 1.5d, .9d, tolerance) == EditorAlignRelation.SamePosition,
            "align marker reports same position only");
        Check(EditorPlacementGuide.Classify(1.5d, .45d, 2.2d, .45d, tolerance) == EditorAlignRelation.SameWidth,
            "align marker reports same width only");
        Check(EditorPlacementGuide.Classify(1.5d, .45d, 1.54d, .42d, tolerance) == EditorAlignRelation.Both,
            "align marker reports both within half a snap step");
        Check(EditorPlacementGuide.Classify(1.5d, .45d, 1.56d, .38d, tolerance) == EditorAlignRelation.None,
            "align marker reports nothing beyond half a snap step");
        Check(EditorPlacementGuide.Classify(0d, 5d, 0d, 0d, tolerance) == EditorAlignRelation.SamePosition,
            "align marker keeps position when only the width differs by the whole track");

        // 读数：吸附后的真值，最多两位小数（1.50 写 1.5）。
        Check(EditorPlacementGuide.ValueText(1.5d) == "1.5" &&
              EditorPlacementGuide.ValueText(.45d) == "0.45" &&
              EditorPlacementGuide.ValueText(2d) == "2",
            "guide value text keeps the snapped truth and trims trailing zeros");

        // 时间读数：吸附态写分数式 BarTime，自由态（按住 Z / SNAP OFF）写原始小数小节。
        var bpms150 = new List<EditableBpm> { new() { Time = ExactBarTime.Zero, Bpm = 150 } };
        Check(EditorPlacementGuide.TimeText(3.3d, bpms150, 8, true) == "2+1/8",
            "guide time text snaps to the grid fraction");
        Check(EditorPlacementGuide.TimeText(3.3d, bpms150, 8, false) == "2.0625",
            "guide time text shows the raw decimal while free");
        // 板面示例写的是网格分母式（91+8/32 对应 GRID 1/32）；编辑器只有一份 BarTime 文本
        // （chip 与菜单 TIME 步进器共用），按最简分数写，所以这里是 91+1/4。
        Check(EditorPlacementGuide.TimeText(146d, [], 32, true) == "91+1/4",
            "guide time text reuses the editor's single BarTime formatter");
        Check(EditorPlacementGuide.TimeText(146d, [], 32, false) == "91.25",
            "guide time text falls back to the fallback timeline's decimal");
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

    /// <summary>路径投影（编辑器画布的真实 body）：细分点必须在 shared 求值器给出的曲线上，
    /// 控制点必须原样保留，细分误差受容差约束，线性段不产生多余细分。</summary>
    private static void TestPathProjection()
    {
        var head = new EditorPathPoint(ExactBarTime.Zero, 2d, 1d, V2PathCurve.Smooth);
        var middle = new EditorPathPoint(ExactBarTime.FromFraction(1, 1), 3.2d, .7d, V2PathCurve.Smooth);
        var tail = new EditorPathPoint(ExactBarTime.FromFraction(2, 1), 3.6d, .5d, null);
        var shape = new EditorPathShape(head, [middle, tail]);
        var flattened = EditorPathProjection.Flatten(shape, 273.2d, 273.2d);

        Check(flattened.Count > 3, $"smooth path flattens into subdivided segments ({flattened.Count} points)");
        Check(flattened[0] == new EditorPathSample(head.Time, head.Center, head.Width, true) &&
              flattened[^1] == new EditorPathSample(tail.Time, tail.Center, tail.Width, true) &&
              flattened.Any(sample => sample.IsControlPoint &&
                  sample.Time == middle.Time && sample.Center == middle.Center && sample.Width == middle.Width),
            "flattened path keeps head, every control point and tail at their exact values");
        Check(flattened.Zip(flattened.Skip(1)).All(pair => pair.First.Time < pair.Second.Time),
            "flattened path times strictly increase");
        Check(!flattened.Any(sample => sample.Width <= 0d) &&
              !flattened.Any(sample => !double.IsFinite(sample.Center) || !double.IsFinite(sample.Width)),
            "flattened path stays finite and positive");

        var evaluator = new V2PathEvaluator(ReferencePathNote(head, middle, tail));
        var offCurve = flattened.Max(sample =>
        {
            var expected = evaluator.Evaluate(sample.Time);
            return Math.Max(Math.Abs(sample.Center - expected.Center), Math.Abs(sample.Width - expected.Width));
        });
        Check(offCurve <= 1e-12, $"every flattened point sits on the shared evaluator (max {offCurve:e2})");
        var sagitta = flattened.Zip(flattened.Skip(1)).Max(pair =>
        {
            var midpoint = pair.First.Time + (pair.Second.Time - pair.First.Time) * ExactBarTime.FromFraction(1, 2);
            var expected = evaluator.Evaluate(midpoint);
            return Math.Abs(expected.Center - (pair.First.Center + pair.Second.Center) / 2d) * 273.2d +
                Math.Abs(expected.Width - (pair.First.Width + pair.Second.Width) / 2d) * 273.2d / 2d;
        });
        Check(sagitta <= EditorPathProjection.SubdivisionTolerancePx,
            $"subdivision keeps the chord error inside the shared tolerance (max {sagitta:0.###}px <= {EditorPathProjection.SubdivisionTolerancePx}px)");

        var linear = EditorPathProjection.Flatten(new EditorPathShape(
            new EditorPathPoint(ExactBarTime.Zero, 1d, .5d, V2PathCurve.Linear),
            [new EditorPathPoint(ExactBarTime.FromFraction(4, 1), 3d, 2d, null)]), 273.2d, 273.2d);
        Check(linear.Count == 2 && linear.All(sample => sample.IsControlPoint),
            "linear paths flatten to head plus tail only");

        var probe = ExactBarTime.FromFraction(3, 2);
        var sampleAtNode = EditorPathProjection.Sample(shape, middle.Time);
        var sampleAtProbe = EditorPathProjection.Sample(shape, probe);
        var expectedProbe = evaluator.Evaluate(probe);
        Check(sampleAtNode == (middle.Center, middle.Width) &&
              sampleAtProbe == (expectedProbe.Center, expectedProbe.Width),
            "path sampling matches the shared evaluator bit for bit");
        Check(EditorPathProjection.Sample(shape, ExactBarTime.FromFraction(9, 1)) == (tail.Center, tail.Width) &&
              EditorPathProjection.Sample(shape, -ExactBarTime.One) == (head.Center, head.Width),
            "path sampling clamps outside the path span");

        var single = EditorPathProjection.Flatten(new EditorPathShape(head, []), 273.2d, 273.2d);
        Check(single.Count == 1 && single[0].Time == head.Time,
            "a path without nodes projects to its head alone");
    }

    /// <summary>双击插入节点的命令层判据：采样值写回、插入即选中、形状零跳变（插入时刻逐位
    /// 相同），线性区间被切开后完全不变，平滑区间只允许亚像素级形变。</summary>
    private static void TestPathNodeInsertion()
    {
        var document = CreateCleanDocument("editor.path-insert", "hard", "Path Insert", "Community");
        var hold = new EditableNote
        {
            Id = "hold-insert",
            Type = V2NoteType.Hold,
            Track = EditorTrack.Center,
            Time = ExactBarTime.Zero,
            Center = 2,
            Width = 1,
            CurveToNext = V2PathCurve.Smooth,
            // 起点贴边：v2 允许 overscan，插入节点必须写回未钳制的采样值。
        };
        hold.Nodes.Add(new EditablePathNode
        {
            Id = "hold-insert-mid",
            Time = ExactBarTime.FromFraction(1, 1),
            Center = 3.4,
            Width = .6,
            CurveToNext = V2PathCurve.Smooth,
            Judge = true,
        });
        hold.Nodes.Add(new EditablePathNode
        {
            Id = "hold-insert-tail",
            Time = ExactBarTime.FromFraction(2, 1),
            Center = 4.4,
            Width = .4,
            Judge = true,
        });
        document.Execute(new AddNoteCommand(hold));
        var controller = new CanvasAuthoringController(document);
        var insertion = ExactBarTime.FromFraction(3, 2);
        var before = EditorPathProjection.Sample(EditorPathProjection.ShapeOf(document.RequireTestNote("hold-insert")), insertion);

        var nodeId = controller.AddPathNode("hold-insert", insertion, before.Center, before.Width,
            V2PathCurve.Smooth);
        var stored = document.RequireTestNote("hold-insert");
        Check(document.Selection.Kind == EditorSelectionKind.PathNode &&
              document.Selection.Id == nodeId &&
              document.Selection.Key == stored.Nodes.Single(node => node.Id == nodeId).Key,
            "inserted path node becomes the selected document entity");
        Check(stored.Nodes.Select(node => node.Time)
                .SequenceEqual([ExactBarTime.FromFraction(1, 1), insertion, ExactBarTime.FromFraction(2, 1)]),
            "insertion keeps strictly increasing node order at the sampled time");
        var inserted = stored.Nodes.Single(node => node.Id == nodeId);
        Check(inserted.Center == before.Center && inserted.Width == before.Width && inserted.Judge == true,
            $"inserted node keeps the sampled geometry unclamped (x{inserted.Center:0.###} · w{inserted.Width:0.###})");

        var after = EditorPathProjection.Sample(EditorPathProjection.ShapeOf(stored), insertion);
        Check(after == before, "the path passes through the inserted node unchanged (zero jump)");
        var smoothDrift = PathDeviationPx(EditorPathProjection.ShapeOf(document.RequireTestNote("hold-insert")),
            ExpectedSmoothSource());
        Check(smoothDrift <= 4d,
            $"splitting one smooth interval keeps the shape continuous (max drift {smoothDrift:0.###}px)");
        V2SemanticValidator.ValidateChart(document.BuildChartSnapshot("hard"), "path-insert.json");

        // 线性区间：切开后两半仍是同一条直线，形状逐位不变。
        var linearDocument = CreateCleanDocument("editor.linear-insert", "hard", "Linear Insert", "Community");
        var linearHold = new EditableNote
        {
            Id = "hold-linear",
            Type = V2NoteType.Hold,
            Track = EditorTrack.Left,
            Time = ExactBarTime.Zero,
            Center = 1,
            Width = .5,
        };
        linearHold.Nodes.Add(new EditablePathNode
        {
            Id = "hold-linear-tail",
            Time = ExactBarTime.FromFraction(2, 1),
            Center = 3,
            Width = 1.5,
            Judge = true,
        });
        linearDocument.Execute(new AddNoteCommand(linearHold));
        var linearController = new CanvasAuthoringController(linearDocument);
        var linearInsertion = ExactBarTime.FromFraction(5, 4);
        var linearBefore = EditorPathProjection.Sample(
            EditorPathProjection.ShapeOf(linearDocument.RequireTestNote("hold-linear")), linearInsertion);
        linearController.AddPathNode("hold-linear", linearInsertion, linearBefore.Center, linearBefore.Width);
        var linearAfter = EditorPathProjection.ShapeOf(linearDocument.RequireTestNote("hold-linear"));
        var linearDrift = 0d;
        for (var step = 0; step <= 16; step++)
        {
            var time = ExactBarTime.FromFraction(step, 8);
            var reference = EditorPathProjection.Sample(EditorPathProjection.ShapeOf(linearHold), time);
            var candidate = EditorPathProjection.Sample(linearAfter, time);
            linearDrift = Math.Max(linearDrift, Math.Abs(reference.Center - candidate.Center) * 115d +
                Math.Abs(reference.Width - candidate.Width) * 102d / 2d);
        }
        Check(linearDrift <= 1e-9,
            $"splitting a linear interval leaves the path bit-identical (max drift {linearDrift:e2}px)");

        ExpectException<InvalidOperationException>(() => linearController.AddPathNode("hold-linear",
                ExactBarTime.FromFraction(2, 1), 3, 1.5),
            "a second node cannot be inserted at an existing node time");
    }

    /// <summary>测试用的 shared 路径模型：与 <see cref="EditorPathProjection"/> 合成的中间表示
    /// 同形，用来独立复算求值结果。</summary>
    private static V2PathNote ReferencePathNote(params EditorPathPoint[] points)
    {
        var nodes = new V2PathNode[points.Length - 1];
        for (var index = 1; index < points.Length; index++)
            nodes[index - 1] = new V2PathNode
            {
                Id = $"node-{index}",
                Time = points[index].Time,
                Center = points[index].Center,
                Width = points[index].Width,
                CurveToNext = points[index].CurveToNext,
            };
        return new V2PathNote
        {
            Id = "path",
            Type = V2NoteType.Hold,
            Time = points[0].Time,
            Center = points[0].Center,
            Width = points[0].Width,
            CurveToNext = points[0].CurveToNext,
            Nodes = nodes,
        };
    }

    /// <summary>插入节点前后在同一组探针时刻上的最大偏差（像素）。</summary>
    private static double PathDeviationPx(EditorPathShape actual, EditorPathShape reference)
    {
        var maximum = 0d;
        for (var step = 0; step <= 32; step++)
        {
            var time = ExactBarTime.FromFraction(step, 16);
            var expected = EditorPathProjection.Sample(reference, time);
            var candidate = EditorPathProjection.Sample(actual, time);
            maximum = Math.Max(maximum, Math.Abs(expected.Center - candidate.Center) * 273.2d +
                Math.Abs(expected.Width - candidate.Width) * 273.2d / 2d);
        }
        return maximum;
    }

    /// <summary>插入前的路径快照（头 + 两个 node），偏差对比的基准。</summary>
    private static EditorPathShape ExpectedSmoothSource() => new(
        new EditorPathPoint(ExactBarTime.Zero, 2d, 1d, V2PathCurve.Smooth),
        [
            new EditorPathPoint(ExactBarTime.FromFraction(1, 1), 3.4d, .6d, V2PathCurve.Smooth),
            new EditorPathPoint(ExactBarTime.FromFraction(2, 1), 4.4d, .4d, null),
        ]);

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

    /// <summary>右键菜单步进器的步长与钳制规则（设计稿 Editor — Context Menu 的
    /// TIME/CENTER/WIDTH 行）。</summary>
    private static void TestEditorPropertySteps()
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
    private static void TestMenuStepCommands()
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

    private static void TestEditorSaveSequence()
    {
        WithExternalResources((root, audio, _) =>
        {
            var draft = EditorProjectDraftFactory.Create(BuildDraftRequest(audio));
            var destination = Path.Combine(root, "editor-first-save");
            Directory.CreateDirectory(destination);
            ExpectDiagnostic(() => V2PackageWriter.Create(
                    draft.BuildPackageCreateRequest(destination, allowEmptyDestination: true)),
                draft.ChartDestination, "/", "editor first save reports an empty chart");
            Check(draft.Document.IsUnpersisted && draft.Document.HasUnsavedChanges &&
                  !Directory.EnumerateFileSystemEntries(destination).Any() &&
                  !FindStagingDirectories(root).Any(),
                "rejected first save keeps the draft dirty and the destination empty");

            AddTap(draft.Document, "editor-first-tap");
            var created = V2PackageWriter.Create(
                draft.BuildPackageCreateRequest(destination, allowEmptyDestination: true));
            draft.Document.MarkSaved(created.DestinationDirectory);
            Check(!draft.Document.IsUnpersisted && !draft.Document.HasUnsavedChanges &&
                  draft.Document.PackageDirectory == Path.GetFullPath(destination),
                "first save marks the draft persisted");

            AddDirtyNote(draft.Document);
            var written = V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = draft.Document.PackageDirectory!,
                DestinationDirectory = draft.Document.PackageDirectory!,
                Pack = draft.Document.BuildPackSnapshot(),
                Charts = draft.Document.BuildAllChartSnapshots(),
                ExternalResources = draft.Document.PendingResourceReplacements,
                AllowEmptyDestination = true,
            });
            draft.Document.MarkSaved(written.DestinationDirectory);
            Check(!draft.Document.HasUnsavedChanges && written.Digests.Count == 1,
                "same-directory save verifies the staged package and clears dirty state");

            var saveAs = Path.Combine(root, "editor-save-as");
            var copied = V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = draft.Document.PackageDirectory!,
                DestinationDirectory = saveAs,
                Pack = draft.Document.BuildPackSnapshot(),
                Charts = draft.Document.BuildAllChartSnapshots(),
                ExternalResources = draft.Document.PendingResourceReplacements,
                AllowEmptyDestination = true,
            });
            draft.Document.MarkSaved(copied.DestinationDirectory);
            var reopened = EditorPackageRepository.Open(saveAs);
            Check(reopened.PackageDirectory == Path.GetFullPath(saveAs) && !reopened.HasUnsavedChanges &&
                  reopened.SelectedChart.Notes.Count == 2 &&
                  reopened.SelectedChart.Notes.Any(note => note.Type == V2NoteType.Tap),
                "save as publishes a reopenable copy of the authored charts");
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
