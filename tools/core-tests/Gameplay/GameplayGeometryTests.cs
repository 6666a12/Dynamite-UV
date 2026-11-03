using static CoreTests.CoreTestSupport;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace CoreTests;

internal static class GameplayGeometryTests
{

    // 2b. fixture DropSpeed 线性插值；另保留回溯换向与重复事件逻辑测试。
    internal static void TestDropSpeedVisualModel(Chart chart)
    {
        Console.WriteLine(nameof(TestDropSpeedVisualModel));
        var fixtureSpeed = new DropSpeedMap(chart.DropSpeeds);
        Check(Math.Abs(fixtureSpeed.SpeedAt(-1.0) - 0.5) < 1e-12,
            "fixture DropSpeed 首事件前 = 0.5");
        Check(Math.Abs(fixtureSpeed.SpeedAt(0.5) - 1.0) < 1e-12,
            "fixture DropSpeed bar 0.5 插值 = 1.0");
        Check(Math.Abs(fixtureSpeed.SpeedAt(1.5) - 1.25) < 1e-12,
            "fixture DropSpeed bar 1.5 插值 = 1.25");
        Check(Math.Abs(fixtureSpeed.SpeedAt(3.0) - 1.0) < 1e-12,
            "fixture DropSpeed 末事件后 = 1.0");
        Check(Math.Abs(fixtureSpeed.RemainingDistance(2.5, 1.5) - 1.25) < 1e-12,
            "fixture RemainingDistance = (2.5-1.5)*1.25 = 1.25");

        var speed = new DropSpeedMap(new[]
        {
            (0.0, 0.2),
            (1.0, 1.1),
        });

        Check(Math.Abs(speed.SpeedAt(0.5) - 0.65) < 1e-12,
            nameof(DropSpeedMap.SpeedAt));
        var peakBar = 7.0 / 18.0;
        var peak = speed.RemainingDistance(1.0, peakBar);
        Check(peak > speed.RemainingDistance(1.0, 0.0),
            nameof(DropSpeedMap.RemainingDistance));
        Check(peak > speed.RemainingDistance(1.0, peakBar - 0.001) &&
            peak > speed.RemainingDistance(1.0, peakBar + 0.001),
            nameof(TestDropSpeedVisualModel));
        Check(Math.Abs(speed.RemainingDistance(1.0, 1.0)) < 1e-12,
            nameof(TestDropSpeedVisualModel));

        var duplicate = new DropSpeedMap(new[] { (2.0, 0.5), (2.0, 0.8) });
        Check(Math.Abs(duplicate.SpeedAt(2.0) - 0.8) < 1e-12,
            nameof(TestDropSpeedVisualModel));
    }

    internal static void TestConstantBpmVisualSpeed(Chart chart)
    {
        Console.WriteLine(nameof(TestConstantBpmVisualSpeed));
        const double basePixelsPerSecond = 1026.0;

        var slowCurrentSecond = 3.0;
        var fastCurrentSecond = 4.5;
        var slowCurrentBar = chart.SecondsToBarTime(slowCurrentSecond);
        var fastCurrentBar = chart.SecondsToBarTime(fastCurrentSecond);
        Check(chart.BpmAtSeconds(slowCurrentSecond) == 120.0 &&
            chart.BpmAtSeconds(fastCurrentSecond) == 240.0,
            "visual speed fixture samples 120 and 240 BPM segments");

        var slowScroll = new DropSpeedMap(new[] { (0.0, 1.0) }).SpeedAt(slowCurrentBar);
        var fastScroll = new DropSpeedMap(new[] { (0.0, 1.0) }).SpeedAt(fastCurrentBar);
        var slowDistance = VisualScrollMath.DistancePixels(
            slowCurrentSecond + 0.5, slowCurrentSecond, basePixelsPerSecond,
            slowScroll, 1.0);
        var fastDistance = VisualScrollMath.DistancePixels(
            fastCurrentSecond + 0.5, fastCurrentSecond, basePixelsPerSecond,
            fastScroll, 1.0);
        Check(Math.Abs(slowDistance - 513.0) < 1e-12 &&
            Math.Abs(fastDistance - 513.0) < 1e-12,
            "same 0.5s lead stays 513px at both 120 and 240 BPM");

        Check(Math.Abs(VisualScrollMath.DistancePixels(
            2.0, 1.0, basePixelsPerSecond, 1.25, 0.5) - 641.25) < 1e-12,
            "scroll and player speed multiply the fixed 1026px/s baseline");
        Check(Math.Abs(VisualScrollMath.DistancePixels(
            2.0, 1.0, basePixelsPerSecond, -0.5, 1.0) + 513.0) < 1e-12,
            "legacy negative DropSpeed preserves signed approach distance");
        Check(Math.Abs(VisualScrollMath.FallthroughSpeedPixelsPerSecond(
            basePixelsPerSecond, -0.5, 1.0) - 513.0) < 1e-12,
            "fallthrough keeps moving outward at the absolute scroll speed");
        Check(Math.Abs(VisualScrollMath.DistancePixels(
            2.0, 1.0, basePixelsPerSecond, 1.25, 0.5, 0.25) - 160.3125) < 1e-12 &&
            Math.Abs(VisualScrollMath.FallthroughSpeedPixelsPerSecond(
                basePixelsPerSecond, 1.25, 0.5, 2.0) - 1282.5) < 1e-12,
            "per-note scroll multiplier compounds approach and fallthrough speed");
    }

    internal static void TestGameplayStageGeometry()
    {
        Console.WriteLine(nameof(TestGameplayStageGeometry));
        var center = GameplayStageGeometry.PositionAt(Track.Center, 2.5, 100);
        var left = GameplayStageGeometry.PositionAt(Track.Left, 2.5, 100);
        var right = GameplayStageGeometry.PositionAt(Track.Right, 2.5, 100);
        Check(Math.Abs(center.X - 963f) < .001f && Math.Abs(center.Y - 761f) < .001f,
            "shared stage center mapping preserves fixed gameplay geometry");
        Check(Math.Abs(GameplayStageGeometry.SideDistanceScale - 691f / 790f) < .000001f &&
              Math.Abs(left.X - (GameplayStageGeometry.LeftLineX + 100f * 691f / 790f)) < .001f &&
              Math.Abs(right.X - (GameplayStageGeometry.RightLineX - 100f * 691f / 790f)) < .001f &&
              Math.Abs(left.Y - right.Y) < .001f,
            "shared stage side mappings are symmetric");
        Check(Math.Abs(GameplayStageGeometry.CenterWidthPx(1) - 259.54f) < .001f &&
              Math.Abs(GameplayStageGeometry.StageXToCenter(GameplayStageGeometry.CenterToStageX(2.5)) - 2.5) < .00001,
            "shared stage center width and inverse mapping remain deterministic");
        Check(Math.Abs(GameplayStageGeometry.PreviewDistanceForBars(Track.Center, 8) - 790f) < .001f &&
              Math.Abs(GameplayStageGeometry.PreviewDistanceForBars(Track.Left, 8) *
                  GameplayStageGeometry.SideDistanceScale - 691f) < .001f &&
              Math.Abs(GameplayStageGeometry.PreviewDistanceForBars(Track.Right, 8) -
                  GameplayStageGeometry.PreviewDistanceForBars(Track.Left, 8)) < .001f,
            "shared editor preview keeps gameplay center and side travel scales");
        Check(Math.Abs(GameplayStageGeometry.PreviewBarAtStagePoint(Track.Left,
                  GameplayStageGeometry.LeftLineX + GameplayStageGeometry.SidePreviewTravelPx,
                  GameplayStageGeometry.SideY0, 3) - 11d) < .00001 &&
              Math.Abs(GameplayStageGeometry.PreviewBarAtStagePoint(Track.Right,
                  GameplayStageGeometry.RightLineX - GameplayStageGeometry.SidePreviewTravelPx,
                  GameplayStageGeometry.SideY0, 3) - 11d) < .00001 &&
              Math.Abs(GameplayStageGeometry.CenterAtStagePoint(Track.Left,
                  GameplayStageGeometry.LeftLineX, GameplayStageGeometry.SideY0 - 2.5f * GameplayStageGeometry.SideUnitPx) - 2.5d) < .00001 &&
              Math.Abs(GameplayStageGeometry.WidthAtStageDrag(Track.Right, 0, 100, 0, 330) - 2d) < .00001,
            "shared editor placement maps both side rails to time center and vertical width");
    }

    // 2b. MIRROR 开关的纯几何不变量：直接打 shared 里的映射本体
    // （GameplayStageGeometry.MirroredDisplay / MirroredTrack，客户端 GameplayMirror 只是读设置）。
    // 侧轨 Left↔Right 互换且沿判定线坐标不动（关于屏幕中轴的横向反射）；
    // Center 水平坐标关于**面板中轴**反射、宽度不变；映射自反
    // （屏幕 → 谱面的输入反查复用同一函数，不自反则输入归属与 note 视觉分叉）。
    internal static void TestGameplayMirrorMapping()
    {
        Console.WriteLine(nameof(TestGameplayMirrorMapping));
        var axisX = GameplayStageGeometry.CenterX0 +
                    (float)GameplayStageGeometry.MirrorAxis * GameplayStageGeometry.CenterUnitPx;

        // 面板中轴必须是 2.5（屏幕 x=963），不是 [0,4] 左缘带的中点 2.0（x=826.4）：
        // 后者会把整个中轨镜像整体左移 136.6px。全部发行谱面的 note 中心都落在 [0,5]。
        Check(Math.Abs(GameplayStageGeometry.MirrorAxis - 2.5) < .00001 &&
              Math.Abs(axisX - 963f) < .001f &&
              Math.Abs(GameplayStageGeometry.MirroredDisplay(Track.Center, 0d, true).Center - 5d) < .00001,
            "center mirror axis is the panel midpoint 2.5 (screen x=963), mapping center 0 onto 5");

        var offCenter = GameplayStageGeometry.PositionAt(Track.Center, 2.5, 100);
        var mappedCenter = GameplayStageGeometry.MirroredDisplay(Track.Center, 2.5, true);
        var mirroredCenter = GameplayStageGeometry.PositionAt(mappedCenter.Track, mappedCenter.Center, 100);
        Check(Math.Abs(mirroredCenter.X - (2f * axisX - offCenter.X)) < .001f &&
              Math.Abs(mirroredCenter.Y - offCenter.Y) < .001f,
            "mirrored center note reflects about the panel axis and keeps its distance to the judge line");
        Check(Math.Abs(GameplayStageGeometry.MirroredDisplay(Track.Center, mappedCenter.Center, true)
                  .Center - 2.5) < .00001 &&
              Math.Abs(GameplayStageGeometry.CenterWidthPx(1) - 259.54f) < .001f,
            "center mirror is self-inverse and leaves note width untouched");

        // 对称用例：中心 ±2.0、宽 3.0 的两条互为镜像。Position 是左缘，所以对应
        // 中心 4.5 / 0.5 → 跨度 [3,6] 与 [-1,2]；镜像后右缘 6 → 左缘 -1（端点对调），
        // 两者的 screenX 相加必须正好等于 2·963。
        const double halfWidth = 1.5;
        var upper = 2.5 + 2.0;
        var lower = 2.5 - 2.0;
        Check(Math.Abs(GameplayStageGeometry.MirroredDisplay(Track.Center, upper, true).Center - lower)
                  < .00001 &&
              Math.Abs(2d * GameplayStageGeometry.MirrorAxis - (upper + halfWidth) -
                       (lower - halfWidth)) < .00001 &&
              Math.Abs(GameplayStageGeometry.CenterToStageX(upper) +
                       GameplayStageGeometry.CenterToStageX(lower) - 2f * axisX) < .001f,
            "center error +2.0/-2.0 with width 3.0 map onto each other symmetric about the panel axis");

        // 输入逆映射：触点在**显示坐标**上 → 判定侧用同一函数反查谱面坐标 → 必须落进 note 跨度。
        // 这也是"镜像开了但点在镜像后位置不判"的根因断言：判定只认谱面坐标，
        // 若在别处再镜像一次（两次施加互相抵消），反查值就会跑回显示坐标、落在跨度之外。
        const double noteP = 3.0;
        const double noteW = 3.0;                            // 谱面跨度 [3, 6]，中心 4.5
        var noteDisplayCenter = GameplayStageGeometry.MirroredDisplay(
            Track.Center, noteP + noteW / 2, true).Center;   // 显示中心 0.5
        var hitChart = GameplayStageGeometry.MirroredDisplay(
            Track.Center, noteDisplayCenter, true).Center;    // 反查回 4.5
        var originalChart = GameplayStageGeometry.MirroredDisplay(
            Track.Center, noteP + noteW / 2, true).Center;    // 点在原位置反查得到 0.5
        Check(hitChart >= noteP && hitChart <= noteP + noteW,
            "mirrored touch at the displayed center maps back inside the chart note bounds");
        Check(!(originalChart >= noteP && originalChart <= noteP + noteW),
            "touch at the un-mirrored position maps outside the chart note bounds with mirror on");

        // 侧轨：显示在右轨的触点必须反查成谱面 Left，且沿判定线坐标不变。
        var sideTouch = GameplayStageGeometry.MirroredDisplay(Track.Right, 2.5, true);
        Check(sideTouch.Track == Track.Left && Math.Abs(sideTouch.Center - 2.5) < .00001 &&
              GameplayStageGeometry.MirroredDisplay(Track.Left, 2.5, false).Track == Track.Left,
            "touch on the displayed right rail maps back to the chart Left track");

        var mappedLeft = GameplayStageGeometry.MirroredDisplay(Track.Left, 2.5, true);
        var mappedRight = GameplayStageGeometry.MirroredDisplay(Track.Right, 2.5, true);
        var mirroredLeft = GameplayStageGeometry.PositionAt(mappedLeft.Track, mappedLeft.Center, 100);
        var mirroredRight = GameplayStageGeometry.PositionAt(mappedRight.Track, mappedRight.Center, 100);
        Check(mappedLeft.Track == Track.Right && mappedRight.Track == Track.Left &&
              Math.Abs(mappedLeft.Center - 2.5) < .00001 && Math.Abs(mappedRight.Center - 2.5) < .00001,
            "side mirror swaps rails and leaves the along-rail coordinate untouched");
        Check(Math.Abs(mirroredLeft.X - (GameplayStageGeometry.RightLineX - 100f * 691f / 790f)) < .001f &&
              Math.Abs(mirroredRight.X - (GameplayStageGeometry.LeftLineX + 100f * 691f / 790f)) < .001f &&
              Math.Abs(mirroredLeft.Y - 552.5f) < .001f && Math.Abs(mirroredRight.Y - 552.5f) < .001f &&
              GameplayStageGeometry.MirroredTrack(GameplayStageGeometry.MirroredTrack(Track.Left, true),
                  true) == Track.Left,
            "mirrored side notes land on the opposite rail at the same height and the swap is self-inverse");
        Check(GameplayStageGeometry.MirroredDisplay(Track.Left, 2.5, false).Track == Track.Left &&
              Math.Abs(GameplayStageGeometry.MirroredDisplay(Track.Left, 2.5, false).Center - 2.5) < .00001 &&
              Math.Abs(GameplayStageGeometry.LeftLineX + GameplayStageGeometry.RightLineX -
                  GameplayStageGeometry.DesignWidth) < .001f,
            "mirror off is the identity and the two rails are symmetric about the screen axis");
    }
}
