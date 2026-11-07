using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <param name="PackPreview">Null leaves the package preview unchanged (including when it is absent).</param>
public sealed record EditorPackageSettingsRequest(
    string PackId,
    string Title,
    string Artist,
    string ChartId,
    string ChartFile,
    V2Difficulty Difficulty,
    string? DifficultyKey,
    int? Level,
    bool Unrated,
    IReadOnlyList<string> Charters,
    double AudioOffsetSec,
    V2Preview? PackPreview,
    V2Preview? ChartPreview,
    string? PackAudioSource,
    string? PackCoverSource,
    string? ChartAudioSource,
    bool ClearChartAudio,
    bool ClearChartPreview);

public static class EditorPackageSettings
{
    public static bool Apply(EditorDocument document, string originalChartId, EditorPackageSettingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        var chart = document.Charts.SingleOrDefault(item => item.Id == originalChartId)
            ?? throw new KeyNotFoundException($"Chart '{originalChartId}' is not open.");
        if (document.SelectedChartId != originalChartId) document.SelectChart(originalChartId);
        var commands = new List<IEditorCommand>();

        // Disabling a chart audio override must win over a staged replacement source: the dialog
        // can carry a browsed source while the checkbox is unchecked, and the user asked to
        // inherit the package audio, not to install another override.
        var clearAudio = request.ClearChartAudio && chart.Audio is not null;
        var clearPreview = request.ClearChartPreview && chart.Preview is not null;

        if (request.PackAudioSource is { } packAudio)
            commands.Add(new ReplaceChartResourceCommand(originalChartId, true, false,
                document.Audio is { } packAudioPath
                    ? ReplaceResourceExtension(packAudioPath, packAudio)
                    : "audio" + Path.GetExtension(packAudio).ToLowerInvariant(),
                packAudio));
        if (request.PackCoverSource is { } packCover)
            commands.Add(new ReplaceChartResourceCommand(originalChartId, true, true,
                document.Cover ?? $"cover{Path.GetExtension(packCover).ToLowerInvariant()}", packCover));
        if (!request.ClearChartAudio && request.ChartAudioSource is { } chartAudio)
            commands.Add(new ReplaceChartResourceCommand(originalChartId, false, false,
                chart.Audio is { } chartAudioPath
                    ? ReplaceResourceExtension(chartAudioPath, chartAudio)
                    : $"audio/{originalChartId}{Path.GetExtension(chartAudio).ToLowerInvariant()}",
                chartAudio));

        if (clearAudio || clearPreview)
            commands.Add(new ClearChartOverridesCommand(originalChartId, clearAudio, clearPreview));

        if (document.PackId != request.PackId || document.Title != request.Title || document.Artist != request.Artist)
            commands.Add(new EditPackMetadataCommand(request.PackId, request.Title, request.Artist));
        if (request.PackPreview is { } packPreview && document.Preview != packPreview)
            commands.Add(new EditPreviewCommand(true, null, packPreview.StartSec, packPreview.DurationSec));

        var level = request.Unrated ? null : request.Level;
        var difficultyKey = request.Difficulty == V2Difficulty.Custom ? request.DifficultyKey : null;
        if (chart.Difficulty != request.Difficulty || chart.DifficultyKey != difficultyKey ||
            chart.Level != level || chart.Unrated != request.Unrated || !chart.Charters.SequenceEqual(request.Charters))
            commands.Add(new EditChartDetailsCommand(originalChartId, request.Difficulty, difficultyKey,
                level, request.Unrated, request.Charters));
        if (chart.AudioOffsetSec != request.AudioOffsetSec)
            commands.Add(new EditChartOffsetCommand(request.AudioOffsetSec));
        if (!request.ClearChartPreview && request.ChartPreview is { } chartPreview && chart.Preview != chartPreview)
            commands.Add(new EditPreviewCommand(false, originalChartId, chartPreview.StartSec, chartPreview.DurationSec));
        if (chart.Id != request.ChartId || chart.File != request.ChartFile)
            commands.Add(new RenameChartCommand(originalChartId, request.ChartId, request.ChartFile));

        if (commands.Count == 0) return false;
        document.Execute(commands.Count == 1 ? commands[0] : new CompositeEditorCommand(commands.ToArray()));
        return true;
    }

    /// <summary>
    /// A replacement keeps the authored package-relative slot, but the staged extension must follow
    /// the external source: the writer's audio probe selects its parser from the package-relative
    /// extension, so staging OGG bytes at a .wav path would pass the preview yet fail the next save.
    /// </summary>
    private static string ReplaceResourceExtension(string packageRelativePath, string externalSourcePath)
    {
        var extension = Path.GetExtension(externalSourcePath).ToLowerInvariant();
        if (string.Equals(Path.GetExtension(packageRelativePath), extension, StringComparison.OrdinalIgnoreCase))
            return packageRelativePath;
        var normalized = packageRelativePath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var directory = slash < 0 ? "" : normalized[..(slash + 1)];
        return directory + Path.GetFileNameWithoutExtension(normalized) + extension;
    }
}
