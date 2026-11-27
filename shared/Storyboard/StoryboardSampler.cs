using System.Numerics;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Shared.Storyboard;

/// <summary>Samples validated storyboard data at an explicit, uncalibrated media second.
/// Reuse a sampler and output list from one thread; no playhead or playback state is retained.</summary>
public sealed class StoryboardSampler
{
    private readonly SamplerRoot[] _roots;
    private readonly IReadOnlyDictionary<string, string> _assetPaths;
    private readonly List<StoryboardClipRect> _clips;
    private readonly StoryboardClipRect[] _clipStack = new StoryboardClipRect[StoryboardLimits.MaxDepth];
    private readonly int[] _clipStarts = new int[StoryboardLimits.MaxDepth + 1];

    /// <summary>Ancestor snapshots (outermost first) referenced by the current sample's draw items.
    /// Clip coordinates are local, and the transform maps them into storyboard space.
    /// Data is valid until the next sample.</summary>
    public IReadOnlyList<StoryboardClipRect> Clips { get; }
    public int MaxDrawItems { get; }

    /// <summary>Authored gradient drawables in stable draw order, with repeat instances sharing the same gradient.</summary>
    public IReadOnlyList<StoryboardGradient> Gradients { get; }

    internal StoryboardSampler(SamplerRoot[] roots, IReadOnlyDictionary<string, string> assetPaths,
        IReadOnlyList<StoryboardGradient> gradients, int drawCount, int clipCapacity)
    {
        _roots = roots;
        _assetPaths = assetPaths;
        Gradients = gradients;
        MaxDrawItems = drawCount;
        _clips = new(clipCapacity);
        Clips = _clips.AsReadOnly();
    }

    /// <summary>Binds previously validated image dimensions without decoding or budgeting resident pixels.
    /// The preview budget argument is retained for caller compatibility and has no effect.</summary>
    public static StoryboardResult<StoryboardSampler> Compile(StoryboardDocument document,
        IReadOnlyDictionary<string, StoryboardTextureSize> textures, V2BpmTimeline? timeline = null,
        bool useLocalPreviewTextureBudget = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(textures);
        try
        {
            return new StoryboardResult<StoryboardSampler>(
                new StoryboardCompiler(document.UsesBarClock ? timeline : null).Compile(document, textures));
        }
        catch (StoryboardError error)
        {
            return new(error.Diagnostic(document.SourceName));
        }
    }

    /// <summary>Returns conservative usage by declared texture path. Aliases and atlas regions of the same
    /// path are merged; a host that allocates cropped copies should use <see cref="BuildImageUsage"/>.</summary>
    public IReadOnlyList<StoryboardTextureUsage> BuildTextureUsage(double mediaDurationSeconds) =>
        StoryboardUsageBuilder.GroupByPath(BuildImageUsage(mediaDurationSeconds), _assetPaths);

    /// <summary>Returns usage by logical image (asset ID and optional crop) within [0, media duration).
    /// Does not evaluate opacity, visibility, transforms, or spatial clips. Non-positive or non-finite durations
    /// return an empty table. Very dense sequences conservatively include all their images for the affected window.</summary>
    public IReadOnlyList<StoryboardImageUsage> BuildImageUsage(double mediaDurationSeconds) =>
        StoryboardUsageBuilder.Build(_roots, mediaDurationSeconds);

    /// <summary>Clears and fills the caller-owned list in draw order. Invalid/negative seconds produce an empty list.
    /// Valid recording length and backdrop/visibility policy are controlled by the host.</summary>
    public void Sample(double mediaSeconds, List<StoryboardDrawItem> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        _clips.Clear();
        if (!double.IsFinite(mediaSeconds) || mediaSeconds < 0) return;
        foreach (var root in _roots)
            SampleNode(root.Node, root.Moments, mediaSeconds, Matrix3x2.Identity, 0, destination);
    }

    private void SampleNode(SamplerNode node, SamplerTimeTable moments, double seconds, Matrix3x2 parent,
        int clipCount, List<StoryboardDrawItem> destination)
    {
        if (node.Windows is { } windows)
        {
            var lo = 0;
            var hi = windows.Length;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (seconds >= windows[mid].From) lo = mid + 1;
                else hi = mid;
            }
            if (lo == 0 || seconds >= windows[lo - 1].Until) return;
            SampleChildren(node, windows[lo - 1].Moments, seconds, parent, clipCount, destination);
            return;
        }
        if (node.First >= 0 && (seconds < moments[node.First].Seconds || seconds >= moments[node.Last].Seconds))
            return;
        if (PropertyAt(node, Property.Visible, moments, seconds).X == 0) return;

        var transform = node.BaseTransform;
        if (node.HasAnimatedPosition) transform.Translation = PositionAt(node, moments, seconds);
        if (node.HasAnimatedBasis)
            transform = CreateTransform(transform.Translation, PropertyAt(node, Property.ScaleX, moments, seconds).X,
                PropertyAt(node, Property.ScaleY, moments, seconds).X, PropertyAt(node, Property.Rotation, moments, seconds).X);
        var world = transform * parent;
        if (node.Kind == NodeKind.Group)
        {
            if (node.ClipRect is { } clip)
            {
                _clipStack[clipCount++] = new(clip.X, clip.Y, clip.Width, clip.Height, world);
                _clipStarts[clipCount] = -1;
            }
            SampleChildren(node, moments, seconds, world, clipCount, destination);
            return;
        }
        var local = node.HasAnimatedFlip ? CreatePivotTransform(node.Origin,
            PropertyAt(node, Property.FlipX, moments, seconds).X, PropertyAt(node, Property.FlipY, moments, seconds).X)
            : node.BasePivotTransform;
        var image = node.Frames is { } frames ? StoryboardSequenceTiming.ImageAt(frames, moments[node.First], seconds) : node.Image;
        destination.Add(new(node.Id, node.DrawKind, node.Size.X, node.Size.Y, local * world, image, node.Gradient,
            PropertyAt(node, Property.Opacity, moments, seconds).X, PropertyAt(node, Property.Tint, moments, seconds),
            node.Blend, node.Sampling, ClipSlice(clipCount), clipCount));
    }

    // Compile-time constants and sampled properties must use the same arithmetic; in
    // particular keep pivot/flip separate from the parent composition to preserve rounding.
    internal static Matrix3x2 CreateTransform(Vector2 position, float scaleX, float scaleY, float degrees)
    {
        var (sine, cosine) = degrees == 0 ? (0d, 1d) : Math.SinCos(Math.IEEERemainder(degrees, 360) * (Math.PI / 180));
        return new((float)(scaleX * cosine), (float)(scaleX * sine),
            (float)(-scaleY * sine), (float)(scaleY * cosine), position.X, position.Y);
    }

    internal static Matrix3x2 CreatePivotTransform(Vector2 origin, float flipXValue, float flipYValue)
    {
        var flipX = flipXValue == 0 ? 1f : -1f;
        var flipY = flipYValue == 0 ? 1f : -1f;
        return new(flipX, 0, 0, flipY, origin.X * flipX, origin.Y * flipY);
    }

    private int ClipSlice(int count)
    {
        if (count == 0) return 0;
        if (_clipStarts[count] >= 0) return _clipStarts[count];
        var start = _clips.Count;
        for (var i = 0; i < count; i++) _clips.Add(_clipStack[i]);
        return _clipStarts[count] = start;
    }

    private void SampleChildren(SamplerNode node, SamplerTimeTable moments, double seconds, Matrix3x2 parent,
        int clipCount, List<StoryboardDrawItem> destination)
    {
        foreach (var child in node.Children)
            SampleNode(child, moments, seconds, parent, clipCount, destination);
    }

    private static Vector3 PropertyAt(SamplerNode node, Property property, SamplerTimeTable moments, double seconds) =>
        node.Tracks[(int)property] is { } keys
            ? TrackAt(keys, node.Base[(int)property], moments, seconds)
            : node.Base[(int)property];

    private static Vector3 TrackAt(SamplerKey[] keys, Vector3 baseValue, SamplerTimeTable moments, double seconds)
    {
        var lo = 0;
        var hi = keys.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (seconds >= moments[keys[mid].Index].Seconds) lo = mid + 1;
            else hi = mid;
        }
        if (lo == 0) return baseValue;
        ref readonly var key = ref keys[lo - 1];
        if (lo == keys.Length || key.Ease == Ease.Step) return key.Value;
        ref readonly var start = ref moments[key.Index];
        if (seconds == start.Seconds) return key.Value;
        ref readonly var end = ref keys[lo];
        var u = SegmentProgress(moments, key.Index, end.Index, seconds, key.Ease);
        return Vector3.Lerp(key.Value, end.Value, u);
    }

    private static Vector2 PositionAt(SamplerNode node, SamplerTimeTable moments, double seconds)
    {
        if (node.Path is not { } keys)
            return new(PropertyAt(node, Property.X, moments, seconds).X, PropertyAt(node, Property.Y, moments, seconds).X);
        var lo = 0;
        var hi = keys.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (seconds >= moments[keys[mid].Index].Seconds) lo = mid + 1;
            else hi = mid;
        }
        if (lo == 0) return new(node.Base[(int)Property.X].X, node.Base[(int)Property.Y].X);
        ref readonly var key = ref keys[lo - 1];
        if (lo == keys.Length || key.Ease == Ease.Step || seconds == moments[key.Index].Seconds) return key.Position;
        ref readonly var next = ref keys[lo];
        var u = SegmentProgress(moments, key.Index, next.Index, seconds, key.Ease);
        if (key.C1 is not { } c1) return Vector2.Lerp(key.Position, next.Position, u);
        var a = Vector2.Lerp(key.Position, c1, u);
        var b = Vector2.Lerp(c1, key.C2, u);
        var c = Vector2.Lerp(key.C2, next.Position, u);
        return Vector2.Lerp(Vector2.Lerp(a, b, u), Vector2.Lerp(b, c, u), u);
    }

    private static float SegmentProgress(SamplerTimeTable moments, int start, int end, double seconds, Ease ease)
    {
        var from = new SamplerProgressPoint(moments[start].Seconds, 0);
        var until = new SamplerProgressPoint(moments[end].Seconds, 1);
        // Relative progress is linear in seconds within a single BPM segment. At tempo changes
        // use the exact Bar fraction computed at load time, before rounding to absolute positions.
        if (moments.ProgressPoints(start, end) is { } points)
        {
            var lo = 0;
            var hi = points.Length;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (seconds >= points[mid].Seconds) lo = mid + 1;
                else hi = mid;
            }
            if (lo > 0) from = points[lo - 1];
            if (lo < points.Length) until = points[lo];
        }
        var span = until.Seconds - from.Seconds;
        var local = double.IsFinite(span) ? (seconds - from.Seconds) / span
            : (seconds / 2 - from.Seconds / 2) / (until.Seconds / 2 - from.Seconds / 2);
        var u = Math.Clamp(from.Progress + (until.Progress - from.Progress) * local, 0, 1);
        return (float)(ease switch
        {
            Ease.SineIn => 1 - Math.Cos(Math.PI * u / 2),
            Ease.SineOut => Math.Sin(Math.PI * u / 2),
            Ease.SineInOut => (1 - Math.Cos(Math.PI * u)) / 2,
            Ease.QuadIn => u * u,
            Ease.QuadOut => 1 - (1 - u) * (1 - u),
            Ease.QuadInOut => u < 0.5 ? 2 * u * u : 1 - 2 * (1 - u) * (1 - u),
            _ => u
        });
    }

}
