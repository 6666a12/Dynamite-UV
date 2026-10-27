using System.Numerics;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>
/// One new-note placement: width drag, optional Hold tail, then one document command.
/// The host supplies screen distance and snapped time; Center retains the canvas left edge.
/// </summary>
public sealed class CanvasPlacementDraft
{
    private const double MaximumCenter = EditorStagePlacement.MaximumCenter;
    public required int Side { get; init; }
    public required AuthoringNoteKind Kind { get; init; }
    public required double Time { get; init; }
    public required double AnchorCenter { get; init; }
    public required double BaseWidth { get; init; }
    public double Center { get; set; }
    public double Width { get; set; }
    public double EndTime { get; set; }
    public Vector2 StartPoint { get; init; }
    public bool Moved { get; private set; }
    public bool TailStage { get; private set; }

    public bool UpdateWidth(double pointerCenter, float distanceFromStart)
    {
        if (TailStage || (!Moved && distanceFromStart < 3f)) return false;
        Moved = true;
        var delta = pointerCenter - AnchorCenter;
        Width = Clamp(EditorStagePlacement.SnapWidth(BaseWidth + Math.Abs(delta) * delta), .1, MaximumCenter);
        Center = Clamp(AnchorCenter - Width * .5, 0, MaximumCenter - Width);
        return true;
    }

    public void BeginTail()
    {
        TailStage = true;
        EndTime = Time;
    }

    public void UpdateTail(double snappedTime, double durationSeconds)
    {
        if (TailStage) EndTime = Clamp(Math.Max(Time, snappedTime), Time, durationSeconds);
    }

    public double Commit(CanvasAuthoringController controller, ExactBarTime time, ExactBarTime? tail)
    {
        var width = Clamp(Width, .1, MaximumCenter - Clamp(Center, 0, MaximumCenter - Width));
        var left = Clamp(Center, 0, MaximumCenter - width);
        var type = Kind switch
        {
            AuthoringNoteKind.Chain => V2NoteType.Drag,
            AuthoringNoteKind.Hold => V2NoteType.Hold,
            AuthoringNoteKind.Mixer => V2NoteType.Mixer,
            AuthoringNoteKind.ExTap => V2NoteType.ExTap,
            AuthoringNoteKind.Mine => V2NoteType.Mine,
            AuthoringNoteKind.BarLine => V2NoteType.BarLine,
            _ => V2NoteType.Tap,
        };
        var track = Side switch { 1 => EditorTrack.Left, 2 => EditorTrack.Right, _ => EditorTrack.Center };
        controller.AddNote(type, track, time, left + width / 2d, width, tail);
        return width;
    }

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
}
