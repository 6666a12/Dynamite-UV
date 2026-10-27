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
