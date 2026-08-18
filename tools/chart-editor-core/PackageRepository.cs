using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Strict local-directory package opening for the desktop editor.</summary>
public static class EditorPackageRepository
{
    public static EditorDocument Open(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var root = Path.GetFullPath(packageDirectory);
        V2PackageWriter.ValidateDirectoryTree(root);
        var metaPath = V2PackageWriter.ResolveRegularFile(root, "meta.json");
        var pack = V2JsonDecoder.DecodePackFile(metaPath);
        var resolved = V2PackageValidator.Validate(pack, new V2PackageValidationHooks
        {
            LoadChart = entry => V2JsonDecoder.DecodeChartFile(
                V2PackageWriter.ResolveRegularFile(root, entry.File)),
            FileExists = relative => IsRegularFile(root, relative),
            ValidateResolvedAudio = (_, relative) =>
            {
                V2PackageWriter.ResolveRegularFile(root, relative);
            },
        }, metaPath);
        if (pack.Cover is not null && !IsRegularFile(root, pack.Cover))
            throw new V2DiagnosticException(metaPath, "/cover",
                "referenced cover does not exist as a regular package file");
        var charts = resolved.ToDictionary(item => item.Entry.Id, item => item.Chart,
            StringComparer.Ordinal);
        return EditorDocument.FromPackage(root, pack, charts);
    }

    [Obsolete("Use EditorProjectDraftFactory.Create with EditorProjectDraftRequest.")]
    public static EditorDocument CreateEmpty(string packId, string chartId, string title,
        string artist, string audioPath = "audio.wav")
    {
        var entry = new V2ChartEntry
        {
            Id = chartId,
            Difficulty = V2Difficulty.Hard,
            Unrated = true,
            Charters = ["Unknown"],
            File = $"charts/{chartId}.json",
        };
        var pack = new V2Pack
        {
            Id = packId,
            Revision = 1,
            Title = title,
            Artist = artist,
            Audio = audioPath,
            Charts = [entry],
        };
        var chart = new V2Chart
        {
            ChartId = chartId,
            AudioOffsetSec = 0,
            Bpms = [new V2BpmEvent(ExactBarTime.Zero, 150)],
            NotesLeft = [],
            NotesCenter = [],
            NotesRight = [],
        };
        return EditorDocument.FromPackage(null, pack,
            new Dictionary<string, V2Chart>(StringComparer.Ordinal) { [chartId] = chart });
    }

    private static bool IsRegularFile(string root, string relative)
    {
        try
        {
            _ = V2PackageWriter.ResolveRegularFile(root, relative);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
