using static EditorTestSupport;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

internal static class EditorGeometryTests
{

    internal static void TestExactSnap()
    {
        var input = ExactBarTime.FromJsonComponents(2, 1, 7);
        var snapped = EditorGeometry.Snap(input, 16);
        Check(snapped == ExactBarTime.FromJsonComponents(2, 1, 8), "exact arbitrary-denominator snap");
    }

    internal static void TestSideTrackStagePlacement()
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
    internal static void TestAuthoringAxisAndSnap()
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

        var anchoredResize = EditorStagePlacement.ResizeSnappedFromEdge(2.5, 1, 3.12, false);
        var startResize = EditorStagePlacement.ResizeSnappedFromEdge(2.5, 1, 1.88, true);
        Check(Math.Abs(anchoredResize.Center - anchoredResize.Width / 2 - 2) < 1e-12 &&
              Math.Abs(startResize.Center + startResize.Width / 2 - 3) < 1e-12,
            "snapped resizing preserves either opposite logical edge");
        var anchor = ExactBarTime.FromFraction(53, 100);
        var rawDelta = ExactBarTime.FromFraction(13, 100);
        Check(EditorStagePlacement.ResolveMoveDelta(anchor, rawDelta, 16, true) ==
                  ExactBarTime.FromFraction(11, 16) - anchor &&
              EditorStagePlacement.ResolveMoveDelta(anchor, rawDelta, 16, false) == rawDelta &&
              EditorStagePlacement.ResolveMoveDelta(anchor, ExactBarTime.Zero, 16, true) == ExactBarTime.Zero,
            "group move snaps its anchor once and preserves free and spatial-only deltas");

        Check(Math.Abs(EditorStagePlacement.ResolvePositionDelta(2.537, .124, .1) - .163) < 1e-12 &&
              Math.Abs(EditorStagePlacement.ResolvePositionDelta(2.537, .124, .01) - .123) < 1e-12 &&
              Math.Abs(EditorStagePlacement.ResolvePositionDelta(2.537, .124, 0) - .124) < 1e-12 &&
              EditorStagePlacement.ResolvePositionDelta(2.537, 0, .1) == 0,
            "spatial modes snap the grabbed anchor, preserve free movement and time-only geometry");
        var freeResize = EditorStagePlacement.ResizeSnappedFromEdge(2.5, 1, 3.1234, false, 0);
        var fineResize = EditorStagePlacement.ResizeSnappedFromEdge(2.5, 1, 3.1234, false, .01);
        Check(Math.Abs(freeResize.Width - 1.1234) < 1e-12 && Math.Abs(fineResize.Width - 1.12) < 1e-12 &&
              Math.Abs(freeResize.Center - freeResize.Width / 2 - 2) < 1e-12,
            "free and fine resize preserve fixed logical edge without broadening bounds");
        TestSpatialRisks();
        TestStageBarTimeAt();
    }

    // 权威 native 舞台时间反查住在 editor core：舞台点 -> 逻辑剩余像素（侧轨除掉
    // SideDistanceScale，与 GameplayStageGeometry.PositionAt 互逆）-> 对 BPM/Scroll/落速的
    // 正向距离方程求根，得到 ExactBarTime。这里用非默认 BPM/Scroll + noteSpeedMultiplier 把
    // PositionAt + VisualScrollMath 的正向结果整圈读回来，并钉住 snap/free 边界。
    private static void TestSpatialRisks()
    {
        foreach (var track in Enum.GetValues<EditorTrack>())
        {
            Check(EditorSpatialAnalysis.Point(track, V2NoteType.Tap, 0, 1) == EditorSpatialRisk.RecommendedOverflow,
                $"{track}: traditional edge overscan remains visible and reachable");
            var far = EditorSpatialAnalysis.Point(track, V2NoteType.Tap, -20, 1);
            Check(far.HasFlag(EditorSpatialRisk.RecommendedOverflow) && far.HasFlag(EditorSpatialRisk.Invisible) &&
                  far.HasFlag(EditorSpatialRisk.Unreachable), $"{track}: three independent spatial risks");
        }
        var invisibleTap = EditorSpatialAnalysis.Point(EditorTrack.Center, V2NoteType.Tap, -1.1, .1);
        Check(invisibleTap.HasFlag(EditorSpatialRisk.Invisible) && !invisibleTap.HasFlag(EditorSpatialRisk.Unreachable),
            "invisible Tap can remain spatially reachable through community touch expansion");
        foreach (var type in new[] { V2NoteType.Mixer, V2NoteType.Mine })
            Check(EditorSpatialAnalysis.Point(EditorTrack.Center, type, -1.1, .1).HasFlag(EditorSpatialRisk.Unreachable),
                $"{type}: raw body does not inherit ordinary touch expansion");
        Check(!EditorSpatialAnalysis.Point(EditorTrack.Center, V2NoteType.BarLine, -20, 1)
                .HasFlag(EditorSpatialRisk.Unreachable), "decorative BarLine has no required touch");
        var resize = EditorStagePlacement.ResizeSnappedFromEdge(-1, 8, -7, true);
        Check(Math.Abs(resize.Center + resize.Width / 2 - 3) < 1e-12 && resize.Width == 10,
            "overscan resize preserves opposite edge with width above five");
        var draft = new StagePlacementDraft(ExactBarTime.One, -.5, 6, -.5);
        draft.UpdateWidth(.5);
        Check(draft.Center == -.5 && draft.Width == 7, "overscan width draft never pushes its anchor inward");
        var path = new EditableNote { Id = "risk-path", Type = V2NoteType.Hold, Track = EditorTrack.Center,
            Time = ExactBarTime.Zero, Center = 2.5, Width = 1 };
        path.Nodes.Add(new EditablePathNode { Id = "risk-mid", Time = ExactBarTime.One, Center = -20, Width = 1, Judge = true });
        path.Nodes.Add(new EditablePathNode { Id = "risk-end", Time = ExactBarTime.FromFraction(2, 1), Center = 2.5, Width = 1, Judge = true });
        var report = EditorSpatialAnalysis.Analyze(path);
        Check(report.PathSampled && report.CheckedPoints > 3 && report.Risks.HasFlag(EditorSpatialRisk.Unreachable) &&
              path.Nodes[0].Center == -20, "path risk analysis reports intermediate excursion without modifying geometry");
    }

    internal static void TestStageBarTimeAt()
    {
        var chart = new V2Chart
        {
            ChartId = "editor.bar-time-at",
            AudioOffsetSec = 0,
            Bpms =
            [
                new V2BpmEvent(ExactBarTime.Zero, 120),
                new V2BpmEvent(ExactBarTime.FromFraction(4, 1), 240),
            ],
            ScrollSpeeds =
            [
                new V2ScrollEvent { Time = ExactBarTime.Zero, Value = 1.0 },
                new V2ScrollEvent { Time = ExactBarTime.FromFraction(2, 1), Value = 0.5 },
            ],
            NotesLeft = [], NotesCenter = [], NotesRight = [],
        };
        var metadata = new V2RuntimeChartMetadata
        {
            SourceChart = chart,
            BpmTimeline = new V2BpmTimeline(chart.Bpms, chart.AudioOffsetSec),
            ScrollMap = new V2ScrollMap(chart.ScrollSpeeds),
            SourceIdsByRuntimeId = new Dictionary<int, string>(),
            RuntimeIdsBySourceId = new Dictionary<string, int>(StringComparer.Ordinal),
            PathsByRuntimeHeadId = new Dictionary<int, V2PathEvaluator>(),
            SyncAccentRuntimeIds = new HashSet<int>(),
        };
        const double currentSecond = 1d;
        const double playerSpeed = 1.25d;
        const double noteSpeedMultiplier = .8d;
        // 6.3 小节落在第二段 BPM（240）与第二段 Scroll（0.5）里。
        const double targetBar = 6.3d;
        var noteSecond = metadata.BpmTimeline.RuntimeSecondsAt(targetBar);
        var speed = VisualScrollMath.SpeedPixelsPerSecond(GameplayStageGeometry.BaseFallSpeedPx,
            metadata.ScrollMap.RuntimeSpeedAt(targetBar), playerSpeed, noteSpeedMultiplier);
        var remaining = (noteSecond - currentSecond) * speed;

        // 侧轨逆尺度：舞台 x 被 SideDistanceScale 投影过，LogicalRemainingPx 必须除回去。
        var leftStageX = GameplayStageGeometry.LeftLineX +
            300d * GameplayStageGeometry.SideDistanceScale;
        Check(Math.Abs(EditorStagePlacement.LogicalRemainingPx(EditorTrack.Left, leftStageX,
                  GameplayStageGeometry.SideY0) - 300d) < 1e-9 &&
              Math.Abs(GameplayStageGeometry.StageDistancePx(Track.Left, (float)leftStageX,
                  GameplayStageGeometry.SideY0) - 300d * GameplayStageGeometry.SideDistanceScale) < 1e-3,
            "side rails divide the projected stage distance by SideDistanceScale back to logical pixels");

        ExactBarTime? freeLeft = null;
        foreach (var (editorTrack, runtimeTrack) in new[]
        {
            (EditorTrack.Left, Track.Left),
            (EditorTrack.Center, Track.Center),
            (EditorTrack.Right, Track.Right),
        })
        {
            var stagePoint = GameplayStageGeometry.PositionAt(runtimeTrack, 2.5d, (float)remaining);
            var logical = EditorStagePlacement.LogicalRemainingPx(editorTrack,
                stagePoint.X, stagePoint.Y);
            Check(Math.Abs(logical - remaining) < 1e-3,
                $"{runtimeTrack} LogicalRemainingPx inverts the PositionAt projection");

            var free = EditorStagePlacement.BarTimeAt(editorTrack, stagePoint.X, stagePoint.Y,
                metadata, currentSecond, playerSpeed, gridDivisor: 16, snap: false,
                noteSpeedMultiplier: noteSpeedMultiplier);
            Check(Math.Abs(free.ToDouble() - targetBar) < 1e-5,
                $"{runtimeTrack} free BarTimeAt recovers the forward PositionAt + VisualScrollMath time");
            if (editorTrack == EditorTrack.Left) freeLeft = free;

            var snapped = EditorStagePlacement.BarTimeAt(editorTrack, stagePoint.X, stagePoint.Y,
                metadata, currentSecond, playerSpeed, gridDivisor: 16, snap: true,
                noteSpeedMultiplier: noteSpeedMultiplier);
            Check(snapped == EditorGeometry.Snap(free, 16) &&
                  Math.Abs(snapped.ToDouble() - 6.3125d) < 1e-12,
                $"{runtimeTrack} snapped BarTimeAt lands on the 1/16 grid");

            Check(EditorStagePlacement.ToEditorTrack(runtimeTrack) == editorTrack &&
                  EditorStagePlacement.ToRuntimeTrack(editorTrack) == runtimeTrack,
                $"{runtimeTrack} track conversion round-trips through the core entry point");
        }

        // noteSpeedMultiplier 参与落速：乘 1.0 时同一舞台点应读到更早的小节，且偏离目标。
        var leftPoint = GameplayStageGeometry.PositionAt(Track.Left, 2.5d, (float)remaining);
        var unitSpeed = EditorStagePlacement.BarTimeAt(EditorTrack.Left, leftPoint.X, leftPoint.Y,
            metadata, currentSecond, playerSpeed, gridDivisor: 16, snap: false,
            noteSpeedMultiplier: 1d);
        Check(freeLeft is { } expected && unitSpeed.ToDouble() < expected.ToDouble() &&
              Math.Abs(unitSpeed.ToDouble() - targetBar) > 1e-3,
            "noteSpeedMultiplier scales the fall speed used by the inverse lookup");

        // 陡坡 Scroll 上定点迭代可能不收敛；权威反查必须与正向距离方程整圈吻合。
        var rampChart = new V2Chart
        {
            ChartId = "editor.steep-scroll-inverse",
            AudioOffsetSec = 0,
            Bpms = [new V2BpmEvent(ExactBarTime.Zero, 120)],
            ScrollSpeeds =
            [
                new V2ScrollEvent
                {
                    Time = ExactBarTime.Zero, Value = 1, CurveToNext = V2ScrollCurve.Linear,
                },
                new V2ScrollEvent { Time = ExactBarTime.One, Value = 10 },
            ],
            NotesLeft = [], NotesCenter = [], NotesRight = [],
        };
        var rampMetadata = new V2RuntimeChartMetadata
        {
            SourceChart = rampChart,
            BpmTimeline = new V2BpmTimeline(rampChart.Bpms, rampChart.AudioOffsetSec),
            ScrollMap = new V2ScrollMap(rampChart.ScrollSpeeds),
            SourceIdsByRuntimeId = new Dictionary<int, string>(),
            RuntimeIdsBySourceId = new Dictionary<string, int>(StringComparer.Ordinal),
            PathsByRuntimeHeadId = new Dictionary<int, V2PathEvaluator>(),
            SyncAccentRuntimeIds = new HashSet<int>(),
        };
        const double rampTarget = .5d;
        var rampSecond = rampMetadata.BpmTimeline.RuntimeSecondsAt(rampTarget);
        var rampRemaining = VisualScrollMath.DistancePixels(rampSecond, 0,
            GameplayStageGeometry.BaseFallSpeedPx, rampMetadata.ScrollMap.RuntimeSpeedAt(rampTarget), 1);
        var rampPoint = GameplayStageGeometry.PositionAt(Track.Center, 2.5d, (float)rampRemaining);
        var rampReadBack = EditorStagePlacement.BarTimeAt(EditorTrack.Center, rampPoint.X, rampPoint.Y,
            rampMetadata, 0, 1, 16, false);
        Check(Math.Abs(rampReadBack.ToDouble() - rampTarget) <= .000001,
            "steep continuous Scroll ramps invert through the forward distance equation");

        foreach (var curve in Enum.GetValues<V2ScrollCurve>())
        {
            rampMetadata = rampMetadata with { ScrollMap = new V2ScrollMap([
                new V2ScrollEvent { Time = ExactBarTime.Zero, Value = 1, CurveToNext = curve },
                new V2ScrollEvent { Time = ExactBarTime.One, Value = 10 },
            ]) };
            var distance = VisualScrollMath.DistancePixels(rampSecond, 0,
                GameplayStageGeometry.BaseFallSpeedPx, rampMetadata.ScrollMap.RuntimeSpeedAt(rampTarget), 1);
            var point = GameplayStageGeometry.PositionAt(Track.Left, 2.5d, (float)distance);
            var recovered = EditorStagePlacement.BarTimeAt(EditorTrack.Left, point.X, point.Y,
                rampMetadata, 0, 1, 16, false);
            Check(Math.Abs(recovered.ToDouble() - rampTarget) <= .000001,
                $"steep {curve} Scroll inverse round-trips on a side track");
        }

        // Hold jumps can leave a distance with no inverse. The documented bracket policy
        // selects the discontinuity rather than oscillating between two unrelated times.
        rampMetadata = rampMetadata with { ScrollMap = new V2ScrollMap([
            new V2ScrollEvent { Time = ExactBarTime.Zero, Value = 1, CurveToNext = V2ScrollCurve.Hold },
            new V2ScrollEvent { Time = ExactBarTime.One, Value = 10 },
        ]) };
        var gapPoint = GameplayStageGeometry.PositionAt(Track.Center, 2.5d,
            GameplayStageGeometry.BaseFallSpeedPx * 5);
        Check(EditorStagePlacement.BarTimeAt(EditorTrack.Center, gapPoint.X, gapPoint.Y,
                  rampMetadata, 0, 1, 16, false) == ExactBarTime.One,
            "Scroll discontinuity deterministically resolves an unreachable distance to its boundary");

        // 负时间钳 0：判定线以下足够远的舞台点（反解秒早于 0 小节）读出 ExactBarTime.Zero。
        var pastLine = GameplayStageGeometry.PositionAt(Track.Center, 2.5f, -2000f);
        Check(EditorStagePlacement.BarTimeAt(EditorTrack.Center, pastLine.X, pastLine.Y,
                  metadata, currentSecond, playerSpeed, gridDivisor: 16, snap: false) ==
              ExactBarTime.Zero,
            "negative stage time clamps to ExactBarTime.Zero");
    }

    internal static void TestPlacementGuide()
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

    internal static void TestExactBarTimeText()
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

    internal static void TestEditorTimeConversion()
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
}
