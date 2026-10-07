using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Authoring-point conversion for the fixed gameplay stage.
/// Keeping it in the editor core makes left/right placement independently testable.
/// </summary>
public static class EditorStagePlacement
{
    public readonly record struct ResizedSpan(double Center, double Width);

    public static ExactBarTime TimeAt(EditorTrack track, double stageX, double stageY,
        double previewBar, double visibleBars, int gridDivisor, bool snap)
    {
        var bar = GameplayStageGeometry.PreviewBarAtStagePoint(ToRuntimeTrack(track),
            (float)stageX, (float)stageY, previewBar, visibleBars);
        var time = ExactBarTime.FromFraction((long)Math.Round(bar * 1000d), 1000);
        return snap ? EditorGeometry.Snap(time, gridDivisor) : time;
    }

    public static double CenterAt(EditorTrack track, double stageX, double stageY) =>
        GameplayStageGeometry.CenterAtStagePoint(ToRuntimeTrack(track),
            (float)stageX, (float)stageY);

    public static double WidthFromDrag(EditorTrack track, double startStageX, double startStageY,
        double endStageX, double endStageY) =>
        GameplayStageGeometry.WidthAtStageDrag(ToRuntimeTrack(track), (float)startStageX,
            (float)startStageY, (float)endStageX, (float)endStageY);

    /// <summary>Moves one geometry edge while keeping the opposite edge fixed.
    /// Center coordinates increase from the left/bottom edge toward the right/top edge on every track.</summary>
    public static ResizedSpan ResizeFromEdge(double center, double width, double requestedEdge,
        bool moveStartEdge, double minimumWidth = .25d, double maximumCenter = 5d)
    {
        var opposite = moveStartEdge ? center + width / 2d : center - width / 2d;
        var edge = Math.Clamp(requestedEdge, 0d, maximumCenter);
        if (moveStartEdge)
            edge = Math.Min(edge, opposite - minimumWidth);
        else
            edge = Math.Max(edge, opposite + minimumWidth);

        edge = Math.Clamp(edge, 0d, maximumCenter);
        var nextWidth = Math.Clamp(Math.Abs(opposite - edge), minimumWidth, maximumCenter);
        var nextCenter = Math.Clamp((opposite + edge) / 2d, nextWidth / 2d, maximumCenter - nextWidth / 2d);
        return new ResizedSpan(nextCenter, nextWidth);
    }

    private static Track ToRuntimeTrack(EditorTrack track) => track switch
    {
        EditorTrack.Left => Track.Left,
        EditorTrack.Right => Track.Right,
        _ => Track.Center,
    };
}
