using static EditorTestSupport;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

internal static class PackagePersistenceTests
{

    internal static void TestGoldenRoundTrip()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack");
        var meta = Path.Combine(root, "meta.json");
        var pack = V2JsonDecoder.DecodePackFile(meta);
        var charts = pack.Charts.ToDictionary(entry => entry.Id,
            entry => V2JsonDecoder.DecodeChartFile(Path.Combine(root, entry.File)), StringComparer.Ordinal);
        var document = EditorDocument.FromPackage(root, pack, charts);
        var encodedPack = V2JsonEncoder.EncodePack(document.BuildPackSnapshot());
        var first = V2JsonEncoder.EncodeChart(document.BuildChartSnapshot("hard"));
        var second = V2JsonEncoder.EncodeChart(document.BuildChartSnapshot("hard"));
        Check(first.SequenceEqual(second), "deterministic chart encoding");
        var decodedPack = V2JsonDecoder.DecodePack(encodedPack, "meta.json");
        var decodedChart = V2JsonDecoder.DecodeChart(first, "chart_hard.json");
        V2SemanticValidator.ValidatePack(decodedPack, "meta.json");
        V2SemanticValidator.ValidateChart(decodedChart, "chart_hard.json");
        var entry = decodedPack.Charts.Single(item => item.Id == "hard");
        var audio = File.ReadAllBytes(Path.Combine(root, entry.Audio ?? decodedPack.Audio!));
        var digest = V2GameplayDigest.Compute(decodedChart, entry, audio);
        var original = V2GameplayDigest.Compute(charts["hard"], pack.Charts.Single(item => item.Id == "hard"), audio);
        Check(digest.Sha256 == original.Sha256, "no-op encode keeps gameplay digest");
    }

    internal static void TestStrictPackageOpen()
    {
        var root = Path.Combine(Path.GetTempPath(), "dynamite-universe-editor-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), source);
            var document = EditorPackageRepository.Open(source);
            Check(document.Charts.Count > 0, "strict editor open accepts golden package");
            Check(!document.IsUnpersisted && !document.HasUnsavedChanges,
                "strict editor open is persisted and clean");

            var missingAudio = Path.Combine(root, "missing-audio");
            CopyDirectory(source, missingAudio);
            File.Delete(Path.Combine(missingAudio, "audio.wav"));
            ExpectDiagnostic(() => EditorPackageRepository.Open(missingAudio), "meta.json", "/audio",
                "strict editor open rejects missing audio");

            var missingCover = Path.Combine(root, "missing-cover");
            CopyDirectory(source, missingCover);
            var coverPack = V2JsonDecoder.DecodePackFile(Path.Combine(missingCover, "meta.json")) with
            {
                Cover = "missing-cover.png",
            };
            File.WriteAllBytes(Path.Combine(missingCover, "meta.json"), V2JsonEncoder.EncodePack(coverPack));
            ExpectDiagnostic(() => EditorPackageRepository.Open(missingCover), "meta.json", "/cover",
                "strict editor open rejects missing cover");

            var mismatchedChart = Path.Combine(root, "mismatched-chart");
            CopyDirectory(source, mismatchedChart);
            var chartPath = Path.Combine(mismatchedChart, "chart_hard.json");
            var chart = V2JsonDecoder.DecodeChartFile(chartPath) with { ChartId = "other" };
            File.WriteAllBytes(chartPath, V2JsonEncoder.EncodeChart(chart));
            ExpectDiagnostic(() => EditorPackageRepository.Open(mismatchedChart), "chart_hard.json", "/chartId",
                "strict editor open rejects chartId mismatch");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    internal static void TestPackageWriter()
    {
        var root = Path.Combine(Path.GetTempPath(), "dynamite-universe-editor-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), source);
            var document = LoadDocument(source);
            AddDirtyNote(document);

            var missingDestination = Path.Combine(root, "save-as-missing");
            V2PackageWriter.Write(BuildWriteRequest(document, source, missingDestination));
            Check(Directory.Exists(missingDestination) && File.Exists(Path.Combine(missingDestination, "meta.json")),
                "save as creates non-existing destination");
            Check(File.Exists(Path.Combine(missingDestination, "audio.wav")),
                "save as preserves package resources");

            var emptyDestination = Path.Combine(root, "save-as-empty");
            Directory.CreateDirectory(emptyDestination);
            V2PackageWriter.Write(BuildWriteRequest(document, source, emptyDestination));
            Check(File.Exists(Path.Combine(emptyDestination, "meta.json")) &&
                File.Exists(Path.Combine(emptyDestination, "chart_hard.json")),
                "save as publishes to allowed empty destination");

            var changedPathPack = document.BuildPackSnapshot() with
            {
                Charts = document.BuildPackSnapshot().Charts.Select(entry => entry.Id == "hard"
                    ? entry with { File = "charts/hard-renamed.json" }
                    : entry).ToArray(),
            };
            V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source,
                DestinationDirectory = source,
                Pack = changedPathPack,
                Charts = document.BuildAllChartSnapshots(),
            });
            Check(File.Exists(Path.Combine(source, "charts", "hard-renamed.json")) &&
                !File.Exists(Path.Combine(source, "chart_hard.json")),
                "save supports changed chart output path");

            var sourceMeta = File.ReadAllBytes(Path.Combine(source, "meta.json"));
            var sourceChart = File.ReadAllBytes(Path.Combine(source, "charts", "hard-renamed.json"));
            var failedDestination = Path.Combine(root, "save-as-failed");
            var missingCoverPack = changedPathPack with { Cover = "missing-cover.png" };
            ExpectDiagnostic(() => V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source,
                DestinationDirectory = failedDestination,
                Pack = missingCoverPack,
                Charts = document.BuildAllChartSnapshots(),
            }), "meta.json", "/cover", "missing cover diagnostic");
            Check(!Directory.Exists(failedDestination) &&
                sourceMeta.SequenceEqual(File.ReadAllBytes(Path.Combine(source, "meta.json"))) &&
                sourceChart.SequenceEqual(File.ReadAllBytes(Path.Combine(source, "charts", "hard-renamed.json"))) &&
                document.IsDirty,
                "failed save leaves JSON unchanged and document dirty");

            // A non-audio payload behind an audio extension must fail staging, not pass on extension.
            var nonWavePack = changedPathPack with { Audio = "audio.mp3" };
            File.Copy(Path.Combine(source, "audio.wav"), Path.Combine(source, "audio.mp3"));
            ExpectDiagnostic(() => V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source,
                DestinationDirectory = failedDestination,
                Pack = nonWavePack,
                Charts = document.BuildAllChartSnapshots(),
            }), "audio.mp3", "/", "WAV content behind an .mp3 name diagnostic");
            Check(!Directory.Exists(failedDestination) && document.IsDirty,
                "mismatched audio container leaves destination absent and document dirty");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    internal static void TestFirstPackageCreation()
    {
        WithExternalResources((root, audio, cover) =>
        {
            var draft = EditorProjectDraftFactory.Create(BuildDraftRequest(audio, cover));
            var emptyDestination = Path.Combine(root, "empty-chart-package");
            ExpectDiagnostic(() => V2PackageWriter.Create(BuildCreateRequest(draft, emptyDestination)),
                draft.ChartDestination, "/",
                "strict first create blocks an empty chart");
            Check(!Directory.Exists(emptyDestination) && !FindStagingDirectories(root).Any(),
                "empty strict create leaves no destination or staging");

            AddTap(draft.Document, "first-tap");
            var destination = Path.Combine(root, "created-package");
            var result = V2PackageWriter.Create(BuildCreateRequest(draft, destination));
            Check(result.Digests.Count == 1 && Directory.Exists(destination),
                "first create returns verified gameplay digest");
            Check(File.ReadAllBytes(audio).SequenceEqual(File.ReadAllBytes(
                      Path.Combine(destination, draft.AudioDestination))) &&
                  File.ReadAllBytes(cover).SequenceEqual(File.ReadAllBytes(
                      Path.Combine(destination, draft.CoverDestination!))),
                "first create imports external WAV and cover");
            var opened = EditorPackageRepository.Open(destination);
            Check(opened.SelectedChart.Notes.Count == 1 &&
                  opened.SelectedChart.Notes[0].Type == V2NoteType.Tap,
                "created package strictly reopens with authored Tap");
            draft.Document.MarkSaved(destination);
            Check(!draft.Document.IsUnpersisted && !draft.Document.HasUnsavedChanges,
                "MarkSaved cleans successfully created draft");

            var deterministicDestination = Path.Combine(root, "created-package-again");
            V2PackageWriter.Create(BuildCreateRequest(draft, deterministicDestination));
            Check(File.ReadAllBytes(Path.Combine(destination, "meta.json")).SequenceEqual(
                      File.ReadAllBytes(Path.Combine(deterministicDestination, "meta.json"))) &&
                  File.ReadAllBytes(Path.Combine(destination, draft.ChartDestination)).SequenceEqual(
                      File.ReadAllBytes(Path.Combine(deterministicDestination, draft.ChartDestination))),
                "first create JSON is deterministic");

            var allowedEmpty = Path.Combine(root, "allowed-empty");
            Directory.CreateDirectory(allowedEmpty);
            V2PackageWriter.Create(BuildCreateRequest(draft, allowedEmpty) with
            {
                AllowEmptyDestination = true,
            });
            Check(File.Exists(Path.Combine(allowedEmpty, "meta.json")) &&
                  !Directory.EnumerateFileSystemEntries(root, "allowed-empty.dynamite-universe-empty-*",
                      SearchOption.TopDirectoryOnly).Any(),
                "first create publishes to explicitly allowed empty destination");

            var nonempty = Path.Combine(root, "nonempty");
            Directory.CreateDirectory(nonempty);
            var sentinel = Path.Combine(nonempty, "keep.txt");
            File.WriteAllText(sentinel, "keep");
            ExpectException<IOException>(() => V2PackageWriter.Create(
                    BuildCreateRequest(draft, nonempty) with { AllowEmptyDestination = true }),
                "first create rejects nonempty destination");
            Check(File.ReadAllText(sentinel) == "keep" && !FindStagingDirectories(root).Any(),
                "nonempty destination failure preserves target and leaves no staging");

            var missingAudio = Path.Combine(root, "missing.wav");
            var missingResourceDestination = Path.Combine(root, "missing-resource");
            ExpectDiagnostic(() => V2PackageWriter.Create(BuildCreateRequest(
                    draft, missingResourceDestination,
                    [new V2ExternalResourceMapping(missingAudio, draft.AudioDestination),
                     new V2ExternalResourceMapping(cover, draft.CoverDestination!)])),
                "create-request", "/externalResources/0/externalSourcePath",
                "first create rejects missing external resource");
            Check(!Directory.Exists(missingResourceDestination) && !FindStagingDirectories(root).Any(),
                "missing external resource leaves no destination or staging");

            var missingMappingDestination = Path.Combine(root, "missing-mapping");
            ExpectDiagnostic(() => V2PackageWriter.Create(BuildCreateRequest(
                    draft, missingMappingDestination,
                    [new V2ExternalResourceMapping(audio, draft.AudioDestination)])),
                "create-request", "/externalResources",
                "first create requires referenced cover mapping");
            Check(!Directory.Exists(missingMappingDestination) && !FindStagingDirectories(root).Any(),
                "missing resource mapping leaves no destination or staging");

            var invalidWave = Path.Combine(root, "invalid.wav");
            File.WriteAllText(invalidWave, "not a wave");
            var invalidWaveDestination = Path.Combine(root, "invalid-wave");
            ExpectDiagnostic(() => V2PackageWriter.Create(BuildCreateRequest(
                    draft, invalidWaveDestination,
                    [new V2ExternalResourceMapping(invalidWave, draft.AudioDestination),
                     new V2ExternalResourceMapping(cover, draft.CoverDestination!)])),
                draft.AudioDestination, "/", "first create rejects invalid WAV");
            Check(!Directory.Exists(invalidWaveDestination) && !FindStagingDirectories(root).Any(),
                "invalid WAV failure rolls back staging and destination");
        });
    }

    internal static void TestEditorSaveSequence()
    {
        WithExternalResources((root, audio, _) =>
        {
            var draft = EditorProjectDraftFactory.Create(BuildDraftRequest(audio));
            var destination = Path.Combine(root, "editor-first-save");
            Directory.CreateDirectory(destination);
            ExpectDiagnostic(() => V2PackageWriter.Create(
                    draft.BuildPackageCreateRequest(destination, allowEmptyDestination: true)),
                draft.ChartDestination, "/", "editor first save reports an empty chart");
            Check(draft.Document.IsUnpersisted && draft.Document.HasUnsavedChanges &&
                  !Directory.EnumerateFileSystemEntries(destination).Any() &&
                  !FindStagingDirectories(root).Any(),
                "rejected first save keeps the draft dirty and the destination empty");

            AddTap(draft.Document, "editor-first-tap");
            var created = V2PackageWriter.Create(
                draft.BuildPackageCreateRequest(destination, allowEmptyDestination: true));
            draft.Document.MarkSaved(created.DestinationDirectory);
            Check(!draft.Document.IsUnpersisted && !draft.Document.HasUnsavedChanges &&
                  draft.Document.PackageDirectory == Path.GetFullPath(destination),
                "first save marks the draft persisted");

            AddDirtyNote(draft.Document);
            var written = V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = draft.Document.PackageDirectory!,
                DestinationDirectory = draft.Document.PackageDirectory!,
                Pack = draft.Document.BuildPackSnapshot(),
                Charts = draft.Document.BuildAllChartSnapshots(),
                ExternalResources = draft.Document.PendingResourceReplacements,
                AllowEmptyDestination = true,
            });
            draft.Document.MarkSaved(written.DestinationDirectory);
            Check(!draft.Document.HasUnsavedChanges && written.Digests.Count == 1,
                "same-directory save verifies the staged package and clears dirty state");

            var saveAs = Path.Combine(root, "editor-save-as");
            var copied = V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = draft.Document.PackageDirectory!,
                DestinationDirectory = saveAs,
                Pack = draft.Document.BuildPackSnapshot(),
                Charts = draft.Document.BuildAllChartSnapshots(),
                ExternalResources = draft.Document.PendingResourceReplacements,
                AllowEmptyDestination = true,
            });
            draft.Document.MarkSaved(copied.DestinationDirectory);
            var reopened = EditorPackageRepository.Open(saveAs);
            Check(reopened.PackageDirectory == Path.GetFullPath(saveAs) && !reopened.HasUnsavedChanges &&
                  reopened.SelectedChart.Notes.Count == 2 &&
                  reopened.SelectedChart.Notes.Any(note => note.Type == V2NoteType.Tap),
                "save as publishes a reopenable copy of the authored charts");
        });
    }
}
