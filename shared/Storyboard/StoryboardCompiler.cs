using System.Numerics;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Shared.Storyboard;

/// <summary>Binds declared images to validated dimensions and builds seekable timing tables.</summary>
internal sealed class StoryboardCompiler(V2BpmTimeline? timeline)
{
    // Bound world coordinates well before float matrix/vector products could overflow.
    private const double MaxWorldMagnitude = 1e12;
    private readonly Dictionary<string, BoundImage> _images = new(StringComparer.Ordinal);
    private readonly List<StoryboardGradient> _gradients = [];
    private readonly StoryboardBarClock? _barClock = timeline is null ? null : new(timeline);
    private int _momentCount;
    private int _textureUseCount;
    private int _drawCount;
    private int _clipCapacity;

    internal StoryboardSampler Compile(StoryboardDocument document, IReadOnlyDictionary<string, StoryboardTextureSize> metadata)
    {
        var bound = new Dictionary<string, StoryboardTextureSize>(StringComparer.Ordinal);
        foreach (var (id, size) in metadata)
            if (document.Assets.ContainsKey(id)) bound.Add(id, size);
        var pathSizes = new Dictionary<string, StoryboardTextureSize>(StringComparer.Ordinal);
        foreach (var asset in document.AssetDeclarations)
        {
            if (!bound.TryGetValue(asset.Id, out var size))
                throw Error(asset.Point, asset.Id, $"Missing texture metadata for '{asset.Path}'.", "UVSB050");
            if (size.Width is <= 0 or > StoryboardLimits.MaxTextureDimension ||
                size.Height is <= 0 or > StoryboardLimits.MaxTextureDimension)
                throw Error(asset.Point, asset.Id, "Texture dimension limit exceeded.", "UVSB050");
            if (!pathSizes.TryAdd(asset.Path, size) && pathSizes[asset.Path] != size)
                throw Error(asset.Point, asset.Id, $"Conflicting dimensions for '{asset.Path}'.", "UVSB050");
            _images.Add(asset.Id, new(new(asset.Id, null), new(size.Width, size.Height)));
        }
        foreach (var region in document.RegionDeclarations)
        {
            var root = _images[region.AssetId];
            var crop = region.Crop;
            if ((long)crop.X + crop.Width > root.Size.X || (long)crop.Y + crop.Height > root.Size.Y)
                throw Error(region.Point, region.Id, $"Region is outside asset '{region.AssetId}'.", "UVSB051");
            _images.Add(region.Id, new(new(region.AssetId, crop), new(crop.Width, crop.Height)));
        }

        var roots = new SamplerRoot[document.Roots.Length];
        for (var i = 0; i < roots.Length; i++)
        {
            var root = document.Roots[i];
            if (root.Clock == Clock.Bar && timeline is null)
                throw Error(root.Point, root.Id, "A bar clock requires a strict v2 BPM timeline.", "UVSB052");
            var table = new MomentBuilder();
            var compiled = BuildNode(root, table, 0, new(1, 0), null);
            _textureUseCount = AddTextureWork(_textureUseCount + (long)compiled.TextureUseCost, root);
            roots[i] = new(compiled, BuildTimeTable(table, root.Clock, ExactBarTime.Zero));
        }
        return new(roots, document.Assets, _gradients.AsReadOnly(), _drawCount, _clipCapacity);
    }

    private SamplerNode BuildNode(SceneNode source, MomentBuilder moments, int clipDepth, WorldBounds parent,
        AncestorBoundaries? ancestors)
    {
        if (source.Repeat is { } repeat)
        {
            var inner = new MomentBuilder();
            // Include both clip edges in the table, even if a child never authors a key there.
            // A child using a distinct but rounded-equal time must not be sampled on an edge.
            var first = inner.Add(ExactBarTime.Zero, source.Point, source.Id);
            var last = inner.Add(repeat.Duration, source.Point, source.Id);
            var children = BuildChildren(source, inner, clipDepth, parent, null);
            var childUses = children.Sum(child => (long)child.TextureUseCost);
            var textureUseCost = childUses == 0 ? 0 : AddTextureWork(1 + (1 + childUses) * repeat.Count, source);
            if (_momentCount + (long)repeat.Count * inner.Count > StoryboardLimits.MaxCompiledMoments)
                throw Error(source.Point, source.Id, "Expanded timing table limit exceeded.", "UVSB002");
            var windows = new SamplerWindow[repeat.Count];
            var start = repeat.At;
            for (var i = 0; i < windows.Length; i++)
            {
                var table = BuildTimeTable(inner, source.Clock, start, ancestors);
                var startTime = table[first].Seconds;
                var endTime = table[last].Seconds;
                if (i > 0 && (windows[i - 1].Until > startTime ||
                    repeat.Period > repeat.Duration && windows[i - 1].Until == startTime))
                    throw Error(source.Point, source.Id, "Repeat boundaries cannot be distinguished at runtime.", "UVSB052");
                windows[i] = new(startTime, endTime, table);
                start += repeat.Period;
            }
            return new(source)
            {
                Windows = windows, Children = children, TextureUseCost = textureUseCost,
                TextureChildren = Array.FindAll(children, child => child.TextureUseCost > 0)
            };
        }

        var world = BoundTransform(source, parent);
        var tracks = source.Tracks;
        var node = new SamplerNode(source)
        {
            HasAnimatedPosition = source.Path is not null || tracks[(int)Property.X] is not null || tracks[(int)Property.Y] is not null,
            HasAnimatedBasis = tracks[(int)Property.ScaleX] is not null || tracks[(int)Property.ScaleY] is not null ||
                tracks[(int)Property.Rotation] is not null,
            BaseTransform = StoryboardSampler.CreateTransform(
                new(source.Base[(int)Property.X].X, source.Base[(int)Property.Y].X),
                source.Base[(int)Property.ScaleX].X, source.Base[(int)Property.ScaleY].X, source.Base[(int)Property.Rotation].X)
        };
        if (source.Active is { } active)
        {
            node.First = moments.Add(active.From, source.Point, source.Id);
            node.Last = moments.Add(active.Until, source.Point, source.Id);
        }
        for (var i = 0; i < source.Tracks.Length; i++)
        {
            if (source.Tracks[i] is not { } track) continue;
            node.Tracks[i] = new SamplerKey[track.Length];
            for (var j = 0; j < track.Length; j++)
            {
                var key = track[j];
                var index = moments.Add(key.At, key.Point, source.Id);
                node.Tracks[i]![j] = new(index, key.Value, key.Ease);
                if (source.Clock == Clock.Bar && j > 0 && track[j - 1].Ease != Ease.Step)
                    moments.AddSegment(node.Tracks[i]![j - 1].Index, index);
            }
        }
        if (source.Path is { } path)
        {
            node.Path = new SamplerPathKey[path.Length];
            for (var i = 0; i < path.Length; i++)
            {
                var key = path[i];
                var index = moments.Add(key.At, key.Point, source.Id);
                node.Path[i] = new(index, key.Position, key.C1, key.C2, key.Ease);
                if (source.Clock == Clock.Bar && i > 0 && path[i - 1].Ease != Ease.Step)
                    moments.AddSegment(node.Path[i - 1].Index, index);
            }
        }
        if (source.Kind == NodeKind.Group)
        {
            if (source.ClipRect is { } clip)
            {
                _clipCapacity += ++clipDepth;
                CheckWorldMagnitude(source, world, Length(Math.Abs(clip.X) + clip.Width, Math.Abs(clip.Y) + clip.Height));
            }
            if (source.Clock == Clock.Bar && (source.Active is not null || tracks[(int)Property.Visible] is not null))
                ancestors = new(source, ancestors);
            node.Children = BuildChildren(source, moments, clipDepth, world, ancestors);
            var uses = node.Children.Sum(child => (long)child.TextureUseCost);
            if (uses > 0)
            {
                node.TextureUseCost = AddTextureWork(uses + 1, source);
                node.TextureChildren = Array.FindAll(node.Children, child => child.TextureUseCost > 0);
            }
            return node;
        }
        _drawCount++;
        if (node.Gradient is { } gradient) _gradients.Add(gradient);
        if (source.ImageId is { } id)
        {
            var img = _images[id];
            node.Image = img.Image;
            node.Size = img.Size;
            node.TextureUseCost = 1;
        }
        if (source.Sequence is { } sequence)
        {
            moments.MarkSequence(node.First, anchor: true);
            moments.MarkSequence(node.Last, anchor: false);
            var frames = new SamplerFrame[sequence.Frames.Length];
            var size = _images[sequence.Frames[0].ImageId].Size;
            var endMs = 0;
            for (var i = 0; i < frames.Length; i++)
            {
                var frame = sequence.Frames[i];
                var img = _images[frame.ImageId];
                if (img.Size != size)
                    throw Error(frame.Point, source.Id, "All sequence frames must have the same visible dimensions.", "UVSB053");
                endMs += frame.DurationMilliseconds;
                frames[i] = new(img.Image, endMs);
            }
            node.Size = size;
            var distinct = frames.Select(frame => frame.Image).Distinct().ToArray();
            node.Frames = new(sequence.Loop, frames, distinct);
            node.TextureUseCost = distinct.Length;
        }
        CheckWorldMagnitude(source, world, Length(node.Size.X, node.Size.Y));
        // Images (including atlas/sequence frames) bind the final local size above.
        node.Origin = -(node.Size * source.Pivot);
        node.HasAnimatedFlip = tracks[(int)Property.FlipX] is not null || tracks[(int)Property.FlipY] is not null;
        node.BasePivotTransform = StoryboardSampler.CreatePivotTransform(node.Origin,
            source.Base[(int)Property.FlipX].X, source.Base[(int)Property.FlipY].X);
        return node;
    }

    private SamplerNode[] BuildChildren(SceneNode source, MomentBuilder moments, int clipDepth, WorldBounds parent,
        AncestorBoundaries? ancestors)
    {
        var children = new SamplerNode[source.Children.Length];
        for (var i = 0; i < children.Length; i++) children[i] = BuildNode(source.Children[i], moments, clipDepth, parent, ancestors);
        return children;
    }

    private static int AddTextureWork(long cost, SceneNode source) => cost <= StoryboardLimits.MaxCompiledTextureUses
        ? (int)cost : throw Error(source.Point, source.Id, "Expanded texture usage table limit exceeded.", "UVSB002");

    private static WorldBounds BoundTransform(SceneNode source, WorldBounds parent)
    {
        var x = MaxAxis(source, Property.X);
        var y = MaxAxis(source, Property.Y);
        if (source.Path is { } path)
            foreach (var key in path)
            {
                // All easing stays in [0,1], so the whole position path is bounded by
                // its endpoints and any Bézier controls, even after independent axis scaling.
                x = Math.Max(x, Math.Abs(key.Position.X));
                y = Math.Max(y, Math.Abs(key.Position.Y));
                if (key.C1 is not { } c1) continue;
                x = Math.Max(x, Math.Max(Math.Abs(c1.X), Math.Abs(key.C2.X)));
                y = Math.Max(y, Math.Max(Math.Abs(c1.Y), Math.Abs(key.C2.Y)));
            }
        var scale = Math.Max(MaxAxis(source, Property.ScaleX), MaxAxis(source, Property.ScaleY));
        var world = new WorldBounds(parent.Scale * scale, parent.Translation + parent.Scale * Length(x, y));
        CheckWorldMagnitude(source, world, 1);
        return world;
    }

    private static float MaxAxis(SceneNode node, Property property)
    {
        var max = Math.Abs(node.Base[(int)property].X);
        if (node.Tracks[(int)property] is { } keys)
            foreach (var key in keys) max = Math.Max(max, Math.Abs(key.Value.X));
        return max;
    }

    private static double Length(double x, double y) => Math.Sqrt(x * x + y * y);

    private static void CheckWorldMagnitude(SceneNode source, WorldBounds world, double localRadius)
    {
        if (world.Translation + world.Scale * localRadius > MaxWorldMagnitude)
            throw Error(source.Point, source.Id, "Accumulated transform or geometry exceeds the world-space limit.", "UVSB054");
    }

    private SamplerTimeTable BuildTimeTable(MomentBuilder builder, Clock clock, ExactBarTime start,
        AncestorBoundaries? ancestors = null)
    {
        if (builder.Count == 0) return SamplerTimeTable.Empty;
        var entries = builder.SortedEntries;
        CountMoments(entries.Length, entries[0]);
        var result = new SamplerMoment[entries.Length];
        var previousSeconds = double.NegativeInfinity;
        foreach (var entry in entries)
        {
            var absolute = start.IsZero ? entry.Time : start + entry.Time;
            var moment = ResolveTime(clock, absolute, entry.Point, entry.Id, entry.ForSequence, entry.SequenceAnchor);
            if (moment.Seconds <= previousSeconds)
                throw Error(entry.Point, entry.Id, "Distinct authored times cannot be distinguished at runtime.", "UVSB052");
            for (var ancestor = ancestors; ancestor is not null; ancestor = ancestor.Parent)
            {
                var boundaries = ancestor.Seconds ??= ResolveAncestorBoundaries(ancestor.Source);
                if (boundaries.TryGetValue(moment.Seconds, out var boundary) && absolute != boundary)
                    throw Error(entry.Point, entry.Id, "Clip time cannot be distinguished from an ancestor boundary.", "UVSB052");
            }
            previousSeconds = moment.Seconds;
            result[entry.Index] = moment;
        }

        Dictionary<SamplerSegment, SamplerProgressPoint[]>? interpolation = null;
        foreach (var segment in builder.Segments)
        {
            var first = builder[segment.Start];
            var last = builder[segment.End];
            var from = start.IsZero ? first.Time : start + first.Time;
            var until = start.IsZero ? last.Time : start + last.Time;
            var secondsFrom = result[segment.Start].Seconds;
            var secondsUntil = result[segment.End].Seconds;
            List<SamplerProgressPoint>? points = null;
            var span = ExactBarTime.Zero;
            foreach (var change in timeline!.ChangesInside(from, until))
            {
                CountMoments(1, first);
                var at = ResolveTime(clock, change, first.Point, first.Id).Seconds;
                if (at <= secondsFrom || at >= secondsUntil || (points is not null && at <= points[^1].Seconds))
                    throw Error(first.Point, first.Id, "A BPM transition in an interpolated segment cannot be distinguished at runtime.", "UVSB052");
                if (points is null)
                {
                    span = last.Time - first.Time;
                    points = [];
                }
                var fraction = ((change - from) / span).ToDouble();
                points.Add(new(at, fraction));
            }
            if (points is not null) (interpolation ??= []).Add(segment, points.ToArray());
        }
        return new(result, interpolation);
    }

    private Dictionary<double, ExactBarTime> ResolveAncestorBoundaries(SceneNode source)
    {
        var result = new Dictionary<double, ExactBarTime>();
        if (source.Active is { } active)
        {
            Add(active.From, source.Point);
            Add(active.Until, source.Point);
        }
        if (source.Tracks[(int)Property.Visible] is { } track)
            foreach (var key in track) Add(key.At, key.Point);
        return result;

        void Add(ExactBarTime time, SourcePoint point)
        {
            var seconds = ResolveTime(Clock.Bar, time, point, source.Id).Seconds;
            if (!result.TryAdd(seconds, time) && result[seconds] != time)
                throw Error(point, source.Id, "Distinct ancestor times cannot be distinguished at runtime.", "UVSB052");
        }
    }

    private void CountMoments(int count, MomentEntry source)
    {
        if ((_momentCount += count) > StoryboardLimits.MaxCompiledMoments)
            throw Error(source.Point, source.Id, "Expanded timing table limit exceeded.", "UVSB002");
    }

    private SamplerMoment ResolveTime(Clock clock, ExactBarTime time, SourcePoint point, string id,
        bool forSequence = false, bool sequenceAnchor = false)
    {
        if (clock == Clock.Media)
        {
            var ms = (long)time.Bar;
            return new(ms / 1000d, ms, null);
        }
        var exactSeconds = _barClock!.SecondsAt(time, point, id);
        var seconds = exactSeconds.ToDouble();
        if (!double.IsFinite(seconds) || forSequence && Math.Abs(seconds) > (1L << 40))
            throw Error(point, id, "Resolved media time is outside the supported runtime range.", "UVSB052");
        var anchor = sequenceAnchor ? new SamplerSequenceAnchor(exactSeconds,
            (exactSeconds - ExactBarTime.FromDouble(seconds)).ToDouble()) : null;
        return new(seconds, -1, anchor);
    }

    private sealed class AncestorBoundaries(SceneNode source, AncestorBoundaries? parent)
    {
        internal SceneNode Source { get; } = source;
        internal AncestorBoundaries? Parent { get; } = parent;
        internal Dictionary<double, ExactBarTime>? Seconds { get; set; }
    }

    private static StoryboardError Error(SourcePoint point, string id, string message, string code) =>
        new(code, point, id, message);

    private readonly record struct BoundImage(StoryboardImage Image, Vector2 Size);
    private readonly record struct WorldBounds(double Scale, double Translation);

    private sealed class MomentBuilder
    {
        private readonly Dictionary<ExactBarTime, int> _indices = new();
        private readonly List<MomentEntry> _entries = [];
        private HashSet<SamplerSegment>? _segments;
        private MomentEntry[]? _sorted;
        public int Count => _entries.Count;
        public MomentEntry this[int index] => _entries[index];
        public IEnumerable<SamplerSegment> Segments => _segments ?? Enumerable.Empty<SamplerSegment>();
        public MomentEntry[] SortedEntries => _sorted ??= _entries.OrderBy(entry => entry.Time).ToArray();

        public int Add(ExactBarTime time, SourcePoint point, string id)
        {
            if (_indices.TryGetValue(time, out var existing)) return existing;
            var index = _entries.Count;
            _indices.Add(time, index);
            _entries.Add(new(time, index, point, id, false, false));
            return index;
        }

        public void AddSegment(int start, int end) => (_segments ??= []).Add(new(start, end));

        public void MarkSequence(int index, bool anchor) => _entries[index] = _entries[index] with
        {
            ForSequence = true, SequenceAnchor = anchor || _entries[index].SequenceAnchor
        };
    }

    private readonly record struct MomentEntry(ExactBarTime Time, int Index, SourcePoint Point, string Id,
        bool ForSequence, bool SequenceAnchor);
}

internal readonly record struct SamplerRoot(SamplerNode Node, SamplerTimeTable Moments);
internal readonly record struct SamplerWindow(double From, double Until, SamplerTimeTable Moments);
internal readonly record struct SamplerMoment(double Seconds, long Milliseconds, SamplerSequenceAnchor? SequenceAnchor);
internal sealed record SamplerSequenceAnchor(ExactBarTime ExactSeconds, double Residual);
internal readonly record struct SamplerSegment(int Start, int End);
internal readonly record struct SamplerProgressPoint(double Seconds, double Progress);
internal sealed class SamplerTimeTable(SamplerMoment[] moments, Dictionary<SamplerSegment, SamplerProgressPoint[]>? interpolation)
{
    internal static SamplerTimeTable Empty { get; } = new([], null);
    internal ref readonly SamplerMoment this[int index] => ref moments[index];
    internal SamplerProgressPoint[]? ProgressPoints(int from, int until) =>
        interpolation?.GetValueOrDefault(new(from, until));
}
internal readonly record struct SamplerKey(int Index, Vector3 Value, Ease Ease);
internal readonly record struct SamplerPathKey(int Index, Vector2 Position, Vector2? C1, Vector2 C2, Ease Ease);
internal readonly record struct SamplerFrame(StoryboardImage Image, int EndMilliseconds);
internal sealed record SamplerFrames(bool Loop, SamplerFrame[] Frames, StoryboardImage[] DistinctImages);

internal sealed class SamplerNode(SceneNode authored)
{
    internal string Id { get; } = authored.Id;
    internal NodeKind Kind { get; } = authored.Kind;
    internal Vector3[] Base { get; } = authored.Base;
    internal Matrix3x2 BaseTransform { get; init; }
    internal bool HasAnimatedBasis { get; init; }
    internal bool HasAnimatedPosition { get; init; }
    internal Matrix3x2 BasePivotTransform { get; set; }
    internal Vector2 Origin { get; set; }
    internal bool HasAnimatedFlip { get; set; }
    internal StoryboardDrawKind DrawKind { get; } = authored.Kind switch
    {
        NodeKind.Rect => StoryboardDrawKind.Rect,
        NodeKind.Ellipse => StoryboardDrawKind.Ellipse,
        _ => StoryboardDrawKind.Sprite
    };
    internal StoryboardBlend Blend { get; } = authored.Blend;
    internal StoryboardSampling Sampling { get; } = authored.Sampling;
    internal LocalRect? ClipRect { get; } = authored.ClipRect;
    internal StoryboardGradient? Gradient { get; } = authored.Gradient;
    internal Vector2 Size { get; set; } = authored.Size;
    internal int First { get; set; } = -1;
    internal int Last { get; set; } = -1;
    internal SamplerKey[]?[] Tracks { get; } = new SamplerKey[10][];
    internal SamplerPathKey[]? Path { get; set; }
    internal StoryboardImage? Image { get; set; }
    internal SamplerFrames? Frames { get; set; }
    internal SamplerNode[] Children { get; set; } = [];
    internal SamplerNode[] TextureChildren { get; set; } = [];
    internal int TextureUseCost { get; set; }
    internal SamplerWindow[]? Windows { get; init; }
}
