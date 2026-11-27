using DynamiteUniverse.Shared.Storyboard;
using Godot;
using Matrix = System.Numerics.Matrix3x2;

namespace DynamiteUniverse.Game;

/// <summary>Convex shape construction and transformed clip intersection for the slow draw path.</summary>
internal sealed class StoryboardGeometry
{
    private const int EllipseSides = 48;
    private readonly List<Vertex> _geometry = new(64);
    private readonly List<Vertex> _scratch = new(64);
    private readonly record struct Vertex(Vector2 At, Vector2 Uv);
    private readonly record struct Point(double X, double Y);

    internal static bool CanBeVisible(in StoryboardDrawItem item)
    {
        var m = item.Transform;
        var a = m.M11 * item.Width;
        var b = m.M21 * item.Height;
        var c = m.M12 * item.Width;
        var d = m.M22 * item.Height;
        var x = m.M31;
        var y = m.M32;
        return float.IsFinite(x + y + a + b + c + d) && m.GetDeterminant() != 0f &&
            x + MathF.Min(a, 0f) + MathF.Min(b, 0f) < 1920 && x + MathF.Max(a, 0f) + MathF.Max(b, 0f) > 0 &&
            y + MathF.Min(c, 0f) + MathF.Min(d, 0f) < 1080 && y + MathF.Max(c, 0f) + MathF.Max(d, 0f) > 0;
    }

    internal void Draw(StoryboardDrawRun target, StoryboardCanvas owner, in StoryboardDrawItem item)
    {
        Texture2D? texture = null;
        var gradientRow = -1f;
        if (item.Image is { } binding && (texture = owner.ResolveTexture(binding)) is null) return;
        if (item.Gradient is { } gradient)
        {
            if (owner.Gradients is not { } page || !page.TryGetRow(gradient, out gradientRow)) return;
            texture = page.Texture;
        }
        _geometry.Clear();
        if (item.Kind == StoryboardDrawKind.Ellipse)
        {
            for (var i = 0; i < EllipseSides; i++)
            {
                var angle = i * MathF.Tau / EllipseSides;
                AppendVertex(item, MathF.Cos(angle) * .5f + .5f, MathF.Sin(angle) * .5f + .5f, gradientRow);
            }
        }
        else
        {
            AppendVertex(item, 0, 0, gradientRow);
            AppendVertex(item, 1, 0, gradientRow);
            AppendVertex(item, 1, 1, gradientRow);
            AppendVertex(item, 0, 1, gradientRow);
        }

        var vertices = _geometry;
        var other = _scratch;
        for (var i = 0; i < item.ClipCount && vertices.Count >= 3; i++)
        {
            var clip = owner.Clips[item.ClipStart + i];
            var w = clip.Width;
            var h = clip.Height;
            var p0 = ClipPoint(clip.Transform, clip.X, clip.Y);
            var p1 = ClipPoint(clip.Transform, (double)clip.X + w, clip.Y);
            var p2 = ClipPoint(clip.Transform, (double)clip.X + w, (double)clip.Y + h);
            var p3 = ClipPoint(clip.Transform, clip.X, (double)clip.Y + h);
            var m = clip.Transform;
            var det = (double)m.M11 * m.M22 - (double)m.M21 * m.M12;
            if (det == 0 || !double.IsFinite(det) || p0 == p1 || p1 == p2 || p2 == p3 || p3 == p0) return;
            var sign = det > 0 ? 1d : -1d;
            Clip(ref vertices, ref other, p0, p1, sign);
            Clip(ref vertices, ref other, p1, p2, sign);
            Clip(ref vertices, ref other, p2, p3, sign);
            Clip(ref vertices, ref other, p3, p0, sign);
        }
        if (vertices.Count < 3) return;
        RemoveDuplicateVertices(vertices);
        if (vertices.Count < 3 || MathF.Abs(TwicePolygonArea(vertices)) < 0.00001f) return;
        Span<Vector2> points = stackalloc Vector2[vertices.Count];
        Span<Vector2> uvs = texture is null ? [] : stackalloc Vector2[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            points[i] = vertices[i].At;
            if (!uvs.IsEmpty) uvs[i] = vertices[i].Uv;
        }
        target.DrawPolygon(points, [StoryboardCanvas.Modulation(item)], uvs, texture);
    }

    private void AppendVertex(in StoryboardDrawItem item, float x, float y, float gradientRow)
    {
        var uv = new Vector2(x, y);
        if (item.Gradient is { } gradient)
        {
            var dx = (double)gradient.To.X - gradient.From.X;
            var dy = (double)gradient.To.Y - gradient.From.Y;
            var t = ((x - gradient.From.X) * dx + (y - gradient.From.Y) * dy) / (dx * dx + dy * dy);
            // The ramp uses texel centers; clamp-to-edge extends horizontal end colors.
            const int width = StoryboardLimits.GradientRampWidth;
            uv = new((float)((0.5 + t * (width - 1)) / width), gradientRow);
        }
        _geometry.Add(new(Transform(item.Transform, x * item.Width, y * item.Height), uv));
    }

    private static Vector2 Transform(Matrix m, float x, float y) =>
        new(x * m.M11 + y * m.M21 + m.M31, x * m.M12 + y * m.M22 + m.M32);

    private static Point ClipPoint(Matrix m, double x, double y) =>
        new(x * m.M11 + y * m.M21 + m.M31, x * m.M12 + y * m.M22 + m.M32);

    private static void Clip(ref List<Vertex> input, ref List<Vertex> output, Point a, Point b, double sign)
    {
        if (input.Count == 0) return;
        output.Clear();
        var edgeX = b.X - a.X;
        var edgeY = b.Y - a.Y;
        var previous = input[^1];
        var previousSide = Side(previous.At);
        foreach (var current in input)
        {
            var side = Side(current.At);
            if ((side < 0) != (previousSide < 0))
            {
                var t = (float)(previousSide / (previousSide - side));
                output.Add(new(previous.At.Lerp(current.At, t), previous.Uv.Lerp(current.Uv, t)));
            }
            if (side >= 0) output.Add(current);
            previous = current;
            previousSide = side;
        }
        (input, output) = (output, input);

        double Side(Vector2 point) => (edgeX * (point.Y - a.Y) - edgeY * (point.X - a.X)) * sign;
    }

    private static void RemoveDuplicateVertices(List<Vertex> vertices)
    {
        for (var i = vertices.Count - 1; i >= 0 && vertices.Count > 1; i--)
        {
            var prev = i == 0 ? vertices[^1] : vertices[i - 1];
            if (vertices[i].At.DistanceSquaredTo(prev.At) < 0.000001f)
                vertices.RemoveAt(i);
        }
    }

    private static float TwicePolygonArea(List<Vertex> vertices)
    {
        var area = 0f;
        var a = vertices[0].At;
        for (var i = 2; i < vertices.Count; i++) area += (vertices[i - 1].At - a).Cross(vertices[i].At - a);
        return area;
    }
}
