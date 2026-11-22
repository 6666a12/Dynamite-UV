using System.Security.Cryptography;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class EditorExportSnapshotTests
{
    internal static void Run()
    {
        var root = Directory.CreateTempSubdirectory("dynamaker-export-snapshot-").FullName;
        try
        {
            var external = Path.Combine(root, "外部素材");
            var session = PersistenceFixture.Create(external);
            var document = session.Document!;
            document.SelectChart("mega");
            var draft = session.Draft;
            var draftVersion = document.ChangeVersion;
            var draftOutput = Path.Combine(root, "draft export");
            var fromDraft = session.WriteExportSnapshotAsync(draftOutput).GetAwaiter().GetResult();
            Check(fromDraft.ChartId == "mega" && fromDraft.PreviewSeconds == 10 && fromDraft.AudioSeconds == 120 &&
                ReferenceEquals(draft, session.Draft) && document.IsUnpersisted && document.HasUnsavedChanges &&
                document.ChangeVersion == draftVersion, "video snapshot supports an unsaved draft without adopting it as a package");
            CheckSnapshot(draftOutput, "audio/mega.wav", 3, 10,
                Path.Combine(external, "独立差分.wav"), V2JsonEncoder.EncodeChart(document.BuildChartSnapshot("mega")));
            VerifyPublication(root, draftOutput);

            var source = Path.Combine(root, "saved source");
            session.Save(source);
            var originalFiles = TreeHashes(source);
            var pendingWave = Path.Combine(root, "new unsaved.wav");
            PersistenceFixture.WriteWave(pendingWave, 410);
            document.Execute(new ReplaceChartResourceCommand("mega", false, false, "audio/unsaved.wav", pendingWave));
            document.Execute(new EditPreviewCommand(false, "mega", 7.125, 6.5));
            document.Execute(new AddNoteCommand(new EditableNote
            {
                Id = "unsaved-mega", Type = V2NoteType.Tap, Track = EditorTrack.Center,
                Time = ExactBarTime.One, Center = 2.5, Width = 1,
            }));
            var selectedChart = V2JsonEncoder.EncodeChart(document.BuildChartSnapshot("mega"));
            var versionBeforeExport = document.ChangeVersion;
            var output = Path.Combine(root, "current difficulty only");
            var written = session.WriteExportSnapshotAsync(output).GetAwaiter().GetResult();
            Check(written.Directory == output && written.ChartId == "mega" && written.AudioSeconds == 120 && written.PreviewSeconds == 6.5,
                "video snapshot returns the resolved difficulty and source durations");
            CheckSnapshot(output, "audio/unsaved.wav", 7.125, 6.5, pendingWave, selectedChart);
            Check(document.SelectedChartId == "mega" && document.ChangeVersion == versionBeforeExport &&
                document.HasUnsavedChanges && document.CanUndo && document.PendingResourceReplacements.Count == 1 &&
                ReferenceEquals(document, session.Document) && document.PackageDirectory == source && session.Draft is null,
                "video export leaves the live document, history, dirty state and pending media unchanged");
            Check(originalFiles.SequenceEqual(TreeHashes(source)), "video export preserves every original package file byte-for-byte");

            var snapshotBeforeRetry = TreeHashes(output);
            ExpectException<IOException>(() => session.WriteExportSnapshotAsync(output).GetAwaiter().GetResult(),
                "video snapshot refuses to replace an existing snapshot destination");
            Check(snapshotBeforeRetry.SequenceEqual(TreeHashes(output)), "existing export snapshot is byte-identical after rejection");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var canceledOutput = Path.Combine(root, "canceled");
            ExpectException<OperationCanceledException>(() => session.WriteExportSnapshotAsync(canceledOutput, cancellation.Token).GetAwaiter().GetResult(),
                "pre-canceled video snapshot cannot create a destination");
            Check(!Directory.Exists(canceledOutput) && originalFiles.SequenceEqual(TreeHashes(source)),
                "canceled video snapshot does not leave files or change the source package");
            VerifyWriteBoundaries(root, session, pendingWave);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void VerifyWriteBoundaries(string root, EditorDocumentSession session, string source)
    {
        var canceled = Path.Combine(root, "canceled during copy");
        using (var cancellation = new CancellationTokenSource())
        {
            ExpectException<OperationCanceledException>(() => session.WriteExportSnapshotAsync(canceled, cancellation.Token,
                    _ => cancellation.Cancel()).GetAwaiter().GetResult(), "cancel during bounded media copy rolls back the snapshot");
        }
        var space = Path.Combine(root, "insufficient disk");
        ExpectException<IOException>(() => session.WriteExportSnapshotAsync(space, beforeMediaWrite: _ => throw new IOException("disk"))
            .GetAwaiter().GetResult(), "snapshot stops on host disk guard before writing a media chunk");
        Check(!Directory.Exists(canceled) && !Directory.Exists(space) && !Directory.EnumerateDirectories(root, "*.dynamite-universe-staging-*").Any(),
            "interrupted snapshot writes remove private staging and do not publish");

        if (!OperatingSystem.IsWindows()) return;
        var attrs = File.GetAttributes(source);
        try
        {
            File.SetAttributes(source, attrs | FileAttributes.ReadOnly);
            var output = Path.Combine(root, "read-only source");
            session.WriteExportSnapshotAsync(output).GetAwaiter().GetResult();
            Check((File.GetAttributes(Path.Combine(output, "audio/unsaved.wav")) & FileAttributes.ReadOnly) == 0 &&
                (File.GetAttributes(source) & FileAttributes.ReadOnly) != 0, "snapshot owns writable copies without changing read-only originals");
        }
        finally { File.SetAttributes(source, attrs); }
    }

    private static void VerifyPublication(string root, string source)
    {
        var duringProbe = Path.Combine(root, "canceled during probe");
        using var cancellation = new CancellationTokenSource();
        var pack = V2JsonDecoder.DecodePackFile(Path.Combine(source, "meta.json"));
        var entry = pack.Charts.Single();
        var request = new V2PackageCreateRequest
        {
            DestinationDirectory = duringProbe, Pack = pack,
            Charts = new Dictionary<string, V2Chart> { [entry.Id] = V2JsonDecoder.DecodeChartFile(Path.Combine(source, entry.File)) },
            ExternalResources = new[] { pack.Audio!, pack.Cover! }.Select(path => new V2ExternalResourceMapping(Path.Combine(source, path), path)).ToArray(),
        };
        ExpectException<OperationCanceledException>(() => V2PackageWriter.Create(request with
        {
            AudioProbe = new HookAudioProbe(cancellation.Cancel),
        }, cancellation.Token), "cancellation during the audio probe is checked before publication");
        Check(!Directory.Exists(duringProbe), "audio probe cancellation never publishes the destination");

        var competing = Path.Combine(root, "competing destination");
        ExpectException<IOException>(() => V2PackageWriter.Create(request with
        {
            DestinationDirectory = competing,
            AudioProbe = new HookAudioProbe(() => Directory.CreateDirectory(competing)),
        }), "a destination created during verification is not replaced");
        Check(Directory.Exists(competing) && !Directory.EnumerateFileSystemEntries(competing).Any(), "competing empty directory remains untouched");
    }

    private sealed class HookAudioProbe(Action act) : IV2PackageAudioProbe
    {
        public V2PackageAudioInfo Probe(string absolutePath, string packageRelativePath)
        {
            act();
            return V2BuiltInAudioProbe.Instance.Probe(absolutePath, packageRelativePath);
        }
    }

    private static void CheckSnapshot(string root, string audio, double previewStart, double previewDuration,
        string sourceAudio, byte[] expectedChart)
    {
        var pack = V2JsonDecoder.DecodePackFile(Path.Combine(root, "meta.json"));
        var entry = pack.Charts.Single();
        Check(entry.Id == "mega" && entry.Audio is null && entry.Preview is null && pack.Audio == audio &&
            pack.Preview?.StartSec == previewStart && pack.Preview.DurationSec == previewDuration && pack.Cover is not null,
            "video snapshot flattens the current difficulty's resolved audio and preview into a one-chart package");
        var reopened = new EditorDocumentSession().Open(root);
        Check(expectedChart.AsSpan().SequenceEqual(V2JsonEncoder.EncodeChart(reopened.BuildChartSnapshot(entry.Id))),
            "strict snapshot reopens with every unsaved selected-chart edit");
        var expectedFiles = new[] { "meta.json", entry.File, audio, pack.Cover! }.Order(StringComparer.Ordinal);
        Check(TreeHashes(root).Keys.SequenceEqual(expectedFiles) && Hash(Path.Combine(root, audio)) == Hash(sourceAudio),
            "snapshot contains only metadata, the selected chart, its exact audio bytes and cover");
    }

    private static SortedDictionary<string, string> TreeHashes(string root)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            hashes.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), Hash(path));
        return hashes;
    }

    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
