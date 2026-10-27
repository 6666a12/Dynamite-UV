using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

internal static class EditorTestSupport
{

    internal static EditorDocument LoadDocument(string root)
    {
        var pack = V2JsonDecoder.DecodePackFile(Path.Combine(root, "meta.json"));
        var charts = pack.Charts.ToDictionary(entry => entry.Id,
            entry => V2JsonDecoder.DecodeChartFile(Path.Combine(root, entry.File)), StringComparer.Ordinal);
        return EditorDocument.FromPackage(root, pack, charts);
    }

    internal static void AddDirtyNote(EditorDocument document)
    {
        document.SelectChart("hard");
        document.Execute(new AddNoteCommand(new EditableNote
        {
            Id = document.AllocateId("tap"),
            Type = V2NoteType.Tap,
            Track = EditorTrack.Center,
            Time = ExactBarTime.FromJsonComponents(0, 1, 10),
            Center = 2.5,
            Width = 1,
        }));
    }

    internal static V2PackageWriteRequest BuildWriteRequest(EditorDocument document, string source, string destination) => new()
    {
        SourceDirectory = source,
        DestinationDirectory = destination,
        Pack = document.BuildPackSnapshot(),
        Charts = document.BuildAllChartSnapshots(),
        AllowEmptyDestination = true,
    };

    internal static V2PackageCreateRequest BuildCreateRequest(EditorProjectDraft draft,
        string destination, IReadOnlyList<V2ExternalResourceMapping>? resources = null)
    {
        var request = draft.BuildPackageCreateRequest(destination);
        return resources is null ? request : request with { ExternalResources = resources };
    }

    internal static EditorProjectDraftRequest BuildDraftRequest(string audio, string? cover = null) => new(
        "editor.new-project",
        "New Project",
        "Community Artist",
        "hard",
        V2Difficulty.Hard,
        null,
        10,
        false,
        "Community Charter",
        150,
        0,
        16,
        audio,
        cover);

    internal static EditorDocument CreateCleanDocument(string packId, string chartId,
        string title, string artist)
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
            Audio = "audio.wav",
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
        return EditorDocument.FromPackage(Path.Combine(Path.GetTempPath(), packId), pack,
            new Dictionary<string, V2Chart>(StringComparer.Ordinal) { [chartId] = chart });
    }

    internal static EditableNote RequireTestNote(this EditorDocument document, string id) =>
        document.SelectedChart.Notes.Single(note => note.Id == id);

    internal static void AddTap(EditorDocument document, string id)
    {
        document.Execute(new AddNoteCommand(new EditableNote
        {
            Id = id,
            Type = V2NoteType.Tap,
            Track = EditorTrack.Center,
            Time = ExactBarTime.Zero,
            Center = 2.5,
            Width = 1,
        }));
    }

    internal static void WithExternalResources(Action<string, string, string> test)
    {
        var root = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory)!,
            "dynamite-universe-editor-draft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var audio = Path.Combine(root, "source.wav");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack", "audio.wav"), audio);
            var cover = Path.Combine(root, "source.png");
            File.WriteAllBytes(cover, [137, 80, 78, 71, 13, 10, 26, 10]);
            test(root, audio, cover);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    internal static IEnumerable<string> FindStagingDirectories(string root) =>
        Directory.EnumerateDirectories(root, "*.dynamite-universe-staging-*", SearchOption.TopDirectoryOnly);

    internal static void ExpectDiagnostic(Action action, string source, string pointer, string name)
    {
        try
        {
            action();
        }
        catch (V2DiagnosticException exception)
        {
            Check((exception.SourceName.EndsWith(source, StringComparison.Ordinal) ||
                   Path.GetFileName(exception.SourceName).Equals(source, StringComparison.Ordinal)) &&
                exception.JsonPointer == pointer, name);
            return;
        }
        throw new InvalidOperationException(name + " did not throw a v2 diagnostic");
    }

    internal static void ExpectException<TException>(Action action, string name)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            Check(true, name);
            return;
        }
        throw new InvalidOperationException(name + $" did not throw {typeof(TException).Name}");
    }

    internal static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    internal static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException(name);
        Console.WriteLine("PASS: " + name);
    }
}
