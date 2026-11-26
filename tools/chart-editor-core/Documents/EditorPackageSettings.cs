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
        var media = new List<IEditorCommand>();
        var history = new List<IEditorCommand>();

        // Disabling a chart audio override must win over a staged replacement source: the dialog
        // can carry a browsed source while the checkbox is unchecked, and the user asked to
        // inherit the package audio, not to install another override.
        var clearAudio = request.ClearChartAudio && chart.Audio is not null;
        var clearPreview = request.ClearChartPreview && chart.Preview is not null;

        var reserved = new List<string>();
        if (request.PackAudioSource is { } packAudio)
        {
            var slot = EditorMediaSlots.AudioDestination(document, null, packAudio);
            media.Add(new ReplaceChartResourceCommand(originalChartId, true, false, slot, packAudio));
            reserved.Add(slot);
        }
        if (request.PackCoverSource is { } packCover)
        {
            var slot = document.Cover ?? $"cover{Path.GetExtension(packCover).ToLowerInvariant()}";
            media.Add(new ReplaceChartResourceCommand(originalChartId, true, true, slot, packCover));
            reserved.Add(slot);
        }
        if (!request.ClearChartAudio && request.ChartAudioSource is { } chartAudio)
            media.Add(new ReplaceChartResourceCommand(originalChartId, false, false,
                EditorMediaSlots.AudioDestination(document, chart, chartAudio, reserved: reserved), chartAudio));

        if (clearAudio)
            media.Add(new ClearChartOverridesCommand(originalChartId, clearAudio: true, clearPreview: false));
        if (clearPreview)
            history.Add(new ClearChartOverridesCommand(originalChartId, clearAudio: false, clearPreview: true));

        if (document.PackId != request.PackId || document.Title != request.Title || document.Artist != request.Artist)
            history.Add(new EditPackMetadataCommand(request.PackId, request.Title, request.Artist));
        if (request.PackPreview is { } packPreview && document.Preview != packPreview)
            history.Add(new EditPreviewCommand(true, null, packPreview.StartSec, packPreview.DurationSec));

        var level = request.Unrated ? null : request.Level;
        var difficultyKey = request.Difficulty == V2Difficulty.Custom ? request.DifficultyKey : null;
        if (chart.Difficulty != request.Difficulty || chart.DifficultyKey != difficultyKey ||
            chart.Level != level || chart.Unrated != request.Unrated || !chart.Charters.SequenceEqual(request.Charters))
            history.Add(new EditChartDetailsCommand(originalChartId, request.Difficulty, difficultyKey,
                level, request.Unrated, request.Charters));
        if (chart.AudioOffsetSec != request.AudioOffsetSec)
            history.Add(new EditChartOffsetCommand(request.AudioOffsetSec));
        if (!request.ClearChartPreview && request.ChartPreview is { } chartPreview && chart.Preview != chartPreview)
            history.Add(new EditPreviewCommand(false, originalChartId, chartPreview.StartSec, chartPreview.DurationSec));
        if (chart.Id != request.ChartId || chart.File != request.ChartFile)
            history.Add(new RenameChartCommand(originalChartId, request.ChartId, request.ChartFile));

        if (media.Count == 0 && history.Count == 0) return false;
        document.ApplyPackageSettings(Batch(media), Batch(history));
        return true;
    }

    private static IEditorCommand? Batch(List<IEditorCommand> commands) => commands.Count switch
    {
        0 => null,
        1 => commands[0],
        _ => new CompositeEditorCommand(commands.ToArray()),
    };
}
