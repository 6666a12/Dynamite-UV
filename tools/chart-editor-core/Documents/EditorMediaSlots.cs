using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Maps owned media replacements to safe package-relative slots.</summary>
internal static class EditorMediaSlots
{
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    public static string AudioDestination(EditorDocument document, EditableChart? owner, string external,
        string? newChartId = null, IEnumerable<string>? reserved = null)
    {
        var ext = Path.GetExtension(external).ToLowerInvariant();
        var packScope = owner is null && newChartId is null;
        var old = packScope ? document.Audio : owner?.Audio;
        var id = owner?.Id ?? newChartId;
        var preferred = old is not null ? Path.ChangeExtension(old, ext).Replace('\\', '/')
            : packScope ? "audio" + ext : $"audio/{id}{ext}";
        var occupied = Occupied(document);
        // An exclusive slot may be replaced in place, including a pending first-save import.
        var sameSlot = old is not null && Paths.Equals(preferred, old);
        var shared = Paths.Equals(document.Cover, old) ||
            (!packScope && Paths.Equals(document.Audio, old)) ||
            document.Charts.Any(chart => chart != owner && chart.Audio is not null && Paths.Equals(chart.Audio, old));
        if (sameSlot && !shared && !(reserved?.Contains(preferred, Paths) ?? false)) return old!;
        if (reserved is not null) occupied.UnionWith(reserved);
        if (Available(document, occupied, preferred)) return preferred;

        var stem = packScope ? "audio" : $"audio/{id}";
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{stem}-{suffix}{ext}";
            if (Available(document, occupied, candidate)) return candidate;
        }
    }

    internal static HashSet<string> Occupied(EditorDocument document)
    {
        var paths = new HashSet<string>(Paths) { "meta.json" };
        if (document.Audio is { } audio) paths.Add(audio);
        if (document.Cover is { } cover) paths.Add(cover);
        foreach (var chart in document.Charts)
        {
            paths.Add(chart.File);
            if (chart.Audio is { } slot) paths.Add(slot);
        }
        foreach (var mapping in document.PendingResourceReplacements) paths.Add(mapping.PackageRelativePath);
        return paths;
    }

    internal static bool Available(EditorDocument document, HashSet<string> occupied, string slot)
    {
        if (occupied.Contains(slot)) return false;
        return document.PackageDirectory is not { } root || !Path.Exists(Path.Combine(root, slot));
    }

    // Structural undo can remove a chart, not revert its recording. Set aside only its
    // current pending import (a reference, not old media bytes), and never detach a shared slot.
    internal static V2ExternalResourceMapping? DetachUnusedPendingAudio(EditorDocument document, string? slot)
    {
        if (slot is null || document.GetPendingResource(slot) is not { } source ||
            Paths.Equals(slot, document.Audio) || Paths.Equals(slot, document.Cover) ||
            document.Charts.Any(chart => Paths.Equals(slot, chart.Audio))) return null;
        document.RemovePendingResource(slot);
        return new(source, slot);
    }

    internal static void StageChartImport(EditorDocument document, V2ExternalResourceMapping import)
    {
        if (!File.Exists(import.ExternalSourcePath))
            throw new FileNotFoundException("External resource does not exist.", import.ExternalSourcePath);
        if (document.GetPendingResource(import.PackageRelativePath) is { } existing && !Paths.Equals(existing, import.ExternalSourcePath))
            throw new IOException($"The recording slot is already in use: {import.PackageRelativePath}");
        document.SetPendingResource(import.PackageRelativePath, import.ExternalSourcePath);
    }

    internal static V2ExternalResourceMapping[] ReferencedMappings(V2Pack pack,
        IEnumerable<V2ExternalResourceMapping> mappings)
    {
        var references = new HashSet<string>(Paths);
        // Preserve an authored package default even when all current charts override it.
        if (pack.Audio is { } audio) references.Add(audio);
        if (pack.Cover is { } cover) references.Add(cover);
        foreach (var chart in pack.Charts)
        {
            if (chart.Audio is { } chartAudio) references.Add(chartAudio);
        }
        var active = new Dictionary<string, V2ExternalResourceMapping>(Paths);
        foreach (var mapping in mappings)
            if (references.Contains(mapping.PackageRelativePath)) active[mapping.PackageRelativePath] = mapping;
        return active.Values.ToArray();
    }
}
