using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

/// <summary>Disk and fresh-process lifecycle, driven through the same session/commands as Shell.</summary>
internal static class PackageLifecycleTests
{
    internal sealed record Snapshot(string Pack, Dictionary<string, string> Charts,
        Dictionary<string, string> Resources, Dictionary<string, string> Digests);

    internal static void Run(string? retainedRoot = null)
    {
        var root = retainedRoot is null
            ? Path.Combine(Path.GetTempPath(), "editor-persistence-" + Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(retainedRoot);
        if (Directory.Exists(root)) throw new IOException("Test output must be a new directory: " + root);
        Directory.CreateDirectory(root);
        try
        {
            var sources = Path.Combine(root, "外部原始资源");
            var session = PersistenceFixture.Create(sources);
            var document = session.Document!;
            Check(document.Charts.Count == 3 && document.Charts.All(c => c.Notes.Count == 252),
                "lifecycle fixture: three charts, 756 parents, all types on all tracks");
            var original = Path.Combine(root, "首次保存 Original");
            var expected = Capture(session);
            Directory.CreateDirectory(original);
            session.Save(original + Path.DirectorySeparatorChar);
            Check(document.PackageDirectory == Path.GetFullPath(original),
                "folder-picker first save normalizes trailing separator");
            AssertSame(expected, Capture(session), "first save");
            Check(!document.HasUnsavedChanges && session.Draft is null, "first save clears dirty and draft");
            var manifest = Path.Combine(root, "expected.json");
            VerifyInFreshProcess(original, expected, manifest);

            // Make all external sources inaccessible: subsequent operations must use package media.
            Directory.Move(sources, sources + "-unavailable");
            session = new EditorDocumentSession();
            document = session.Open(original + Path.DirectorySeparatorChar);
            AssertSame(expected, Capture(session), "reopen without original media");
            var diskBefore = FileHashes(original);
            document.SelectChart("hard");
            var tap = document.SelectedChart.Notes.First(n => n.Type == V2NoteType.Tap);
            document.Execute(new MoveNoteCommand(tap.Id, ExactBarTime.FromFraction(7, 15), .6, .85));
            document.Execute(new SetNoteScrollSpeedMultiplierCommand(tap.Id, 1.7));
            var edited = Capture(session);
            Check(edited.Digests["hard"] != expected.Digests["hard"] &&
                edited.Digests["normal"] == expected.Digests["normal"] &&
                edited.Digests["mega"] == expected.Digests["mega"], "only edited chart digest changes");
            document.Undo(); document.Undo();
            AssertSame(expected, Capture(session), "undo to saved state");
            Check(!document.HasUnsavedChanges, "undo to saved state clears dirty");
            document.Redo(); document.Redo();
            AssertSame(edited, Capture(session), "redo restores exact edits");

            // Windows sharing violation before publication; existing bytes and dirty edits survive.
            if (OperatingSystem.IsWindows())
            {
                using (var locked = new FileStream(Path.Combine(original, "meta.json"), FileMode.Open,
                           FileAccess.Read, FileShare.None))
                    ExpectException<IOException>(() => session.Save(original), "locked package rejects save");
                Check(document.HasUnsavedChanges, "failed save retains dirty document");
                CheckHashes(diskBefore, FileHashes(original), "failed save preserves every original file");
                VerifyInFreshProcess(original, expected, manifest);
            }
            session.Save(original);
            VerifyInFreshProcess(original, edited, manifest);

            // Metadata-only changes must persist without affecting gameplay identity.
            document.Execute(new EditPackMetadataCommand(document.PackId, "改名后的谱包 Ω", "作者 / Updated"));
            var renamed = Capture(session);
            CheckHashes(edited.Digests, renamed.Digests, "title/artist do not alter gameplay digests");
            var beforeSaveAs = FileHashes(original);
            var copy = Path.Combine(root, "另存为 Copy");
            Directory.CreateDirectory(copy);
            session.Save(copy + Path.DirectorySeparatorChar);
            CheckHashes(beforeSaveAs, FileHashes(original), "save as leaves all source files unchanged");
            VerifyInFreshProcess(copy, renamed, manifest);

            document.Execute(new CopyChartCommand("hard", "spare", "charts/spare.json"));
            session.Save(copy);
            Check(File.Exists(Path.Combine(copy, "charts/spare.json")), "copied difficulty reaches disk");
            document.Execute(new DeleteChartCommand("spare"));
            document.Execute(new MoveChartCommand("mega", 0));
            var replacement = Path.Combine(root, "replacement.wav");
            PersistenceFixture.WriteWave(replacement, 440);
            document.Execute(new ReplaceChartResourceCommand("hard", true, false,
                "audio/replaced.wav", replacement));
            var replaced = Capture(session);
            Check(replaced.Digests["mega"] == renamed.Digests["mega"] &&
                replaced.Digests["hard"] != renamed.Digests["hard"] &&
                replaced.Digests["normal"] != renamed.Digests["normal"],
                "pack audio replacement affects inherited audio only");
            session.Save(copy);
            Check(!File.Exists(Path.Combine(copy, "charts/spare.json")) &&
                !File.Exists(Path.Combine(copy, "audio.wav")), "save removes obsolete chart and audio files");
            File.Move(replacement, replacement + ".unavailable");
            Check(document.PendingResourceReplacements.Count == 0, "successful save adopts package resource ownership");
            document.Execute(new EditPackMetadataCommand(document.PackId, "资源已入包", document.Artist));
            session.Save(copy);
            replaced = Capture(session);
            Check(document.HasUnsavedChanges == false, "same-session resave no longer needs external replacement");
            var moved = Path.Combine(root, "搬移后 Portable");
            Directory.Move(copy, moved);
            VerifyInFreshProcess(moved, replaced, manifest);

            // Repeated no-op saves must be byte-stable, not only digest-stable.
            session = new EditorDocumentSession();
            session.Open(moved);
            var stable = FileHashes(moved);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                session.Save(moved);
                session = new EditorDocumentSession();
                session.Open(moved);
                AssertSame(replaced, Capture(session), $"repeat save/reopen {cycle + 1}");
                CheckHashes(stable, FileHashes(moved), $"repeat save {cycle + 1} is byte-stable");
            }
            Console.WriteLine("PERSISTENCE LIFECYCLE PASS: " + moved);
        }
        finally
        {
            if (retainedRoot is null) Directory.Delete(root, true);
        }
    }

    internal static void VerifyChild(string package, string manifest)
    {
        var expected = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(manifest))!;
        var session = new EditorDocumentSession();
        var document = session.Open(package);
        Check(!document.HasUnsavedChanges && !document.CanUndo, "fresh process opens clean without old history");
        AssertSame(expected, Capture(session), "fresh-process strict reopen");
        foreach (var chart in document.Charts)
        {
            var loaded = V2Integration.LoadStrict(package, chart.Id);
            Check(loaded.GameplayDigest == expected.Digests[chart.Id] && loaded.NoteCount > 0 &&
                  double.IsFinite(loaded.DurationSec), $"shared gameplay bridge loads {chart.Id}");
        }
        Console.WriteLine("PERSISTENCE CHILD PASS");
    }

    private static Snapshot Capture(EditorDocumentSession session)
    {
        var document = session.Document!;
        var pack = document.BuildPackSnapshot();
        var charts = new Dictionary<string, string>(StringComparer.Ordinal);
        var resources = new Dictionary<string, string>(StringComparer.Ordinal);
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        string ResourcePath(string relative) => document.PendingResourceReplacements
            .FirstOrDefault(r => r.PackageRelativePath == relative)?.ExternalSourcePath ??
            (document.PackageDirectory is { } directory ? Path.Combine(directory, relative) :
                session.Draft!.ExternalResources[relative]);
        foreach (var entry in pack.Charts)
        {
            var chart = document.BuildChartSnapshot(entry.Id);
            charts.Add(entry.Id, Convert.ToHexString(V2JsonEncoder.EncodeChart(chart)));
            var relative = pack.ResolveAudio(entry);
            var audio = File.ReadAllBytes(ResourcePath(relative));
            resources[relative] = Convert.ToHexString(SHA256.HashData(audio));
            digests.Add(entry.Id, V2GameplayDigest.Compute(chart, entry, audio).Sha256);
        }
        if (pack.Cover is { } cover)
            resources[cover] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ResourcePath(cover))));
        return new(Convert.ToHexString(V2JsonEncoder.EncodePack(pack)), charts, resources, digests);
    }

    private static void AssertSame(Snapshot expected, Snapshot actual, string name)
    {
        Check(expected.Pack == actual.Pack, name + " metadata/order/preview");
        CheckHashes(expected.Charts, actual.Charts, name + " all authored chart fields");
        CheckHashes(expected.Resources, actual.Resources, name + " media hashes");
        CheckHashes(expected.Digests, actual.Digests, name + " gameplay digests");
    }

    private static Dictionary<string, string> FileHashes(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
            p => Path.GetRelativePath(directory, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));

    private static void CheckHashes(Dictionary<string, string> expected, Dictionary<string, string> actual, string name) =>
        Check(expected.Count == actual.Count && expected.All(pair =>
            actual.TryGetValue(pair.Key, out var value) && value == pair.Value), name);

    private static void VerifyInFreshProcess(string package, Snapshot expected, string manifest)
    {
        File.WriteAllText(manifest, JsonSerializer.Serialize(expected));
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(manifest)!, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(PackageLifecycleTests).Assembly.Location);
        start.ArgumentList.Add("--verify-persistence-child");
        start.ArgumentList.Add(package);
        start.ArgumentList.Add(manifest);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            process.Kill(true);
            throw new TimeoutException("Fresh-process package verification timed out");
        }
        var output = stdout.GetAwaiter().GetResult();
        var errors = stderr.GetAwaiter().GetResult();
        Console.Write(output);
        if (process.ExitCode != 0 || !output.Contains("PERSISTENCE CHILD PASS"))
            throw new InvalidOperationException($"Child failed ({process.ExitCode}): {errors}\n{output}");
    }
}
