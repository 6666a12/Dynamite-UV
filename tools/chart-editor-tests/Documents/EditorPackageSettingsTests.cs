using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class EditorPackageSettingsTests
{
    internal static void Run()
    {
        ClearWinsOverReplacementSource();
        ReplacementExtensionFollowsSource();
        NullPackPreviewIsPreserved();
        DraftFirstSavePublishesPendingReplacements();
        FormatChangeSavesAndReopens();
    }

    private static EditorDocument CreateSavedPackage(string root, string audio, string cover)
    {
        var session = new EditorDocumentSession();
        var document = session.CreateDraft(BuildDraftRequest(audio, cover));
        AddTap(document, "tap-1");
        var saved = session.Save(Path.Combine(root, "settings-pack"));
        return EditorPackageRepository.Open(saved.DestinationDirectory);
    }

    private static EditorPackageSettingsRequest BaseRequest(EditorDocument document)
    {
        var chart = document.SelectedChart;
        return new(document.PackId, document.Title, document.Artist, chart.Id, chart.File,
            chart.Difficulty, chart.DifficultyKey, chart.Level, chart.Unrated,
            chart.Charters.ToArray(), chart.AudioOffsetSec,
            document.Preview, chart.Preview, null, null, null, false, false);
    }

    private static void ClearWinsOverReplacementSource()
    {
        WithExternalResources((root, audio, cover) =>
        {
            var document = CreateSavedPackage(root, audio, cover);
            var chartId = document.SelectedChartId;
            var ignored = EditorPackageSettings.Apply(document, chartId,
                BaseRequest(document) with { ChartAudioSource = audio, ClearChartAudio = true });
            Check(!ignored && document.SelectedChart.Audio is null,
                "clear also suppresses a browsed source when no override existed");
            EditorPackageSettings.Apply(document, chartId,
                BaseRequest(document) with { ChartAudioSource = audio });
            Check(document.SelectedChart.Audio == $"audio/{chartId}.wav",
                "chart audio override stages the derived audio path");

            var ogg = Path.Combine(root, "override.ogg");
            File.WriteAllBytes(ogg, [1, 2, 3]);
            EditorPackageSettings.Apply(document, chartId,
                BaseRequest(document) with { ChartAudioSource = ogg, ClearChartAudio = true });
            Check(document.SelectedChart.Audio is null && document.Audio == "audio.wav",
                "clearing a chart audio override wins over the attached replacement source");
        });
    }

    private static void ReplacementExtensionFollowsSource()
    {
        WithExternalResources((root, audio, cover) =>
        {
            var document = CreateSavedPackage(root, audio, cover);
            var chartId = document.SelectedChartId;
            Check(document.Audio == "audio.wav", "saved package keeps the authored audio path");

            var packOgg = Path.Combine(root, "pack.ogg");
            File.WriteAllBytes(packOgg, [1, 2, 3]);
            EditorPackageSettings.Apply(document, chartId,
                BaseRequest(document) with { PackAudioSource = packOgg });
            Check(document.Audio == "audio.ogg" &&
                  document.PendingResourceReplacements.Single(item => item.PackageRelativePath == "audio.ogg")
                      .ExternalSourcePath == Path.GetFullPath(packOgg),
                "pack audio replacement derives the destination extension from the new source");

            EditorPackageSettings.Apply(document, chartId,
                BaseRequest(document) with { ChartAudioSource = audio });
            Check(document.SelectedChart.Audio == $"audio/{chartId}.wav",
                "same-format chart audio replacement keeps the authored path");

            var chartOgg = Path.Combine(root, "chart.ogg");
            File.WriteAllBytes(chartOgg, [1, 2, 3]);
            EditorPackageSettings.Apply(document, chartId,
                BaseRequest(document) with { ChartAudioSource = chartOgg });
            Check(document.SelectedChart.Audio == $"audio/{chartId}.ogg",
                "chart audio replacement swaps the extension in place");
        });
    }

    private static void NullPackPreviewIsPreserved()
    {
        WithExternalResources((root, audio, cover) =>
        {
            var document = CreateSavedPackage(root, audio, cover);
            Check(document.Preview is null, "saved draft package has no authored preview metadata");
            var changed = EditorPackageSettings.Apply(document, document.SelectedChartId,
                BaseRequest(document) with { PackPreview = null });
            Check(!changed && document.Preview is null,
                "an apply that leaves the display preview fallback untouched fabricates no metadata");

            EditorPackageSettings.Apply(document, document.SelectedChartId,
                BaseRequest(document) with { PackPreview = new V2Preview(1, 8) });
            Check(document.Preview == new V2Preview(1, 8),
                "an explicitly edited pack preview is still applied");
        });
    }

    private static void FormatChangeSavesAndReopens()
    {
        WithExternalResources((root, audio, cover) =>
        {
            var session = new EditorDocumentSession();
            var document = session.CreateDraft(BuildDraftRequest(audio, cover));
            AddTap(document, "tap-format");
            var flac = Path.Combine(root, "replacement.flac");
            var bytes = AudioProbeFixtures.Flac(40);
            File.WriteAllBytes(flac, bytes);
            EditorPackageSettings.Apply(document, document.SelectedChartId,
                BaseRequest(document) with { PackAudioSource = flac, ChartAudioSource = flac });
            var saved = session.Save(Path.Combine(root, "changed-format"));
            var reopened = EditorPackageRepository.Open(saved.DestinationDirectory);
            Check(reopened.Audio == "audio.flac" && reopened.SelectedChart.Audio!.EndsWith(".flac") &&
                  File.ReadAllBytes(Path.Combine(saved.DestinationDirectory, "audio.flac")).SequenceEqual(bytes) &&
                  !File.Exists(Path.Combine(saved.DestinationDirectory, "audio.wav")),
                "format-changing draft replacements pass the real probe and reopen without stale WAV media");
        });
    }

    private static void DraftFirstSavePublishesPendingReplacements()
    {
        WithExternalResources((root, audio, cover) =>
        {
            var session = new EditorDocumentSession();
            var document = session.CreateDraft(BuildDraftRequest(audio, cover));
            AddTap(document, "tap-1");
            var chartId = document.SelectedChartId;

            var replacementBytes = File.ReadAllBytes(audio);
            replacementBytes[^1] ^= 0xFF;
            var replacement = Path.Combine(root, "replacement.wav");
            File.WriteAllBytes(replacement, replacementBytes);

            EditorPackageSettings.Apply(document, chartId,
                BaseRequest(document) with { PackAudioSource = replacement, ChartAudioSource = audio });
            Check(document.Audio == "audio.wav" && document.SelectedChart.Audio == $"audio/{chartId}.wav",
                "draft resource changes stage pending replacements before the first save");

            var saved = session.Save(Path.Combine(root, "draft-pack"));
            Check(File.ReadAllBytes(Path.Combine(saved.DestinationDirectory, "audio.wav"))
                      .SequenceEqual(replacementBytes),
                "first save publishes the replaced package audio instead of the draft original");
            Check(File.Exists(Path.Combine(saved.DestinationDirectory, "audio", $"{chartId}.wav")),
                "first save publishes a chart audio override added before persistence");
            Check(File.Exists(Path.Combine(saved.DestinationDirectory, "cover.png")),
                "first save still publishes the untouched draft cover");
            var reopened = EditorPackageRepository.Open(saved.DestinationDirectory);
            Check(reopened.Audio == "audio.wav" && reopened.Charts.Single().Audio == $"audio/{chartId}.wav",
                "reopened package records the pending-replacement media references");
        });
    }
}
