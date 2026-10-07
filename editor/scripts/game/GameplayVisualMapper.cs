using Godot;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Game;

/// <summary>Shared note geometry and sustain-link rendering rules for gameplay and DynaMaker UV.</summary>
public static class GameplayVisualMapper
{
    public const float BaseFallSpeedPx = 1026f;
    public const float SideLeadPx = 691f / GameplayStageGeometry.SideDistanceScale;

    public static Vector2 PositionAt(Note note, float remainingPx) =>
        GameplayStageGeometry.PositionAt(note.Track, note.Position + note.Width * .5, remainingPx)
        is var p ? new Vector2(p.X, p.Y) : Vector2.Zero;

    public static Vector2 SizeFor(Note note)
    {
        if (note.Track == Track.Center)
        {
            var axis = GameplayStageGeometry.CenterWidthPx(note.Width);
            return note.Type switch
            {
                NoteType.HoldHead or NoteType.HoldNode => new Vector2(axis, 26f),
                NoteType.MixerHead or NoteType.MixerNode => new Vector2(axis, 40f),
                NoteType.BarLine => new Vector2(axis, 6f),
                _ => new Vector2(axis, 24f),
            };
        }

        if (note.Type == NoteType.BarLine)
            return new Vector2(10f, Mathf.Max(12f, (float)note.Width * 190f * GameplayStageGeometry.NoteVisualScale));

        return new Vector2(17f, Mathf.Max(12f, (float)note.Width * 102f * GameplayStageGeometry.NoteVisualScale));
    }

    public static Color ColorFor(Note note) => NoteVisualSpec.BaseColor(note.Type);
    public static Color LinkColorFor(Note note) => NoteVisualSpec.LinkColor(note.Type);

    public static float LinkHalfFor(Note note)
    {
        if (note.Type is NoteType.HoldHead or NoteType.HoldNode)
            return note.Track == Track.Center
                ? Mathf.Max(4f, (float)note.Width * GameplayStageGeometry.CenterUnitPx * .4f)
                : Mathf.Max(6f, (float)note.Width * 102f * .5f * GameplayStageGeometry.NoteVisualScale);
        if (note.Type is NoteType.MixerHead or NoteType.MixerNode)
            return 5f;
        return note.Track == Track.Center
            ? Mathf.Max(4f, (float)note.Width * GameplayStageGeometry.CenterUnitPx * .4f)
            : 4f;
    }

    public static bool ClipToJudgeLine(Track track, ref Vector2 a, ref Vector2 b,
        ref float wa, ref float wb)
    {
        bool Beyond(Vector2 point) => track switch
        {
            Track.Center => point.Y > GameplayStageGeometry.CenterLineY,
            Track.Left => point.X < GameplayStageGeometry.LeftLineX,
            _ => point.X > GameplayStageGeometry.RightLineX,
        };

        if (Beyond(b)) return false;
        if (!Beyond(a)) return true;

        var denominator = track switch
        {
            Track.Center => b.Y - a.Y,
            Track.Left or Track.Right => b.X - a.X,
            _ => 0f,
        };
        if (Mathf.IsZeroApprox(denominator)) return false;
        var numerator = track switch
        {
            Track.Center => GameplayStageGeometry.CenterLineY - a.Y,
            Track.Left => GameplayStageGeometry.LeftLineX - a.X,
            _ => GameplayStageGeometry.RightLineX - a.X,
        };
        var k = numerator / denominator;
        a += (b - a) * k;
        wa += (wb - wa) * k;
        return true;
    }

    public static Vector2[] LinkPolygon(Track track, Vector2 a, Vector2 b, float wa, float wb) =>
        track == Track.Center
            ? new[] { new Vector2(a.X - wa, a.Y), new Vector2(a.X + wa, a.Y), new Vector2(b.X + wb, b.Y), new Vector2(b.X - wb, b.Y) }
            : new[] { new Vector2(a.X, a.Y - wa), new Vector2(b.X, b.Y - wb), new Vector2(b.X, b.Y + wb), new Vector2(a.X, a.Y + wa) };

    public static Vector2[] LinkFrame(Track track, Vector2 a, Vector2 b, float wa, float wb)
    {
        var polygon = LinkPolygon(track, a, b, wa, wb);
        return polygon.Append(polygon[0]).ToArray();
    }

    public static Vector2[] LinkRibbon(Track track, IReadOnlyList<Vector2> points, IReadOnlyList<float> halfWidths)
    {
        if (points.Count != halfWidths.Count || points.Count < 2) return Array.Empty<Vector2>();
        var polygon = new Vector2[points.Count * 2];
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var half = halfWidths[i];
            if (track == Track.Center)
            {
                polygon[i] = new Vector2(point.X - half, point.Y);
                polygon[polygon.Length - 1 - i] = new Vector2(point.X + half, point.Y);
            }
            else
            {
                polygon[i] = new Vector2(point.X, point.Y - half);
                polygon[polygon.Length - 1 - i] = new Vector2(point.X, point.Y + half);
            }
        }
        return polygon;
    }

    public static Vector2[] LinkRibbonFrame(Track track, IReadOnlyList<Vector2> points, IReadOnlyList<float> halfWidths)
    {
        if (points.Count != halfWidths.Count || points.Count < 2) return Array.Empty<Vector2>();
        var edge = new Vector2[points.Count * 2];
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var half = halfWidths[i];
            edge[i] = track == Track.Center ? new Vector2(point.X - half, point.Y) : new Vector2(point.X, point.Y - half);
            edge[edge.Length - 1 - i] = track == Track.Center ? new Vector2(point.X + half, point.Y) : new Vector2(point.X, point.Y + half);
        }
        return edge;
    }
}
