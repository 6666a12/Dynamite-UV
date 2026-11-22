using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public sealed record EditorExportSnapshot(string Directory, string ChartId, double AudioSeconds, double PreviewSeconds)
{
    internal static Task<EditorExportSnapshot> WriteAsync(EditorDocument document, EditorProjectDraft? draft,
        string destination, CancellationToken cancellationToken, Action<int>? beforeMediaWrite)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var chartId = document.SelectedChartId;
        var chart = document.BuildChartSnapshot(chartId);
        var original = document.BuildPackSnapshot();
        var entry = original.Charts.Single(item => item.Id == chartId);
        var audio = original.ResolveAudio(entry);
        var preview = original.ResolvePreview(entry);
        var pack = original with
        {
            FormatMinor = chart.FormatMinor,
            Audio = audio,
            Preview = preview,
            Charts = [entry with { Audio = null, Preview = null }],
        };
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (draft is not null)
            foreach (var item in draft.ExternalResources) resources[item.Key] = item.Value;
        foreach (var item in document.PendingResourceReplacements)
            resources[item.PackageRelativePath] = item.ExternalSourcePath;
        var root = document.PackageDirectory;
        return Task.Run(() =>
        {
            var mappings = new[] { audio, pack.Cover }.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new V2ExternalResourceMapping(resources.TryGetValue(path, out var source) ? source :
                    V2PackageWriter.ResolveRegularFile(root ?? throw new IOException($"Missing resource: {path}"), path), path)).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            var probe = new CaptureAudioProbe(draft?.AudioProbe ?? V2BuiltInAudioProbe.Instance);
            var written = V2PackageWriter.Create(new()
            {
                DestinationDirectory = destination, Pack = pack,
                Charts = new Dictionary<string, V2Chart>(StringComparer.Ordinal) { [chartId] = chart },
                ExternalResources = mappings, AudioProbe = probe,
            }, cancellationToken, beforeMediaWrite);
            return new EditorExportSnapshot(written.DestinationDirectory, chartId, probe.Seconds, preview?.DurationSec ?? 0);
        }, cancellationToken);
    }

    private sealed class CaptureAudioProbe(IV2PackageAudioProbe decoder) : IV2PackageAudioProbe
    {
        public double Seconds { get; private set; }
        public V2PackageAudioInfo Probe(string absolutePath, string packageRelativePath)
        {
            var result = decoder.Probe(absolutePath, packageRelativePath);
            Seconds = result.DurationSeconds;
            return result;
        }
    }
}
