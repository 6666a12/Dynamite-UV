using DynamiteUniverse.Shared.Chart.V2;

namespace ChartTool;

/// <summary>
/// Projects the converter's audit-carrying intermediate records onto shared's frozen v2 model.
/// Disk JSON is always emitted by <see cref="V2JsonEncoder"/> so chart-tool cannot drift from the
/// canonical encoder used by the other v2 producers.
/// </summary>
internal static class V2OutputProjection
{
    public static V2Pack ToPack(ConvertedPack source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new V2Pack
        {
            Id = source.Id,
            Revision = source.Revision,
            Title = source.Title,
            Artist = source.Artist,
            Audio = source.Audio,
            Cover = source.Cover,
            Charts = source.Charts.Select(ToChartEntry).ToArray(),
        };
    }

    public static V2Chart ToChart(ConvertedChart source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new V2Chart
        {
            ChartId = source.ChartId,
            AudioOffsetSec = source.AudioOffsetSec,
            Bpms = source.Bpms.Select(item =>
                new V2BpmEvent(ToTime(item.Time), item.Bpm)).ToArray(),
            ScrollSpeeds = source.ScrollSpeeds.Select((item, index) => new V2ScrollEvent
            {
                Time = ToTime(item.Time),
                Value = item.Value,
                CurveToNext = index + 1 < source.ScrollSpeeds.Count
                    ? V2ScrollCurve.Linear
                    : null,
            }).ToArray(),
            NotesLeft = ToNotes(source.NotesLeft),
            NotesCenter = ToNotes(source.NotesCenter),
            NotesRight = ToNotes(source.NotesRight),
        };
    }

    private static V2ChartEntry ToChartEntry(ConvertedChartEntry source) => new()
    {
        Id = source.Id,
        Difficulty = ToDifficulty(source.Difficulty),
        DifficultyKey = source.DifficultyKey,
        Level = source.Level,
        Unrated = source.Unrated,
        Charters = [source.Charter],
        File = source.File,
    };

    private static IReadOnlyList<V2Note> ToNotes(IReadOnlyList<ConvertedNote> source) =>
        source.Select(ToNote).ToArray();

    private static V2Note ToNote(ConvertedNote source)
    {
        var type = ToNoteType(source.Type);
        if (type is not (V2NoteType.Hold or V2NoteType.Mixer))
        {
            if (source.Nodes.Count != 0)
                throw new InvalidOperationException(
                    $"Non-path converted note '{source.Id}' unexpectedly has path nodes.");
            return new V2BasicNote
            {
                Id = source.Id,
                Type = type,
                Time = ToTime(source.Time),
                Center = source.Center,
                Width = source.Width,
            };
        }

        return new V2PathNote
        {
            Id = source.Id,
            Type = type,
            Time = ToTime(source.Time),
            Center = source.Center,
            Width = source.Width,
            CurveToNext = V2PathCurve.Linear,
            Nodes = source.Nodes.Select((node, index) => new V2PathNode
            {
                Id = node.Id,
                Time = ToTime(node.Time),
                Center = node.Center,
                Width = node.Width,
                CurveToNext = index + 1 < source.Nodes.Count
                    ? V2PathCurve.Linear
                    : null,
                Judge = type == V2NoteType.Hold
                    ? node.Judge ?? throw new InvalidOperationException(
                        $"Converted Hold node '{node.Id}' must set judge.")
                    : node.Judge,
                JudgeWasExplicit = type == V2NoteType.Hold,
            }).ToArray(),
        };
    }

    private static ExactBarTime ToTime(ChartTool.IsolatedV2.RationalBarTime source) =>
        ExactBarTime.FromJsonComponents(source.Bar, source.FractionNumerator, source.Denominator);

    private static V2Difficulty ToDifficulty(string source) => source switch
    {
        "casual" => V2Difficulty.Casual,
        "normal" => V2Difficulty.Normal,
        "hard" => V2Difficulty.Hard,
        "mega" => V2Difficulty.Mega,
        "giga" => V2Difficulty.Giga,
        "tech" => V2Difficulty.Tech,
        "custom" => V2Difficulty.Custom,
        _ => throw new InvalidOperationException($"Unsupported converted difficulty '{source}'."),
    };

    private static V2NoteType ToNoteType(string source) => source switch
    {
        "tap" => V2NoteType.Tap,
        "drag" => V2NoteType.Drag,
        "exTap" => V2NoteType.ExTap,
        "hold" => V2NoteType.Hold,
        "mixer" => V2NoteType.Mixer,
        "mine" => V2NoteType.Mine,
        "barLine" => V2NoteType.BarLine,
        _ => throw new InvalidOperationException($"Unsupported converted note type '{source}'."),
    };
}

internal static class PackageWriter
{
    public static bool Write(string outputDirectory, LegacyPack source, ConvertedPack converted,
        DiagnosticBag diagnostics)
    {
        string output;
        try
        {
            output = Path.GetFullPath(outputDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Error(outputDirectory, "", $"invalid output directory: {ex.Message}");
            return false;
        }
        if (Directory.Exists(output) || File.Exists(output))
        {
            diagnostics.Error(output, "", "output already exists; refusing to overwrite");
            return false;
        }
        if (IsSameOrNested(output, source.SourceDirectory))
        {
            diagnostics.Error(output, "", "output may not equal or be nested inside the source pack");
            return false;
        }
        var outputParent = Path.GetDirectoryName(output);
        if (string.IsNullOrEmpty(outputParent) || !Directory.Exists(outputParent))
        {
            diagnostics.Error(output, "", "output parent directory must already exist");
            return false;
        }

        var files = new List<(string PackagePath, byte[]? Content, string? CopySource)>
        {
            ("meta.json", V2JsonEncoder.EncodePack(V2OutputProjection.ToPack(converted)), null),
            (source.AudioPath, null, ResolveSource(source, source.AudioPath)),
        };
        if (source.CoverPath is not null)
            files.Add((source.CoverPath, null, ResolveSource(source, source.CoverPath)));
        foreach (var entry in converted.Charts)
        {
            files.Add((entry.File,
                V2JsonEncoder.EncodeChart(V2OutputProjection.ToChart(entry.Chart)), null));
        }

        var collision = files.GroupBy(item => item.PackagePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (collision is not null)
        {
            diagnostics.Error(output, "", $"output package path collision: {collision.Key}");
            return false;
        }

        var staging = output + ".chart-tool-staging-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var file in files)
            {
                if (!TextRules.TryResolveFile(staging, file.PackagePath, out var destination))
                    throw new InvalidOperationException($"unsafe output path: {file.PackagePath}");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (file.Content is not null)
                    File.WriteAllBytes(destination, file.Content);
                else
                    File.Copy(file.CopySource!, destination, overwrite: false);
            }
            Directory.Move(staging, output);
            return true;
        }
        catch (Exception ex)
        {
            diagnostics.Error(output, "", $"failed to write package: {ex.Message}");
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            return false;
        }
    }

    private static string ResolveSource(LegacyPack source, string packagePath)
    {
        if (!TextRules.TryResolveFile(source.SourceDirectory, packagePath, out var path))
            throw new InvalidOperationException($"unsafe source path: {packagePath}");
        return path;
    }

    private static bool IsSameOrNested(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}
