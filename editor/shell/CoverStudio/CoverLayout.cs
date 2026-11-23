using Avalonia;
using Avalonia.Media;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

internal sealed record CoverBadge(string Id, string Label, string Level, Color Color, bool Icon, bool Custom,
    bool Visible, Point Center);

internal sealed record CoverLayout(string Title, string Artist, string Noter, bool TopBar, CoverBadge[] Badges,
    Rect Crop, double Zoom, Point Framing)
{
    public const double Width = 1920, Height = 1080, AxisY = 460, Radius = 105;
    public static readonly Size WideSize = new(Width, Height);
    public static readonly Point Pivot = new(Width / 2, AxisY);
    private static readonly string[] Colors = ["#4ade80", "#38bdf8", "#fbbf24", "#c084fc", "#f87171", "#ff4d8f"];
    internal static Color TypeColor(V2Difficulty type) => type switch
    {
        V2Difficulty.Casual => Color.Parse(Colors[0]), V2Difficulty.Normal => Color.Parse(Colors[1]),
        V2Difficulty.Hard => Color.Parse(Colors[2]), V2Difficulty.Mega => Color.Parse(Colors[3]),
        V2Difficulty.Giga => Color.Parse(Colors[4]), V2Difficulty.Tech => Color.Parse(Colors[5]),
        _ => Color.Parse("#9b91ff"),
    };

    public static CoverLayout FromDocument(EditorDocument document)
    {
        var badges = document.Charts.Select((chart, index) => new CoverBadge(chart.Id,
            (chart.Difficulty == V2Difficulty.Custom ? chart.DifficultyKey ?? "CUSTOM" : chart.Difficulty.ToString()).ToUpperInvariant(),
            chart.Unrated || chart.Level is null ? "–" : chart.Level.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TypeColor(chart.Difficulty), false, chart.Difficulty == V2Difficulty.Custom, index < 6, Pivot)).ToList();
        badges.Add(new("", "DynaMaker UV", "", Color.Parse("#35e0ff"), true, false, true, Pivot));
        var noter = string.Join(" / ", document.Charts.SelectMany(chart => chart.Charters).Distinct(StringComparer.Ordinal));
        return new CoverLayout(document.Title, document.Artist, noter, true, [.. badges], default, 1, default).Recenter();
    }

    public CoverLayout ChangeBadge(string id, Func<CoverBadge, CoverBadge> edit) => this with
    {
        Badges = Badges.Select(b => b.Id == id ? edit(b) : b).ToArray(),
    };

    public CoverLayout Recenter()
    {
        var badges = Badges.Where(b => !b.Icon && b.Visible).ToArray();
        Vector[] slots = badges.Length switch
        {
            0 => [],
            1 => [new(-111, 0)],
            2 => [new(-111, -111), new(111, -111)],
            3 => [new(-111, -166), new(111, -166), new(0, 167)],
            4 => [new(-111, -111), new(111, -111), new(-111, 111), new(111, 111)],
            5 => [new(-111, -111), new(111, -111), new(-222, 0), new(-111, 111), new(111, 111)],
            6 => [new(-111, -111), new(111, -111), new(-222, 0), new(222, 0), new(-111, 111), new(111, 111)],
            _ => [],
        };
        if (badges.Length > 6) return this;
        var destinations = badges.Select((b, i) => (b.Id, Center: Pivot + slots[i])).ToDictionary(b => b.Id, b => b.Center);
        var iconCenter = badges.Length switch { 1 => Pivot + new Vector(111, 0), 2 => Pivot + new Vector(0, 55),
            3 => Pivot - new Vector(0, 55), _ => Pivot };
        return this with { Badges = Badges.Select(b => b.Icon ? b with { Center = iconCenter } :
            destinations.TryGetValue(b.Id, out var center) ? b with { Center = center } : b).ToArray() };
    }

    public static Point ClampBadge(Point p) => new(Math.Clamp(p.X, Radius, Width - Radius),
        Math.Clamp(p.Y, Radius, Height - Radius));

    public static Rect ClampCrop(Size source, Point position, double side)
    {
        var extent = Math.Min(source.Width, source.Height);
        side = Math.Clamp(Math.Round(side), Math.Max(1, Math.Round(extent * .05)), extent);
        return new(Math.Clamp(Math.Round(position.X), 0, source.Width - side),
            Math.Clamp(Math.Round(position.Y), 0, source.Height - side), side, side);
    }

    public static Rect DefaultCrop(Size source)
    {
        var side = Math.Max(1, Math.Round(Math.Min(source.Width, source.Height) * .78));
        return ClampCrop(source, new((source.Width - side) / 2, (source.Height - side) / 2), side);
    }

    public static Rect WideImage(Size source, double zoom, Point framing)
    {
        var scale = Math.Max(Width / source.Width, Height / source.Height) * Math.Clamp(zoom, 1, 4);
        var w = source.Width * scale;
        var h = source.Height * scale;
        return new(Math.Clamp((Width-w)/2 + framing.X, Math.Min(Width-w, 0), 0),
            Math.Clamp((Height-h)/2 + framing.Y, Math.Min(Height-h, 0), 0), w, h);
    }
}

/// <summary>A guide's normal and signed distance from the canvas origin (all in cover pixels).</summary>
internal readonly record struct CoverGuide(Vector Normal, double Distance);
internal readonly record struct CoverSnapResult(Point Position, CoverGuide[] Guides);

internal static class CoverSnap
{
    private const double Diagonal = 0.7071067811865475;
    private static readonly Vector X = new(1, 0), Y = new(0, 1), U = new(Diagonal, Diagonal), V = new(Diagonal, -Diagonal);
    private static double Dot(Vector u, Vector v) => u.X * v.X + u.Y * v.Y;
    private static double Project(Point p, Vector n) => p.X * n.X + p.Y * n.Y;

    public static CoverSnapResult Align(Point raw, IEnumerable<CoverBadge> others, double tolerance)
    {
        var position = CoverLayout.ClampBadge(raw);
        var candidates = new List<(CoverGuide Guide, double Delta)>();
        void Add(Vector normal, double target, double offset = 0)
        {
            var delta = target - offset - Project(position, normal);
            if (Math.Abs(delta) <= tolerance)
                candidates.Add((new(normal, target), delta));
        }
        Add(X, CoverLayout.Pivot.X);
        // Align with the template's badge band, not the geometric midline under it.
        Add(Y, CoverLayout.Pivot.Y);
        var edge = CoverLayout.Radius * Diagonal;
        foreach (var b in others.Where(b => b.Visible))
        {
            Add(X, b.Center.X);
            Add(Y, b.Center.Y);
            foreach (var n in new[] { U, V })
                foreach (var fixedEdge in new[] { -edge, edge })
                    foreach (var movingEdge in new[] { -edge, edge })
                        Add(n, Project(b.Center, n) + fixedEdge, movingEdge);
        }
        var sorted = candidates.OrderBy(c => Math.Abs(c.Delta)).ToArray();
        foreach (var first in sorted)
        {
            var correction = first.Guide.Normal * first.Delta;
            var snapped = position + correction;
            foreach (var second in sorted)
            {
                var similarity = Dot(first.Guide.Normal, second.Guide.Normal);
                if (Math.Abs(similarity) > .99) continue;
                var tangent = second.Guide.Normal - first.Guide.Normal * similarity;
                var combined = correction + tangent * ((second.Delta - Dot(correction, second.Guide.Normal)) / Dot(tangent, second.Guide.Normal));
                var both = position + combined;
                if (combined.Length <= tolerance && CoverLayout.ClampBadge(both) == both)
                    return new(both, [first.Guide, second.Guide]);
            }
            if (CoverLayout.ClampBadge(snapped) == snapped) return new(snapped, [first.Guide]);
        }
        return new(position, []);
    }
}

internal sealed class CoverHistory(CoverLayout initial)
{
    private readonly List<CoverLayout> _undo = [], _redo = [];
    public CoverLayout Current { get; private set; } = initial;
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public void Reset(CoverLayout value) { Current = value; _undo.Clear(); _redo.Clear(); }
    public void Preview(CoverLayout next) => Current = next;
    public void Commit(CoverLayout before)
    {
        if (before == Current || (before with { Badges = Current.Badges }) == Current && before.Badges.SequenceEqual(Current.Badges)) return;
        if (_undo.Count == 100) _undo.RemoveAt(0);
        _undo.Add(before);
        _redo.Clear();
    }
    public void Change(CoverLayout next)
    {
        var before = Current;
        Preview(next);
        Commit(before);
    }
    public void Undo() => Move(_undo, _redo);
    public void Redo() => Move(_redo, _undo);
    private void Move(List<CoverLayout> from, List<CoverLayout> to)
    {
        if (from.Count == 0) return;
        to.Add(Current);
        Current = from[^1];
        from.RemoveAt(from.Count - 1);
    }
}
