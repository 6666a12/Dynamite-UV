using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DynamiteUniverse.Shared.Storyboard;
using Godot;

namespace DynamiteUniverse.Game;

/// <summary>Validates local bindings and metadata before any streamed pixel allocation.</summary>
internal static class GameplayStoryboardLoader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int MaxIndexBytes = 64 * 1024;
    internal const int MaxPngBytes = 16 * 1024 * 1024;

    public static LoadedPerformance? Load(ChartPack pack, LoadedChart chart, double audioDuration)
    {
        if (!ChartPack.IsEditorOrInternal || !pack.IsV2 || pack.Installation is not null ||
            pack.DirPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return null;
        var local = pack.DirPath.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(pack.DirPath) : pack.DirPath;
        var packRoot = Path.GetFullPath(local);
        var installedRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("user://online/installed"));
        if (StoryboardLocalFiles.Within(installedRoot, packRoot)) return null;
        var root = Path.Combine(packRoot, "storyboard");
        if (!File.Exists(Path.Combine(root, "index.json"))) return null;
        StoryboardLocalFiles.CheckDirectoryChain(root);
        var binding = StoryboardLocalFiles.SafePath(root, "index.json", ".json");
        var script = ReadBinding(binding, chart.ChartId);
        if (script is null) return null;

        var started = Stopwatch.GetTimestamp();
        var scriptPath = StoryboardLocalFiles.SafePath(root, script, ".uvsb");
        var bytes = StoryboardLocalFiles.Read(scriptPath, StoryboardLimits.MaxSourceBytes);
        var text = StrictUtf8.GetString(bytes);
        var readMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        started = Stopwatch.GetTimestamp();
        var document = Require(StoryboardDocument.Parse(text, script));
        var parseMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (document.UsesBarClock && chart.V2Timeline is null)
            throw new InvalidDataException($"{script}: bar clock requires the selected strict v2 timeline");

        var sources = new Dictionary<string, StoryboardPngSource>(StringComparer.OrdinalIgnoreCase);
        var sourcesById = new Dictionary<string, StoryboardPngSource>(StringComparer.Ordinal);
        var dimensions = new Dictionary<string, StoryboardTextureSize>(StringComparer.Ordinal);
        var scriptDir = Path.GetDirectoryName(scriptPath)!;
        started = Stopwatch.GetTimestamp();
        Span<byte> header = stackalloc byte[33];
        foreach (var (id, relative) in document.Assets)
        {
            var path = StoryboardLocalFiles.SafePath(scriptDir, relative, ".png");
            if (sources.TryGetValue(path, out var source))
            {
                if (!StringComparer.Ordinal.Equals(path, source.Path))
                    throw new InvalidDataException($"{script}: case-colliding texture paths '{source.Path}' and '{path}'");
            }
            else
            {
                using var stream = StoryboardLocalFiles.Open(path, MaxPngBytes, out var length);
                stream.ReadExactly(header);
                source = new(path, length, ReadPngSize(header, relative));
                sources.Add(path, source);
            }
            dimensions.Add(id, source.Size);
            sourcesById.Add(id, source);
        }
        var preflightMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        started = Stopwatch.GetTimestamp();
        var sampler = Require(StoryboardSampler.Compile(document, dimensions, chart.V2Timeline));
        var lookup = new Dictionary<StoryboardImage, int>();
        var canonical = new Dictionary<StoryboardTextureKey, int>();
        var keys = new List<StoryboardTextureKey>();
        var intervals = new List<List<StoryboardTextureInterval>>();
        foreach (var usage in sampler.BuildImageUsage(audioDuration))
        {
            var key = new StoryboardTextureKey(sourcesById[usage.Image.AssetId], usage.Image.Crop);
            if (!canonical.TryGetValue(key, out var slot))
            {
                slot = keys.Count;
                canonical.Add(key, slot);
                keys.Add(key);
                intervals.Add(new());
            }
            lookup.Add(usage.Image, slot);
            intervals[slot].AddRange(usage.Intervals);
        }
        var requests = new StoryboardTextureRequest[keys.Count];
        for (var i = 0; i < requests.Length; i++)
            requests[i] = new(keys[i], Merge(intervals[i]));
        return new(document, sampler, new(requests, lookup), bytes.Length,
            readMs, parseMs, preflightMs, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static StoryboardTextureInterval[] Merge(List<StoryboardTextureInterval> intervals)
    {
        intervals.Sort(static (a, b) => a.From.CompareTo(b.From));
        var count = 0;
        foreach (var range in intervals.ToArray())
        {
            if (count == 0 || range.From > intervals[count - 1].Until) intervals[count++] = range;
            else if (range.Until > intervals[count - 1].Until)
                intervals[count - 1] = intervals[count - 1] with { Until = range.Until };
        }
        return intervals.GetRange(0, count).ToArray();
    }

    private static T Require<T>(StoryboardResult<T> result) where T : class =>
        result.Value ?? throw new InvalidDataException(result.Diagnostic?.ToString() ?? "Invalid Storyboard");

    private static string? ReadBinding(string path, string chartId)
    {
        try
        {
            using var document = JsonDocument.Parse(StoryboardLocalFiles.Read(path, MaxIndexBytes), new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Storyboard index must be an object");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var charts = default(JsonElement);
            foreach (var p in root.EnumerateObject())
            {
                if (!seen.Add(p.Name)) throw new InvalidDataException($"Duplicate Storyboard index field '{p.Name}'");
                if (p.Name == "version" && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var version) && version == 0)
                    continue;
                if (p.Name == "charts" && p.Value.ValueKind == JsonValueKind.Object) charts = p.Value;
                else throw new InvalidDataException($"Invalid Storyboard index field '{p.Name}'");
            }
            if (!seen.Contains("version") || charts.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Storyboard index requires version 0 and charts");
            seen.Clear();
            string? selection = null;
            foreach (var entry in charts.EnumerateObject())
            {
                if (!seen.Add(entry.Name) || entry.Value.ValueKind != JsonValueKind.String || entry.Value.GetString() is not { Length: > 0 } file)
                    throw new InvalidDataException($"Invalid or duplicate Storyboard chart binding '{entry.Name}'");
                StoryboardLocalFiles.ValidatePath(file, ".uvsb");
                if (StringComparer.Ordinal.Equals(entry.Name, chartId)) selection = file;
            }
            return selection;
        }
        catch (InvalidOperationException ex)
        {
            // JsonDocument validates grammar before lazily unescaping strings, including property names.
            throw new InvalidDataException("Storyboard index contains an invalid Unicode string", ex);
        }
    }

    internal static StoryboardTextureSize ReadPngSize(ReadOnlySpan<byte> bytes, string file)
    {
        if (bytes.Length < 33 || BinaryPrimitives.ReadUInt64BigEndian(bytes) != 0x89504e470d0a1a0aUL ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) != 13 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException($"Invalid PNG header for Storyboard asset '{file}'");
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..]);
        if (width is <= 0 or > StoryboardLimits.MaxTextureDimension || height is <= 0 or > StoryboardLimits.MaxTextureDimension)
            throw new InvalidDataException($"Storyboard PNG '{file}' exceeds dimension limit {StoryboardLimits.MaxTextureDimension}");
        var depth = bytes[24];
        if (bytes[25] switch { 0 => depth is not (1 or 2 or 4 or 8 or 16), 3 => depth is not (1 or 2 or 4 or 8),
            2 or 4 or 6 => depth is not (8 or 16), _ => true } || bytes[26] != 0 || bytes[27] != 0 || bytes[28] > 1)
            throw new InvalidDataException($"Unsupported PNG encoding for Storyboard asset '{file}'");
        return new(width, height);
    }
}

internal sealed record StoryboardPngSource(string Path, int Length, StoryboardTextureSize Size);
internal readonly record struct StoryboardTextureKey(StoryboardPngSource Source, StoryboardCrop? Crop)
{
    internal int Width => Crop?.Width ?? Source.Size.Width;
    internal int Height => Crop?.Height ?? Source.Size.Height;
    // Charge whole rows rounded up for upload, including RGBA8 conversion and an independent crop.
    internal long TextureBytes => ((Width * 4L + 255) & ~255L) * Height;
    internal long ScratchBytes => 2L * Source.Length + 8L * Source.Size.Width * Source.Size.Height +
        (Crop is null ? 0 : 4L * Width * Height);
}
internal sealed record StoryboardTextureRequest(StoryboardTextureKey Key, StoryboardTextureInterval[] Intervals);
internal sealed record StoryboardTexturePlan(StoryboardTextureRequest[] Requests, IReadOnlyDictionary<StoryboardImage, int> Lookup);
internal sealed record LoadedPerformance(StoryboardDocument Document, StoryboardSampler Sampler,
    StoryboardTexturePlan TexturePlan, int SourceBytes, double ReadMs, double ParseMs, double AssetPreflightMs, double CompileMs);
