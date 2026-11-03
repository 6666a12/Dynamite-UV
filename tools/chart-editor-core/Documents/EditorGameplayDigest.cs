using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public static class EditorGameplayDigest
{
    public static V2GameplayDigestResult Compute(EditorDocument document, string audioSha256Hex)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioSha256Hex);
        var entry = document.BuildPackSnapshot().Charts.Single(item => item.Id == document.SelectedChartId);
        return V2GameplayDigest.Compute(
            document.BuildChartSnapshot(document.SelectedChartId), entry.JudgePreset, audioSha256Hex);
    }
}
