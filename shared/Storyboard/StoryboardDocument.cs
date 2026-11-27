using System.Collections.ObjectModel;
using System.Numerics;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Shared.Storyboard;

public static class StoryboardLimits
{
    public const int MaxSourceBytes = 2 * 1024 * 1024;
    public const int MaxAssetCount = 1024;
    public const int MaxTextureDimension = 4096;
    public const int GradientRampWidth = 512;
    // Legacy caller budgets; no aggregate pixel budget is applied when compiling metadata.
    public const long MaxTotalTexturePixels = 16_777_216;
    public const long MaxLocalPreviewTexturePixels = 25_165_824;
    internal const int MaxBar = 1_000_000;
    internal const int MaxDepth = 16;
    internal const int MaxNodes = 12_000;
    internal const int MaxEntriesPerNode = 32;
    internal const int MaxTokenLength = 1024;
    internal const int MaxRepeatCount = 4096;
    internal const int MaxCompiledMoments = 250_000;
    internal const int MaxCompiledTextureUses = 250_000;
    internal const int MaxExpandedSequenceSpans = 250_000;
}

public sealed record StoryboardDiagnostic(
    string Code, string SourceName, int Line, int Column, string? ObjectId, string Message)
{
    public override string ToString() => $"{SourceName}:{Line}:{Column}: {Code}: " +
        (ObjectId is null ? Message : $"{ObjectId} / {Message}");
}

public sealed class StoryboardResult<T> where T : class
{
    internal StoryboardResult(T value) => Value = value;
    internal StoryboardResult(StoryboardDiagnostic diagnostic) => Diagnostic = diagnostic;
    public T? Value { get; }
    public StoryboardDiagnostic? Diagnostic { get; }
}

public enum StoryboardBackdropMode { Keep, Replace }
public enum StoryboardDrawKind { Sprite, Rect, Ellipse }
public enum StoryboardBlend { Normal, Add }
public enum StoryboardSampling { Linear, Nearest }

/// <summary>sRGB bytes with independent linear alpha.</summary>
public readonly record struct StoryboardColor(byte R, byte G, byte B, byte A = 255)
{
    public Vector4 ToLinearRgba() => new(Linear(R), Linear(G), Linear(B), A / 255f);

    private static float Linear(byte channel)
    {
        var srgb = channel / 255f;
        return srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
    }
}

public readonly record struct StoryboardBackdrop(StoryboardBackdropMode Mode, StoryboardColor Color);
public readonly record struct StoryboardTextureSize(int Width, int Height);
public readonly record struct StoryboardCrop(int X, int Y, int Width, int Height);
public readonly record struct StoryboardImage(string AssetId, StoryboardCrop? Crop);

/// <summary>A half-open media time interval, in seconds.</summary>
public readonly record struct StoryboardTextureInterval(double From, double Until);

/// <summary>Usage of a whole declared relative image path, with aliases and crops coalesced.
/// Intervals returned by the sampler are sorted and merged, including adjacent intervals.</summary>
public sealed record StoryboardTextureUsage(string Path, IReadOnlyList<StoryboardTextureInterval> Intervals);

/// <summary>Usage of one logical asset/crop pair. Intervals returned by the sampler are sorted and merged.</summary>
public sealed record StoryboardImageUsage(StoryboardImage Image, IReadOnlyList<StoryboardTextureInterval> Intervals);

public readonly record struct StoryboardClipRect(float X, float Y, float Width, float Height, Matrix3x2 Transform);
public readonly record struct StoryboardGradientStop(float At, StoryboardColor Color);

public sealed class StoryboardGradient
{
    private readonly Vector4[] _linearColors;
    public Vector2 From { get; }
    public Vector2 To { get; }
    public IReadOnlyList<StoryboardGradientStop> Stops { get; }

    internal StoryboardGradient(Vector2 from, Vector2 to, StoryboardGradientStop[] stops)
    {
        From = from;
        To = to;
        Stops = Array.AsReadOnly(stops);
        _linearColors = Array.ConvertAll(stops, stop => stop.Color.ToLinearRgba());
    }

    /// <summary>Samples an interpolated straight (not premultiplied) linear RGBA color.</summary>
    public Vector4 SampleLinear(float at)
    {
        if (!(at > 0f))
            return _linearColors[0];
        if (at >= 1f)
            return _linearColors[^1];
        var lo = 0;
        var hi = Stops.Count - 1;
        while (hi - lo > 1)
        {
            var mid = lo + (hi - lo) / 2;
            if (Stops[mid].At <= at) lo = mid;
            else hi = mid;
        }
        var start = Stops[lo].At;
        return Vector4.Lerp(_linearColors[lo], _linearColors[hi], (at - start) / (Stops[hi].At - start));
    }
}

/// <summary>Transform maps unpivoted local [0,Width] × [0,Height] into storyboard space.
/// Tint is linear RGB. ClipStart/ClipCount refer to the sampler's current clip list.</summary>
public readonly record struct StoryboardDrawItem(
    string Id, StoryboardDrawKind Kind, float Width, float Height, Matrix3x2 Transform,
    StoryboardImage? Image, StoryboardGradient? Gradient, float Opacity, Vector3 Tint,
    StoryboardBlend Blend, StoryboardSampling Sampling, int ClipStart, int ClipCount);

/// <summary>Validated content only; no scripts are executed and no resources are opened.</summary>
public sealed class StoryboardDocument
{
    public string SourceName { get; }
    public IReadOnlyDictionary<string, string> Assets { get; }
    public IReadOnlyDictionary<string, StoryboardImage> Regions { get; }
    public StoryboardBackdrop Backdrop { get; }
    public bool UsesBarClock { get; }
    internal AssetDeclaration[] AssetDeclarations { get; }
    internal RegionDeclaration[] RegionDeclarations { get; }
    internal SceneNode[] Roots { get; }

    internal StoryboardDocument(string sourceName, AssetDeclaration[] assets, RegionDeclaration[] regions,
        SceneNode[] roots, StoryboardBackdrop backdrop)
    {
        SourceName = sourceName;
        AssetDeclarations = assets;
        RegionDeclarations = regions;
        Regions = new ReadOnlyDictionary<string, StoryboardImage>(regions.ToDictionary(
            region => region.Id, region => new StoryboardImage(region.AssetId, region.Crop), StringComparer.Ordinal));
        Roots = roots;
        Backdrop = backdrop;
        UsesBarClock = Array.Exists(roots, root => root.Clock == Clock.Bar);
        Assets = new ReadOnlyDictionary<string, string>(assets.ToDictionary(
            asset => asset.Id, asset => asset.Path, StringComparer.Ordinal));
    }

    public static StoryboardResult<StoryboardDocument> Parse(string source, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceName);
        if (sourceName.Length == 0) sourceName = "<storyboard>";
        try
        {
            return new(StoryboardDecoder.Decode(KdlReader.Read(source), sourceName));
        }
        catch (StoryboardError error)
        {
            return new(error.Diagnostic(sourceName));
        }
    }
}

internal readonly record struct SourcePoint(int Line, int Column);

internal sealed class StoryboardError(string code, SourcePoint point, string? objectId, string message)
    : Exception(message)
{
    internal StoryboardDiagnostic Diagnostic(string sourceName) =>
        new(code, sourceName, point.Line, point.Column, objectId, Message);
}

internal enum Clock { Media, Bar }
internal enum NodeKind { Clip, Group, Sprite, Rect, Ellipse }
internal enum Ease { Step, Linear, SineIn, SineOut, SineInOut, QuadIn, QuadOut, QuadInOut }
internal enum Property { X, Y, ScaleX, ScaleY, Rotation, Opacity, Tint, Visible, FlipX, FlipY }

internal readonly record struct AssetDeclaration(string Id, string Path, SourcePoint Point);
internal readonly record struct RegionDeclaration(string Id, string AssetId, StoryboardCrop Crop, SourcePoint Point);
internal readonly record struct Active(ExactBarTime From, ExactBarTime Until);
internal readonly record struct Repeat(ExactBarTime At, ExactBarTime Duration, int Count, ExactBarTime Period);
internal readonly record struct LocalRect(float X, float Y, float Width, float Height);
internal readonly record struct Key(ExactBarTime At, Vector3 Value, Ease Ease, SourcePoint Point);
internal readonly record struct PathKey(ExactBarTime At, Vector2 Position, Vector2? C1, Vector2 C2, Ease Ease, SourcePoint Point);
internal readonly record struct Frame(string ImageId, int DurationMilliseconds, SourcePoint Point);
internal sealed record Sequence(bool Loop, Frame[] Frames);

internal sealed class SceneNode(string id, SourcePoint point, NodeKind kind, Clock clock, int order)
{
    internal string Id { get; } = id;
    internal SourcePoint Point { get; } = point;
    internal NodeKind Kind { get; } = kind;
    internal Clock Clock { get; } = clock;
    internal int Order { get; } = order;
    internal Active? Active { get; set; }
    internal Repeat? Repeat { get; set; }
    internal Vector3[] Base { get; } =
        [Vector3.Zero, Vector3.Zero, Vector3.One, Vector3.One, Vector3.Zero,
         Vector3.One, Vector3.One, Vector3.One, Vector3.Zero, Vector3.Zero];
    internal Key[]?[] Tracks { get; } = new Key[10][];
    internal PathKey[]? Path { get; set; }
    internal Vector2 Pivot { get; set; } = new(0.5f, 0.5f);
    internal StoryboardBlend Blend { get; set; }
    internal StoryboardSampling Sampling { get; set; }
    internal string? ImageId { get; set; }
    internal Sequence? Sequence { get; set; }
    internal Vector2 Size { get; set; }
    internal StoryboardGradient? Gradient { get; set; }
    internal LocalRect? ClipRect { get; set; }
    internal SceneNode[] Children { get; set; } = [];
}
