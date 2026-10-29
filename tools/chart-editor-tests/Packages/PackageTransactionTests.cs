using static EditorTestSupport;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

internal static class PackageTransactionTests
{

    internal static void TestSameDirectoryResourceReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "dynamite-universe-editor-replace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), source);
            var document = LoadDocument(source);
            AddDirtyNote(document);
            var originalAudio = File.ReadAllBytes(Path.Combine(source, "audio.wav"));
            var replacementAudio = Path.Combine(root, "replacement.wav");
            File.WriteAllBytes(replacementAudio, MutateWave(originalAudio));

            V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source,
                DestinationDirectory = source,
                Pack = document.BuildPackSnapshot(),
                Charts = document.BuildAllChartSnapshots(),
                ExternalResources = [new V2ExternalResourceMapping(replacementAudio, "audio.wav")],
            });
            Check(!File.ReadAllBytes(Path.Combine(source, "audio.wav")).SequenceEqual(originalAudio),
                "same-directory save replaces an existing resource in place");

            var movedSource = Path.Combine(root, "moved-source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), movedSource);
            var movedDocument = LoadDocument(movedSource);
            AddDirtyNote(movedDocument);
            var movedAudio = Path.Combine(root, "moved.wav");
            File.WriteAllBytes(movedAudio, MutateWave(originalAudio));
            var movedPack = movedDocument.BuildPackSnapshot() with { Audio = "audio/moved.wav" };
            V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = movedSource,
                DestinationDirectory = movedSource,
                Pack = movedPack,
                Charts = movedDocument.BuildAllChartSnapshots(),
                ExternalResources = [new V2ExternalResourceMapping(movedAudio, "audio/moved.wav")],
            });
            Check(File.Exists(Path.Combine(movedSource, "audio", "moved.wav")) &&
                  !File.Exists(Path.Combine(movedSource, "audio.wav")),
                "same-directory save publishes a new resource path and removes the obsolete resource");
            var reopened = EditorPackageRepository.Open(movedSource);
            Check(reopened.SelectedChart.Notes.Count == movedDocument.SelectedChart.Notes.Count &&
                  reopened.SelectedChart.Notes.Any(note => note.Type == V2NoteType.Tap),
                "same-directory save with a new resource path strictly reopens");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    internal static void TestSameDirectoryPublicationRollback()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "dynamite-universe-editor-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), source);
            var document = LoadDocument(source);
            AddDirtyNote(document);
            var originalAudio = File.ReadAllBytes(Path.Combine(source, "audio.wav"));
            var originalMeta = File.ReadAllBytes(Path.Combine(source, "meta.json"));
            var originalChart = File.ReadAllBytes(Path.Combine(source, "chart_hard.json"));
            var replacementAudio = Path.Combine(root, "replacement.wav");
            File.WriteAllBytes(replacementAudio, MutateWave(originalAudio));
            var cover = Path.Combine(root, "cover.png");
            File.WriteAllBytes(cover, [137, 80, 78, 71, 13, 10, 26, 10]);
            var pack = document.BuildPackSnapshot() with { Cover = "zz-cover.png" };

            V2PackageWriter.PublicationInterceptor = relative =>
            {
                if (relative == "zz-cover.png")
                    throw new IOException("forced publication failure");
            };
            try
            {
                ExpectException<IOException>(() => V2PackageWriter.Write(new V2PackageWriteRequest
                {
                    SourceDirectory = source,
                    DestinationDirectory = source,
                    Pack = pack,
                    Charts = document.BuildAllChartSnapshots(),
                    ExternalResources =
                    [
                        new V2ExternalResourceMapping(replacementAudio, "audio.wav"),
                        new V2ExternalResourceMapping(cover, "zz-cover.png"),
                    ],
                }), "forced same-directory publication failure");
            }
            finally
            {
                V2PackageWriter.PublicationInterceptor = null;
            }

            Check(originalAudio.SequenceEqual(File.ReadAllBytes(Path.Combine(source, "audio.wav"))) &&
                  originalMeta.SequenceEqual(File.ReadAllBytes(Path.Combine(source, "meta.json"))) &&
                  originalChart.SequenceEqual(File.ReadAllBytes(Path.Combine(source, "chart_hard.json"))),
                "failed same-directory publication restores JSON and resources");
            Check(!FindStagingDirectories(root).Any(),
                "failed same-directory publication leaves no staging directory");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    internal static void TestSameDirectoryCleanupFailure()
    {
        // Windows refuses to delete a read-only backup; Unix file deletion ignores this bit.
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "dynamite-universe-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? protectedBackup = null;
        try
        {
            var source = Path.Combine(root, "source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), source);
            var document = LoadDocument(source);
            AddDirtyNote(document);
            var replacementAudio = Path.Combine(root, "replacement.wav");
            var expectedAudio = MutateWave(File.ReadAllBytes(Path.Combine(source, "audio.wav")));
            File.WriteAllBytes(replacementAudio, expectedAudio);
            var pack = document.BuildPackSnapshot() with { Title = "Saved before cleanup failed" };
            var charts = document.BuildAllChartSnapshots();
            V2PackageWriter.PublicationInterceptor = relative =>
            {
                if (relative != "meta.json") return;
                protectedBackup = Directory.EnumerateFiles(source,
                    "chart_hard.json.dynamite-universe-backup-*").Single();
                File.SetAttributes(protectedBackup, FileAttributes.ReadOnly);
            };
            ExpectException<AggregateException>(() => V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source, DestinationDirectory = source, Pack = pack, Charts = charts,
                ExternalResources = [new V2ExternalResourceMapping(replacementAudio, "audio.wav")],
            }), "backup cleanup failure is reported after publication");
            Check(File.ReadAllBytes(Path.Combine(source, "audio.wav")).SequenceEqual(expectedAudio) &&
                  File.ReadAllBytes(Path.Combine(source, "meta.json")).SequenceEqual(V2JsonEncoder.EncodePack(pack)) &&
                  pack.Charts.All(entry => File.ReadAllBytes(Path.Combine(source, entry.File))
                      .SequenceEqual(V2JsonEncoder.EncodeChart(charts[entry.Id]))),
                "cleanup failure preserves all published JSON and resource bytes");
            Check(EditorPackageRepository.Open(source).Title == pack.Title,
                "published package strictly reopens despite a leftover backup");
            Check(!FindStagingDirectories(root).Any(),
                "cleanup continues with staging after a backup delete fails");
        }
        finally
        {
            V2PackageWriter.PublicationInterceptor = null;
            if (protectedBackup is not null && File.Exists(protectedBackup))
                File.SetAttributes(protectedBackup, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    internal static void TestIncompleteRollbackContinues()
    {
        var root = Path.Combine(Path.GetTempPath(), "du-incomplete-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack"), source);
            var document = LoadDocument(source);
            AddDirtyNote(document);
            var audioPath = Path.Combine(source, "audio.wav");
            var originalAudio = File.ReadAllBytes(audioPath);
            var chartPath = Path.Combine(source, "chart_hard.json");
            var originalChart = File.ReadAllBytes(chartPath);
            var originalMeta = File.ReadAllBytes(Path.Combine(source, "meta.json"));
            var publicationFailure = new IOException("forced publication failure before meta");
            V2PackageWriter.PublicationInterceptor = relative =>
            {
                if (relative != "meta.json") return;
                // A directory at a published file path prevents both deletion and restoration.
                File.Delete(chartPath);
                Directory.CreateDirectory(chartPath);
                throw publicationFailure;
            };
            try
            {
                V2PackageWriter.Write(BuildWriteRequest(document, source, source));
                Check(false, "incomplete rollback must report failure");
            }
            catch (AggregateException exception)
            {
                Check(ReferenceEquals(exception.InnerExceptions[0], publicationFailure) &&
                      exception.InnerExceptions.Count >= 2,
                    "incomplete rollback retains publication and rollback errors");
            }
            Check(File.ReadAllBytes(audioPath).SequenceEqual(originalAudio) &&
                  File.ReadAllBytes(Path.Combine(source, "meta.json")).SequenceEqual(originalMeta),
                "rollback continues restoring other files after one restore fails");
            var backup = Directory.GetFiles(source, "chart_hard.json.dynamite-universe-backup-*").Single();
            Check(File.ReadAllBytes(backup).SequenceEqual(originalChart),
                "unrestored chart backup remains available for recovery");
        }
        finally
        {
            V2PackageWriter.PublicationInterceptor = null;
            Directory.Delete(root, recursive: true);
        }
    }

    internal static byte[] MutateWave(byte[] original)
    {
        var mutated = (byte[])original.Clone();
        mutated[^1] ^= 0x5A;
        return mutated;
    }
}
