using Godot;

namespace DuxCommunity.Ui;

/// <summary>Shared cut-corner polygon construction for custom-drawn controls.</summary>
public static class UiGeometry
{
    public static Vector2[] CutCorners(Vector2 size, float cut)
    {
        var c = Mathf.Min(cut, Mathf.Min(size.X, size.Y) * 0.5f);
        return
        [
            new(c, 0), new(size.X, 0), new(size.X, size.Y - c),
            new(size.X - c, size.Y), new(0, size.Y), new(0, c),
        ];
    }

    public static Vector2[] Close(Vector2[] points)
    {
        var closed = new Vector2[points.Length + 1];
        points.CopyTo(closed, 0);
        closed[^1] = points[0];
        return closed;
    }

    public static Vector2[] CutCorners(float x, float y, float width, float height, float cut)
    {
        var points = CutCorners(new Vector2(width, height), cut);
        for (var i = 0; i < points.Length; i++)
            points[i] += new Vector2(x, y);
        return points;
    }
}
