using System.Numerics;

namespace DynamiteUniverse.Shared.Chart;

/// <summary>Frozen 1920x1080 gameplay-stage mappings shared by runtime and authoring previews.</summary>
public static class GameplayStageGeometry
{
    public const float DesignWidth = 1920f;
    public const float DesignHeight = 1080f;
    public const float CenterLineY = 861f;
    public const float CenterX0 = 280f;
    public const float CenterUnitPx = 273.2f;
    public const float LeftLineX = 184f;
    public const float RightLineX = 1736f;
    public const float SideY0 = 840f;
    public const float SideUnitPx = 115f;
    public const float SideDistanceScale = .75f;
    public const float NoteVisualScale = .95f;
    public const float CenterPreviewTravelPx = 790f;
    // Side coordinates are projected by SideDistanceScale, so this produces
    // the frozen 691px visible run at the left/right judgement rails.
    public const float SidePreviewTravelPx = 691f / SideDistanceScale;

    public static float PreviewTravelPx(Track track) =>
        track == Track.Center ? CenterPreviewTravelPx : SidePreviewTravelPx;

    public static float PreviewDistanceForBars(Track track, double barDelta,
        double visibleBars = 8d) =>
        (float)(barDelta / visibleBars * PreviewTravelPx(track));

    /// <summary>Inverse of the fixed preview projection used by DynaMaker UV placement.</summary>
    public static double PreviewBarAtStagePoint(Track track, float stageX, float stageY,
        double currentBar, double visibleBars = 8d)
    {
        var distance = track switch
        {
            Track.Left => stageX - LeftLineX,
            Track.Right => RightLineX - stageX,
            _ => CenterLineY - stageY,
        };
        return Math.Max(0d, currentBar + distance / PreviewTravelPx(track) * visibleBars);
    }

    public static double CenterAtStagePoint(Track track, float stageX, float stageY) =>
        track == Track.Center ? StageXToCenter(stageX) : (SideY0 - stageY) / SideUnitPx;

    public static double WidthAtStageDrag(Track track, float startX, float startY,
        float endX, float endY) => track == Track.Center
        ? Math.Abs(endX - startX) / CenterUnitPx
        : Math.Abs(endY - startY) / SideUnitPx;

    public static Vector2 PositionAt(Track track, double center, float remainingPx) => track switch
    {
        Track.Center => new(CenterX0 + (float)center * CenterUnitPx, CenterLineY - remainingPx),
        Track.Left => new(LeftLineX + remainingPx * SideDistanceScale, SideY0 - (float)center * SideUnitPx),
        _ => new(RightLineX - remainingPx * SideDistanceScale, SideY0 - (float)center * SideUnitPx),
    };

    public static float CenterToStageX(double center) => CenterX0 + (float)center * CenterUnitPx;
    public static double StageXToCenter(float x) => (x - CenterX0) / CenterUnitPx;
    public static float CenterWidthPx(double width) => MathF.Max(12f, (float)width * CenterUnitPx * NoteVisualScale);

    /// <summary>
    /// 中轨**面板**中轴（<see cref="Position"/> 单位）。中轨 note 的渲染中心是
    /// `P + W/2`（P 为左缘），全部发行谱面的中心值都落在 `[0, 5]`（例如 W=1 的贴边条
    /// `P ∈ [-0.5, 4.5]` → 中心 `[0, 5]`），所以面板中轴在中心坐标 **2.5** 处，
    /// 屏幕 `x = CenterX0 + 2.5·CenterUnitPx = 963`（≈ 文档记的 `Center 轨中心 x=960`）。
    /// 注意不是 2.0：那是一整个 "P∈[0,4]" 左缘带的中点，会把镜像整体左移 136.6px。
    /// </summary>
    public const double MirrorAxis = 2.5;

    /// <summary>镜像下的显示轨道：左右侧轨互换，中轨不变（自反，正反两用）。</summary>
    public static Track MirroredTrack(Track track, bool mirrored) =>
        !mirrored || track == Track.Center
            ? track
            : track == Track.Left
                ? Track.Right
                : Track.Left;

    /// <summary>
    /// 谱面 (轨道, 坐标) → 显示 (轨道, 坐标) 的单一映射：侧轨只换轨、沿判定线的坐标不动
    /// （等价于关于屏幕中轴的横向反射），中轨只把水平坐标关于 <see cref="MirrorAxis"/> 反射，
    /// 宽度不变。自反，因此屏幕 → 谱面的反查（输入触点归属）用同一个函数。
    /// </summary>
    public static (Track Track, double Center) MirroredDisplay(Track track, double center,
        bool mirrored)
    {
        if (!mirrored)
            return (track, center);
        return track == Track.Center
            ? (track, 2d * MirrorAxis - center)
            : (MirroredTrack(track, true), center);
    }
}
