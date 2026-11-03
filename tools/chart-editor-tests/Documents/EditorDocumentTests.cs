using static EditorTestSupport;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

internal static class EditorDocumentTests
{

    internal static void TestPropertyCatalog()
    {
        EditorPropertyCatalog.Validate();
        var expected = new[]
        {
            "pack.format", "pack.formatVersion", "pack.id", "pack.revision", "pack.title",
            "pack.artist", "pack.audio", "pack.cover", "pack.preview.startSec",
            "pack.preview.durationSec", "pack.charts[]", "charts[].id", "charts[].difficulty",
            "charts[].difficultyKey", "charts[].level", "charts[].unrated", "charts[].charters[]",
            "charts[].file", "charts[].audio", "charts[].preview.startSec",
            "charts[].preview.durationSec", "chart.format", "chart.formatVersion", "chart.chartId",
            "chart.audioOffsetSec", "chart.bpms[].time", "chart.bpms[].bpm",
            "chart.scrollSpeeds[].time", "chart.scrollSpeeds[].value", "chart.scrollSpeeds[].curveToNext",
            "chart.notesLeft[]", "chart.notesCenter[]", "chart.notesRight[]", "chart.notes[].id",
            "chart.notes[].type", "chart.notes[].time", "chart.notes[].center", "chart.notes[].width",
            "chart.notes[].scrollSpeedMultiplier", "chart.notes[].curveToNext", "chart.notes[].nodes[]",
            "chart.notes[].nodes[].id",
            "chart.notes[].nodes[].time", "chart.notes[].nodes[].center", "chart.notes[].nodes[].width",
            "chart.notes[].nodes[].curveToNext", "chart.notes[].nodes[].judge",
            "chart.notes[].nodes[].judgeWasExplicit",
        };
        Check(EditorPropertyCatalog.All.Select(item => item.FieldPath).Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal)),
            "property catalog covers every v2 persisted and derived editor field");
        Check(EditorPropertyCatalog.Require("pack.format").ReadOnlyReason is not null &&
              EditorPropertyCatalog.Require("chart.audioOffsetSec").EditRoute == EditorPropertyEditRoute.EditorCommand &&
              EditorPropertyCatalog.Require("pack.audio").EditRoute == EditorPropertyEditRoute.PackageResourceTransaction,
            "property catalog distinguishes fixed, command, and transactional fields");
    }

    internal static void TestDraftCreationAndValidation()
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

    internal static void TestDraftPersistenceState()
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

    internal static void TestCommandsAndSnapshots()
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
        Check(document.Selection.Key == document.SelectedChart.Notes.Single().Key,
            "note selection resolves stable internal key");
        var noteKey = document.SelectedChart.Notes.Single().Key;
        document.Execute(new RenameNoteCommand(note.Id, "renamed-note"));
        Check(document.SelectedChart.Notes.Single().Id == "renamed-note" &&
              document.SelectedChart.Notes.Single().Key == noteKey,
            "note identity command preserves stable key");
        document.Undo();
        Check(document.SelectedChart.Notes.Single().Id == note.Id && document.SelectedChart.Notes.Single().Key == noteKey,
            "undo note identity restores serialized id");
        document.Redo();
        document.Execute(new SetNoteTrackCommand("renamed-note", EditorTrack.Left));
        Check(document.SelectedChart.Notes.Single().Track == EditorTrack.Left,
            "note track command edits owning track");
        document.Undo();
        Check(document.SelectedChart.Notes.Single().Track == EditorTrack.Center,
            "undo note track restores owning track");
        document.Redo();
        document.Undo();
        document.Undo();
        document.Execute(new MoveNoteCommand(note.Id, ExactBarTime.FromJsonComponents(1, 1, 7), 5.1, .6));
        Check(document.SelectedChart.Notes[0].Center == 5.1, "move command");
        document.Undo();
        Check(document.SelectedChart.Notes[0].Center == 2.5, "undo move command");
        document.Undo();
        Check(!document.IsDirty, "undo back to saved history is clean");
        document.Redo();
        document.Redo();
        Check(document.SelectedChart.Notes[0].Time == ExactBarTime.FromJsonComponents(1, 1, 7), "redo move command");

        document.Execute(new SetNoteScrollSpeedMultiplierCommand(note.Id, .5));
        Check(document.SelectedChart.Notes[0].ScrollSpeedMultiplier == .5,
            "note scroll speed multiplier command");
        document.Undo();
        Check(document.SelectedChart.Notes[0].ScrollSpeedMultiplier == 1.0,
            "undo note scroll speed multiplier");
        document.Redo();
        ExpectException<InvalidOperationException>(() => document.Execute(
            new SetNoteScrollSpeedMultiplierCommand(note.Id, 0)),
            "zero note scroll speed multiplier is rejected");
        Check(document.SelectedChart.Notes[0].ScrollSpeedMultiplier == .5,
            "invalid note scroll speed multiplier is rejected without mutation");

        var chart = document.BuildChartSnapshot("hard");
        V2SemanticValidator.ValidateChart(chart, "chart.json");
        Check(chart.NotesCenter[0].ScrollSpeedMultiplier == .5,
            "note scroll speed multiplier reaches strict snapshot");
        Check(EditorValidationService.Validate(document).Any(item => item.Severity == V2DiagnosticSeverity.Warning),
            "overscan produces warning without changing v2 data");
    }

    internal static void TestUndoFollowsOwningChart()
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

    internal static void TestChartLifecycleCommands()
    {
        var document = CreateCleanDocument("editor.charts", "hard", "Charts", "Community");
        document.Execute(new AddNoteCommand(new EditableNote
        {
            Id = "hard-tap", Type = V2NoteType.Tap, Track = EditorTrack.Center,
            Time = ExactBarTime.Zero, Center = 2.5, Width = 1,
        }));
        document.Execute(new EditChartDetailsCommand("hard", V2Difficulty.Mega, null, 15, false,
            ["Lead Charter", "Co-Charter"]));
        Check(document.SelectedChart.Difficulty == V2Difficulty.Mega && document.SelectedChart.Level == 15 &&
              !document.SelectedChart.Unrated && document.SelectedChart.Charters.Count == 2,
            "chart metadata command edits difficulty level and credits");
        document.Undo();
        Check(document.SelectedChart.Difficulty == V2Difficulty.Hard && document.SelectedChart.Unrated &&
              document.SelectedChart.Charters.SequenceEqual(["Unknown"]),
            "undo chart metadata restores entry details");
        document.Redo();
        ExpectException<V2DiagnosticException>(() => document.Execute(
            new EditChartDetailsCommand("hard", V2Difficulty.Custom, null, 15, false, ["Community"])),
            "chart metadata rejects custom difficulty without key");
        Check(document.SelectedChart.Difficulty == V2Difficulty.Mega && document.SelectedChart.Level == 15,
            "invalid chart metadata restores prior entry details");
        document.Execute(new RenameChartCommand("hard", "mega-chart", "charts/mega-chart.json"));
        var renamedKey = document.SelectedChart.Key;
        Check(document.SelectedChartId == "mega-chart" && document.SelectedChart.Id == "mega-chart" &&
              document.SelectedChart.File == "charts/mega-chart.json",
            "chart identity command edits id and storage path");
        document.Undo();
        Check(document.SelectedChartId == "hard" && document.SelectedChart.File == "charts/hard.json" &&
              document.SelectedChart.Key == renamedKey,
            "undo chart identity restores id and storage path");
        document.Redo();
        Check(document.SelectedChartId == "mega-chart" && document.SelectedChart.File == "charts/mega-chart.json" &&
              document.SelectedChart.Key == renamedKey,
            "redo chart identity restores renamed chart");
        document.Undo();
        WithExternalResources((root, audio, cover) =>
        {
            document.Execute(new ReplaceChartResourceCommand("hard", true, false, "audio/replaced.wav", audio));
            Check(document.Audio == "audio/replaced.wav" &&
                  document.PendingResourceReplacements.Any(item => item.PackageRelativePath == "audio/replaced.wav"),
                "resource replacement stages pack audio");
            document.Undo();
            Check(document.Audio == "audio.wav" && document.PendingResourceReplacements.Count == 0,
                "undo resource replacement restores pack audio");
            document.Redo();
            Check(document.Audio == "audio/replaced.wav",
                "redo resource replacement restores staged audio");
            document.Undo();
        });
        var normal = new EditableChart
        {
            Id = "normal", Difficulty = V2Difficulty.Normal, Unrated = true,
            File = "charts/normal.json", AudioOffsetSec = 0,
        };
        normal.Charters.Add("Community");
        normal.Bpms.Add(new EditableBpm { Time = ExactBarTime.Zero, Bpm = 150 });
        document.Execute(new AddChartCommand(normal));
        Check(document.Charts.Select(chart => chart.Id).SequenceEqual(["hard", "normal"]) &&
              document.SelectedChartId == "normal", "add chart command selects new chart");
        document.Undo();
        Check(document.Charts.Count == 1 && document.SelectedChartId == "hard",
            "undo add chart restores prior selection");
        document.Redo();
        Check(document.Charts.Count == 2 && document.SelectedChartId == "normal",
            "redo add chart restores new chart");

        document.Execute(new AddNoteCommand(new EditableNote
        {
            Id = "normal-tap", Type = V2NoteType.Tap, Track = EditorTrack.Center,
            Time = ExactBarTime.Zero, Center = 2.5, Width = 1,
        }));
        document.Execute(new CopyChartCommand("normal", "mega", "charts/mega.json"));
        var copy = document.Charts.Single(chart => chart.Id == "mega");
        Check(copy.Notes.Single().Id == "normal-tap" && document.SelectedChartId == "mega",
            "copy chart preserves authored data in a separate chart");
        document.Undo();
        Check(!document.Charts.Any(chart => chart.Id == "mega") && document.SelectedChartId == "normal",
            "undo copied chart restores source selection");
        document.Redo();
        Check(document.Charts.Any(chart => chart.Id == "mega") && document.SelectedChartId == "mega",
            "redo copied chart restores copied chart selection");
        document.Execute(new MoveChartCommand("mega", 0));
        Check(document.Charts[0].Id == "mega", "move chart command reorders metadata");
        document.Undo();
        Check(document.Charts.Select(chart => chart.Id).SequenceEqual(["hard", "normal", "mega"]),
            "undo chart reorder restores original order");
        document.Execute(new DeleteChartCommand("normal"));
        Check(!document.Charts.Any(chart => chart.Id == "normal") && document.Charts.Count == 2,
            "delete chart command removes selected chart");
        document.Undo();
        Check(document.Charts.Select(chart => chart.Id).SequenceEqual(["hard", "normal", "mega"]),
            "undo delete chart restores chart and order");
        ExpectException<InvalidOperationException>(() =>
        {
            var only = CreateCleanDocument("editor.only", "hard", "Only", "Community");
            only.Execute(new DeleteChartCommand("hard"));
        }, "cannot delete last chart");
        V2SemanticValidator.ValidatePack(document.BuildPackSnapshot());
        foreach (var chart in document.Charts)
            V2SemanticValidator.ValidateChart(document.BuildChartSnapshot(chart.Id), chart.File);
        Check(true, "chart lifecycle snapshots remain strict");
    }
}
