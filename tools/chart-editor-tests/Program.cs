using DuxCommunity.ChartEditor.Core;
using DuxShared.Chart.V2;

internal static class Program
{
    private static int Main()
    {
        try
        {
            TestExactSnap();
            TestDraftCreationAndValidation();
            TestDraftPersistenceState();
            TestCommandsAndSnapshots();
            TestUndoFollowsOwningChart();
            TestBpmCollisionIsRejected();
            TestDirectEditingCommands();
            TestGoldenRoundTrip();
            TestStrictPackageOpen();
            TestPackageWriter();
            TestFirstPackageCreation();
            Console.WriteLine("EDITOR TESTS PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("EDITOR TESTS FAIL: " + exception);
            return 1;
        }
    }

    private static void TestExactSnap()
    {
        var input = ExactBarTime.FromJsonComponents(2, 1, 7);
        var snapped = EditorGeometry.Snap(input, 16);
        Check(snapped == ExactBarTime.FromJsonComponents(2, 1, 8), "exact arbitrary-denominator snap");
    }

    private static void TestDraftCreationAndValidation()
    {
        WithExternalResources((_, audio, cover) =>
        {
            var request = BuildDraftRequest(audio, cover) with
            {
                Difficulty = V2Difficulty.Custom,
                DifficultyKey = "SIGNAL",
                Level = null,
                Unrated = true,
                InitialBpm = 172.5,
                AudioOffsetSec = -.025,
                GridDivisor = 24,
            };
            var draft = EditorProjectDraftFactory.Create(request);
            var document = draft.Document;
            var pack = document.BuildPackSnapshot();
            var chart = document.BuildChartSnapshot(request.ChartId);
            Check(pack.Id == request.PackId && pack.Title == request.Title &&
                  pack.Artist == request.Artist && pack.Revision == 1,
                "typed draft keeps pack identity");
            Check(pack.Charts.Single().Difficulty == V2Difficulty.Custom &&
                  pack.Charts.Single().DifficultyKey == "SIGNAL" &&
                  pack.Charts.Single().Unrated && pack.Charts.Single().Level is null &&
                  pack.Charts.Single().Charters.SequenceEqual([request.Charter]),
                "typed draft keeps chart metadata");
            Check(draft.ChartDestination == $"charts/{request.ChartId}.json" &&
                  pack.Charts.Single().File == draft.ChartDestination &&
                  draft.AudioDestination == "audio.wav" && pack.Audio == "audio.wav" &&
                  draft.CoverDestination == "cover.png" && pack.Cover == "cover.png",
                "draft uses deterministic safe package destinations");
            Check(document.GridDivisor == 24 && chart.AudioOffsetSec == -.025 &&
                  chart.Bpms.Count == 1 && chart.Bpms[0] == new V2BpmEvent(ExactBarTime.Zero, 172.5),
                "draft keeps timing and grid settings");
            Check(chart.NotesLeft.Count == 0 && chart.NotesCenter.Count == 0 &&
                  chart.NotesRight.Count == 0 && document.SelectedChart.Notes.Count == 0,
                "draft never synthesizes a note");
            Check(draft.ExternalResources.Count == 2 &&
                  draft.ExternalResources[draft.AudioDestination] == Path.GetFullPath(audio) &&
                  draft.ExternalResources[draft.CoverDestination!] == Path.GetFullPath(cover),
                "draft exposes external resource imports");

            ExpectDiagnostic(() => EditorProjectDraftFactory.Create(request with
                {
                    Difficulty = V2Difficulty.Hard,
                    DifficultyKey = "forbidden",
                }), "new-project", "/charts/0/difficultyKey",
                "draft applies standard difficulty semantics");
            ExpectDiagnostic(() => EditorProjectDraftFactory.Create(request with
                {
                    Level = 0,
                    Unrated = false,
                }), "new-project", "/charts/0/level",
                "draft applies level-or-unrated semantics");
            ExpectDiagnostic(() => EditorProjectDraftFactory.Create(request with
                {
                    GridDivisor = 0,
                }), "new-project", "/gridDivisor",
                "draft rejects invalid grid divisor");
            ExpectDiagnostic(() => EditorProjectDraftFactory.Create(request with
                {
                    ExternalAudioSource = Path.Combine(Path.GetDirectoryName(audio)!, "missing.wav"),
                }), "new-project", "/audioSource",
                "draft rejects missing external audio");
            ExpectDiagnostic(() => EditorProjectDraftFactory.Create(request with
                {
                    ExternalCoverSource = Path.Combine(Path.GetDirectoryName(audio)!, "missing.png"),
                }), "new-project", "/coverSource",
                "draft rejects missing external cover");
        });
    }

    private static void TestDraftPersistenceState()
    {
        WithExternalResources((root, audio, _) =>
        {
            var draft = EditorProjectDraftFactory.Create(BuildDraftRequest(audio));
            var document = draft.Document;
            Check(document.IsUnpersisted && document.HasUnsavedChanges && document.IsDirty &&
                  !document.CanUndo && document.PackageDirectory is null,
                "new draft is unsaved without history");
            document.MarkSaved(Path.Combine(root, "persisted"));
            Check(!document.IsUnpersisted && !document.HasUnsavedChanges && !document.IsDirty &&
                  document.PackageDirectory == Path.GetFullPath(Path.Combine(root, "persisted")),
                "MarkSaved makes draft persisted and clean");
            AddTap(document, "draft-tap");
            Check(document.HasUnsavedChanges && document.IsDirty,
                "edit after MarkSaved is unsaved");
            document.Undo();
            Check(!document.HasUnsavedChanges && !document.IsDirty,
                "undo to saved draft history is clean");
        });

        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-pack");
        var opened = EditorPackageRepository.Open(fixture);
        Check(!opened.IsUnpersisted && !opened.HasUnsavedChanges && !opened.IsDirty,
            "opened document retains clean history semantics");
        AddDirtyNote(opened);
        Check(opened.HasUnsavedChanges, "opened document edit is unsaved");
        opened.Undo();
        Check(!opened.HasUnsavedChanges, "opened document undo returns to clean history");
    }

    private static void TestCommandsAndSnapshots()
    {
        var document = CreateCleanDocument("editor.test", "hard", "Editor Test", "Community");
        var note = new EditableNote
        {
            Id = document.AllocateId("tap"),
            Type = V2NoteType.Tap,
            Track = EditorTrack.Center,
            Time = ExactBarTime.FromJsonComponents(0, 1, 5),
            Center = 2.5,
            Width = 1.0,
        };
        document.Execute(new AddNoteCommand(note));
        Check(document.SelectedChart.Notes.Count == 1 && document.CanUndo, "add note command");
        document.Execute(new MoveNoteCommand(note.Id, ExactBarTime.FromJsonComponents(1, 1, 7), 5.1, .6));
        Check(document.SelectedChart.Notes[0].Center == 5.1, "move command");
        document.Undo();
        Check(document.SelectedChart.Notes[0].Center == 2.5, "undo move command");
        document.Undo();
        Check(!document.IsDirty, "undo back to saved history is clean");
        document.Redo();
        document.Redo();
        Check(document.SelectedChart.Notes[0].Time == ExactBarTime.FromJsonComponents(1, 1, 7), "redo move command");

        var chart = document.BuildChartSnapshot("hard");
        V2SemanticValidator.ValidateChart(chart, "chart.json");
        Check(EditorValidationService.Validate(document).Any(item => item.Severity == V2DiagnosticSeverity.Warning),
            "overscan produces warning without changing v2 data");
    }

    private static void TestUndoFollowsOwningChart()
    {
        var first = CreateCleanDocument("editor.history", "hard", "History", "Community");
        var pack = first.BuildPackSnapshot();
        var secondEntry = pack.Charts[0] with { Id = "mega", File = "chart_mega.json" };
        var twoChartPack = pack with { Charts = [pack.Charts[0], secondEntry] };
        var charts = new Dictionary<string, V2Chart>(StringComparer.Ordinal)
        {
            ["hard"] = first.BuildChartSnapshot("hard"),
            ["mega"] = first.BuildChartSnapshot("hard") with { ChartId = "mega" },
        };
        var document = EditorDocument.FromPackage(null, twoChartPack, charts);
        var note = new EditableNote
        {
            Id = document.AllocateId("tap"),
            Type = V2NoteType.Tap,
            Track = EditorTrack.Center,
            Time = ExactBarTime.Zero,
            Center = 2.5,
            Width = 1.0,
        };
        document.Execute(new AddNoteCommand(note));
        document.SelectChart("mega");
        document.Undo();
        Check(document.SelectedChartId == "hard" && document.Charts.Single(chart => chart.Id == "hard").Notes.Count == 0,
            "undo switches to command-owning chart");
        document.SelectChart("mega");
        document.Redo();
        Check(document.SelectedChartId == "hard" && document.Charts.Single(chart => chart.Id == "hard").Notes.Count == 1,
            "redo switches to command-owning chart");
    }

    private static void TestBpmCollisionIsRejected()
    {
        var document = CreateCleanDocument("editor.bpm", "hard", "BPM", "Community");
        document.Execute(new AddBpmCommand(ExactBarTime.FromFraction(1, 1), 180));
        var original = document.SelectedChart.Bpms.Single(item => item.Time == ExactBarTime.Zero);
        try
        {
            document.Execute(new EditBpmCommand(original.Time, ExactBarTime.FromFraction(1, 1), 200));
        }
        catch (InvalidOperationException)
        {
            Check(document.SelectedChart.Bpms.Count(item => item.Time == ExactBarTime.Zero) == 1 &&
                document.SelectedChart.Bpms.Single(item => item.Time == ExactBarTime.Zero).Bpm == 150,
                "BPM collision leaves original event unchanged");
            return;
        }
        throw new InvalidOperationException("BPM collision was accepted");
    }

    private static void TestDirectEditingCommands()
    {
        var document = CreateCleanDocument(
            "editor.direct", "hard", "Direct Edit", "Community");
        var hold = new EditableNote
        {
            Id = document.AllocateId("hold"),
            Type = V2NoteType.Hold,
            Track = EditorTrack.Center,
            Time = ExactBarTime.FromFraction(1, 1),
            Center = 1.5,
            Width = .75,
        };
        hold.Nodes.Add(new EditablePathNode
        {
            Id = document.AllocateId("hold-node"),
            Time = ExactBarTime.FromFraction(2, 1),
            Center = 2.0,
            Width = 1.0,
            Judge = true,
        });
        document.Execute(new AddNoteCommand(hold));
        document.Execute(new EditPathTerminalCommand(
            hold.Id, ExactBarTime.FromFraction(5, 2), 2.75, 1.25));
        var edited = document.SelectedChart.Notes.Single();
        Check(edited.Nodes[^1].Time == ExactBarTime.FromFraction(5, 2) &&
              edited.Nodes[^1].Center == 2.75 && edited.Nodes[^1].Width == 1.25,
            "hold terminal edit command");
        document.Undo();
        Check(edited.Nodes[^1].Time == ExactBarTime.FromFraction(2, 1) &&
              edited.Nodes[^1].Center == 2.0 && edited.Nodes[^1].Width == 1.0,
            "undo hold terminal edit");
        document.Redo();
        Check(edited.Nodes[^1].Time == ExactBarTime.FromFraction(5, 2),
            "redo hold terminal edit");

        document.Execute(new EditChartOffsetCommand(.015));
        Check(document.SelectedChart.AudioOffsetSec == .015,
            "chart offset command marks new value");
        document.Undo();
        Check(document.SelectedChart.AudioOffsetSec == 0,
            "undo chart offset command");
        document.Redo();
        Check(document.SelectedChart.AudioOffsetSec == .015,
            "redo chart offset command");

        document.Execute(new CompositeEditorCommand(
            new MoveNoteCommand(hold.Id, ExactBarTime.FromFraction(3, 2), 2.25, .875),
            new EditPathTerminalCommand(hold.Id,
                ExactBarTime.FromFraction(3, 1), 3.0, 1.5)));
        Check(edited.Time == ExactBarTime.FromFraction(3, 2) &&
              edited.Nodes[^1].Time == ExactBarTime.FromFraction(3, 1),
            "head and terminal update as one command");
        document.Undo();
        Check(edited.Time == ExactBarTime.FromFraction(1, 1) &&
              edited.Nodes[^1].Time == ExactBarTime.FromFraction(5, 2),
            "composite head and terminal undo together");

        try
        {
            document.Execute(new DeleteBpmCommand(ExactBarTime.Zero));
        }
        catch (InvalidOperationException)
        {
            Check(document.SelectedChart.Bpms.Count == 1 &&
                  document.SelectedChart.Bpms[0].Time == ExactBarTime.Zero,
                "base BPM deletion is rejected without mutation");
            return;
        }
        throw new InvalidOperationException("base BPM deletion was accepted");
    }

    private static void TestGoldenRoundTrip()
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

    private static void TestStrictPackageOpen()
    {
        var root = Path.Combine(Path.GetTempPath(), "dux-editor-open-" + Guid.NewGuid().ToString("N"));
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

    private static void TestPackageWriter()
    {
        var root = Path.Combine(Path.GetTempPath(), "dux-editor-writer-" + Guid.NewGuid().ToString("N"));
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

            var nonWavePack = changedPathPack with { Audio = "audio.mp3" };
            File.Copy(Path.Combine(source, "audio.wav"), Path.Combine(source, "audio.mp3"));
            ExpectDiagnostic(() => V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source,
                DestinationDirectory = failedDestination,
                Pack = nonWavePack,
                Charts = document.BuildAllChartSnapshots(),
            }), "audio.mp3", "/", "non-WAV audio diagnostic");
            Check(!Directory.Exists(failedDestination) && document.IsDirty,
                "non-WAV failure leaves destination absent and document dirty");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void TestFirstPackageCreation()
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
                  !Directory.EnumerateFileSystemEntries(root, "allowed-empty.dux-empty-*",
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

    private static EditorDocument LoadDocument(string root)
    {
        var pack = V2JsonDecoder.DecodePackFile(Path.Combine(root, "meta.json"));
        var charts = pack.Charts.ToDictionary(entry => entry.Id,
            entry => V2JsonDecoder.DecodeChartFile(Path.Combine(root, entry.File)), StringComparer.Ordinal);
        return EditorDocument.FromPackage(root, pack, charts);
    }

    private static void AddDirtyNote(EditorDocument document)
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

    private static V2PackageWriteRequest BuildWriteRequest(EditorDocument document, string source, string destination) => new()
    {
        SourceDirectory = source,
        DestinationDirectory = destination,
        Pack = document.BuildPackSnapshot(),
        Charts = document.BuildAllChartSnapshots(),
        AllowEmptyDestination = true,
    };

    private static V2PackageCreateRequest BuildCreateRequest(EditorProjectDraft draft,
        string destination, IReadOnlyList<V2ExternalResourceMapping>? resources = null)
    {
        var request = draft.BuildPackageCreateRequest(destination);
        return resources is null ? request : request with { ExternalResources = resources };
    }

    private static EditorProjectDraftRequest BuildDraftRequest(string audio, string? cover = null) => new(
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

    private static EditorDocument CreateCleanDocument(string packId, string chartId,
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

    private static void AddTap(EditorDocument document, string id)
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

    private static void WithExternalResources(Action<string, string, string> test)
    {
        var root = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory)!,
            "dux-editor-draft-" + Guid.NewGuid().ToString("N"));
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

    private static IEnumerable<string> FindStagingDirectories(string root) =>
        Directory.EnumerateDirectories(root, "*.dux-staging-*", SearchOption.TopDirectoryOnly);

    private static void ExpectDiagnostic(Action action, string source, string pointer, string name)
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

    private static void ExpectException<TException>(Action action, string name)
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

    private static void CopyDirectory(string source, string destination)
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

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException(name);
        Console.WriteLine("PASS: " + name);
    }
}
