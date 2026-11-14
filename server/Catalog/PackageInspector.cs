using System.IO.Compression;
using System.Security.Cryptography;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.IO;

namespace DynamiteUniverse.Server.Catalog;

internal sealed record ValidatedPack(V2Pack Pack, IReadOnlyList<ReleaseChart> Charts, CatalogDisplay Display, string? CoverPath);
public sealed record ReleaseChart(string ChartId, string Digest, string RulesetId);

internal static class PackageInspector
{
    internal const long MaxArchive = SafePackageArchive.MaxArchive;
    internal static ValidatedPack Inspect(string zipPath, string staging)
    {
        var expandedRoot = Path.Combine(staging, "expanded");
        SafePackageArchive.Extract(zipPath, expandedRoot);
        using var zip = ZipFile.OpenRead(zipPath);
        var pack = V2JsonDecoder.DecodePackFile(Path.Combine(expandedRoot, "meta.json"));
        string Resolve(string relative) => V2PackageWriter.ResolveRegularFile(expandedRoot, relative);
        var durations = new Dictionary<string, double>();
        var resolved = V2PackageValidator.Validate(pack, new V2PackageValidationHooks
        {
            FileExists = path => File.Exists(Resolve(path)),
            LoadChart = entry => V2JsonDecoder.DecodeChartFile(Resolve(entry.File)),
            AudioDurationSeconds = path => durations.TryGetValue(path, out var value) ? value :
                durations[path] = V2BuiltInAudioProbe.ProbeFile(Resolve(path), path).DurationSeconds,
        });
        var referenced = new HashSet<string>(StringComparer.Ordinal) { "meta.json" };
        if (pack.Cover is not null) referenced.Add(pack.Cover);
        if (pack.Audio is not null) referenced.Add(pack.Audio);
        var charts = new List<ReleaseChart>();
        var display = new List<ChartDisplay>();
        var auditedAudio = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chart in resolved)
        {
            referenced.Add(chart.Entry.File); referenced.Add(chart.ResolvedAudio);
            using var audio = File.OpenRead(Resolve(chart.ResolvedAudio));
            var digest = V2GameplayDigest.Compute(chart.Chart, chart.Entry.JudgePreset, Convert.ToHexStringLower(SHA256.HashData(audio)));
            charts.Add(new(chart.Entry.Id, digest.Sha256, digest.RulesetId));
            if (auditedAudio.Add(chart.ResolvedAudio)) CatalogMediaValidator.Validate(Resolve(chart.ResolvedAudio), false);
            var entry = chart.Entry;
            display.Add(new(entry.Id, entry.Difficulty.ToString().ToLowerInvariant(), entry.DifficultyKey,
                entry.Level, entry.Unrated, entry.Charters, chart.Chart.Bpms.Min(b => b.Bpm), chart.Chart.Bpms.Max(b => b.Bpm)));
        }
        if (zip.Entries.Any(e => !referenced.Contains(e.FullName))) throw new InvalidDataException("Unreferenced package content");
        CoverDisplay? cover = null;
        string? coverPath = null;
        if (pack.Cover is not null)
        {
            coverPath = Resolve(pack.Cover); CatalogMediaValidator.Validate(coverPath, true);
            using var image = File.OpenRead(coverPath);
            var mediaType = Path.GetExtension(pack.Cover).ToLowerInvariant() switch
            { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
            cover = new(mediaType, image.Length, Convert.ToHexStringLower(SHA256.HashData(image)));
        }
        return new(pack, charts, new(CatalogMediaValidator.Profile, display, cover), coverPath);
    }
}
