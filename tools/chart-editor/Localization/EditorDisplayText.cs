using System.Globalization;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Localization;

public static class EditorDisplayText
{
    public static string Note(V2NoteType type) => EditorLocalization.Current.Get(type switch
    {
        V2NoteType.Tap => "Note.Tap",
        V2NoteType.Drag => "Note.Drag",
        V2NoteType.ExTap => "Note.ExTap",
        V2NoteType.Hold => "Note.Hold",
        V2NoteType.Mixer => "Note.Mixer",
        V2NoteType.Mine => "Note.Mine",
        V2NoteType.BarLine => "Note.BarLine",
        _ => "Common.Unknown",
    });

    public static string Track(Editor.EditorTrack track) => EditorLocalization.Current.Get(track switch
    {
        Editor.EditorTrack.Left => "Track.Left",
        Editor.EditorTrack.Center => "Track.Center",
        Editor.EditorTrack.Right => "Track.Right",
        _ => "Track.Events",
    });

    public static string Curve(V2PathCurve? curve) => EditorLocalization.Current.Get(curve switch
    {
        null => "Curve.None",
        V2PathCurve.Linear => "Curve.Linear",
        V2PathCurve.Hold => "Curve.Step",
        V2PathCurve.EaseInQuad => "Curve.EaseIn",
        V2PathCurve.EaseOutQuad => "Curve.EaseOut",
        V2PathCurve.EaseInOutCubic => "Curve.EaseInOut",
        V2PathCurve.Smooth => "Curve.Smooth",
        _ => "Common.Unknown",
    });

    public static string Curve(V2ScrollCurve? curve) => EditorLocalization.Current.Get(curve switch
    {
        null => "Curve.None",
        V2ScrollCurve.Linear => "Curve.Linear",
        V2ScrollCurve.Hold => "Curve.Step",
        V2ScrollCurve.EaseInQuad => "Curve.EaseIn",
        V2ScrollCurve.EaseOutQuad => "Curve.EaseOut",
        V2ScrollCurve.EaseInOutCubic => "Curve.EaseInOut",
        _ => "Common.Unknown",
    });
}
