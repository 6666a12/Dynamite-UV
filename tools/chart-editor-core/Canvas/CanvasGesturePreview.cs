using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public static class CanvasGesturePreview
{
    public static (ExactBarTime Time, double Seconds, ExactBarTime Delta) MoveNote(
        CanvasDragAnchor anchor, double pointerSeconds, IReadOnlyList<EditableBpm> bpms,
        int gridDivisor, bool snap, double toleranceSeconds, ExactBarTime currentTime,
        ExactBarTime endTime, double durationSeconds)
    {
        var time = anchor.TimeAt(pointerSeconds, bpms, gridDivisor, snap, toleranceSeconds);
        if (time != anchor.Time)
        {
            var latest = EditorTime.SecondsToBar(durationSeconds, bpms) - (endTime - currentTime);
            if (time > latest) time = latest;
            if (time < ExactBarTime.Zero) time = ExactBarTime.Zero;
        }
        return (time, EditorTime.BarToSeconds(time, bpms), time - currentTime);
    }

    public static (ExactBarTime Time, double Seconds)? MoveNodeTime(CanvasEditGesture gesture,
        double pointerSeconds, IReadOnlyList<EditableBpm> bpms, int gridDivisor, bool snap,
        double toleranceSeconds, ExactBarTime previous, ExactBarTime? next, double durationSeconds)
    {
        var time = gesture.ResizeNodeWidth ? gesture.Anchor.Time :
            gesture.Anchor.TimeAt(pointerSeconds, bpms, gridDivisor, snap, toleranceSeconds);
        var seconds = EditorTime.BarToSeconds(time, bpms);
        return time == gesture.Anchor.Time ||
            (time > previous && (next is null || time < next.Value) && seconds <= durationSeconds)
            ? (time, seconds) : null;
    }

    public static (ExactBarTime Time, double Seconds) ShiftPathTime(ExactBarTime time,
        ExactBarTime delta, IReadOnlyList<EditableBpm> bpms)
    {
        var shifted = time + delta;
        return (shifted, EditorTime.BarToSeconds(shifted, bpms));
    }

    public static (double Left, double Width) ResizeNoteWidth(bool leading, double pointerCenter,
        double currentLeft, double originalLeft, double originalWidth)
    {
        if (leading)
        {
            var right = originalLeft + originalWidth;
            var left = Math.Clamp(pointerCenter, 0d, right - .1d);
            var width = Math.Clamp(EditorStagePlacement.SnapWidth(right - left), .1d,
                EditorStagePlacement.MaximumCenter - left);
            left = Math.Clamp(right - width, 0d, EditorStagePlacement.MaximumCenter - width);
            width = Math.Clamp(EditorStagePlacement.SnapWidth(width), .1d,
                EditorStagePlacement.MaximumCenter - left);
            return (left, width);
        }

        var trailing = Math.Clamp(pointerCenter, currentLeft + .1d, EditorStagePlacement.MaximumCenter);
        var trailingWidth = trailing - currentLeft;
        trailingWidth = Math.Clamp(EditorStagePlacement.SnapWidth(trailingWidth), .1d,
            EditorStagePlacement.MaximumCenter - currentLeft);
        return (currentLeft, trailingWidth);
    }

    public static double ResizeNodeWidth(double pointerCenter, double nodeLeft,
        bool relative, double originalWidth, double widthPointer)
    {
        var width = relative ? originalWidth + (pointerCenter - widthPointer) : pointerCenter - nodeLeft;
        return Math.Clamp(EditorStagePlacement.SnapWidth(width), .1d,
            EditorStagePlacement.MaximumCenter - nodeLeft);
    }
}
