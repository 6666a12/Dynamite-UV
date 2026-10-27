using System.Numerics;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public enum CanvasGestureKind { Move, ResizeWidth, ResizeNode }

/// <summary>
/// Owns one existing-object edit gesture, its original values and single commit/cancel lifecycle.
/// Preview projection and pointer hit testing remain with the canvas. Centers here are left edges,
/// matching the existing canvas projection; document commands receive center = left + width / 2.
/// </summary>
public sealed class CanvasEditGesture
{
    public required CanvasGestureKind Kind { get; init; }
    public required int NoteIndex { get; init; }
    public required int NodeIndex { get; init; }
    public required Vector2 StartPoint { get; init; }
    public required CanvasDragAnchor Anchor { get; init; }
    public double OriginalCenter { get; init; }
    public double OriginalWidth { get; init; }
    public double OriginalNodeCenter { get; init; }
    public double OriginalNodeWidth { get; init; }
    public bool ResizeLeading { get; init; }
    public bool ResizeNodeWidth { get; init; }
    public bool RelativeWidth { get; init; }
    public double WidthPointer { get; init; }
    public bool Moved { get; private set; }
    public bool Finished { get; private set; }

    /// <summary>Distance is computed by the host with its existing screen-coordinate math.</summary>
    public bool Advance(float distanceFromStart)
    {
        if (Finished || (!Moved && distanceFromStart < 3f)) return false;
        Moved = true;
        return true;
    }

    public void Cancel() => Finished = true;

    public bool Commit(CanvasAuthoringController controller, string noteId, string? nodeId,
        ExactBarTime time, double left, double width)
    {
        if (Finished) return false;
        Finished = true;
        if (!Moved) return false;
        var originalLeft = nodeId is null ? OriginalCenter : OriginalNodeCenter;
        var originalWidth = nodeId is null ? OriginalWidth : OriginalNodeWidth;
        var changed = time != Anchor.Time || !(Math.Abs(left - originalLeft) <= 1e-9) ||
            !(Math.Abs(width - originalWidth) <= 1e-9);
        if (!changed) return false;
        if (nodeId is null)
            controller.MoveNote(noteId, time, left + width / 2d, width);
        else
            controller.EditPathNode(noteId, nodeId, time, left + width / 2d, width);
        return true;
    }
}
