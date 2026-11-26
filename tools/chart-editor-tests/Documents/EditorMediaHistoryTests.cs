using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorDifficultyWorkflowTests;
using static EditorTestSupport;

internal static class EditorMediaHistoryTests
{
    internal static void Run()
    {
        WithExternalResources(MediaOnlyPreservesUndoRedo);
        WithExternalResources(MixedSettingsRemainAtomic);
        WithExternalResources(PreviewHistoryDoesNotChangeRecording);
        WithExternalResources(CreatedChartKeepsItsCurrentRecording);
    }

    private static (EditorDocumentSession Session, EditorDocument Document, string Package) OpenPackage(
        string root, string audio, string cover)
    {
        var session = new EditorDocumentSession();
        AddTap(session.CreateDraft(BuildDraftRequest(audio, cover)), "base");
        var package = Path.Combine(root, "pack");
        session.Save(package);
        return (session, session.Open(package), package);
    }

    private static void MediaOnlyPreservesUndoRedo(string root, string audio, string cover)
    {
        var (session, d, package) = OpenPackage(root, audio, cover);
        var packReplacement = DifferentWav(root, audio, "new-pack.wav", 13);
        var chartReplacement = DifferentWav(root, audio, "new-chart.wav", 20);
        var newCover = Path.Combine(root, "new-cover.png");
        File.WriteAllBytes(newCover, [.. File.ReadAllBytes(cover), 1]);
        AddTap(d, "draft-note");
        d.Undo();
        var version = d.ChangeVersion;

        Apply(d, Settings(d) with
        {
            PackAudioSource = packReplacement, ChartAudioSource = chartReplacement, PackCoverSource = newCover,
        });
        Check(!d.CanUndo && d.CanRedo && d.IsDirty && d.ChangeVersion == version + 1,
            "media settings dirty the document and invalidate snapshots without changing the undo/redo stack");
        Check(session.ResolveDigestAudio() == chartReplacement && d.PendingResourceReplacements.Count == 3,
            "all three media changes are staged for preview and explicit save");
        Check(SameBytes(Path.Combine(package, d.Audio!), audio) && SameBytes(Path.Combine(package, d.Cover!), cover),
            "applying media never overwrites the package before Save");
        d.Redo();
        Check(d.SelectedChart.Notes.Count == 2 && session.ResolveDigestAudio() == chartReplacement,
            "redo still applies the original note edit without changing audio");
        d.Undo();
        Check(d.SelectedChart.Notes.Count == 1 && d.IsDirty && session.ResolveDigestAudio() == chartReplacement,
            "undoing the note to its saved position leaves the unsaved media intact and dirty");

        session.Save(package);
        Check(!d.IsDirty && d.CanRedo && d.PendingResourceReplacements.Count == 0,
            "saving adopts the new media and marks the document clean without clearing redo");
        d.Redo();
        d.Undo();
        Check(!d.IsDirty, "an authoring undo back to the new saved position is clean");
        var reopened = session.Open(package);
        Check(SameBytes(Path.Combine(package, reopened.Audio!), packReplacement) &&
            SameBytes(session.ResolveDigestAudio()!, chartReplacement) &&
            SameBytes(Path.Combine(package, reopened.Cover!), newCover),
            "strict reopen resolves the exact replacement audio and cover bytes");
    }

    private static void MixedSettingsRemainAtomic(string root, string audio, string cover)
    {
        var (session, d, _) = OpenPackage(root, audio, cover);
        var replacement = DifferentWav(root, audio, "mixed.wav", 25);
        var invalid = Settings(d) with { Title = "Other title", ChartId = "Not a chart ID", ChartAudioSource = replacement };
        var version = d.ChangeVersion;
        ExpectException<V2DiagnosticException>(() => Apply(d, invalid), "invalid rename rejects the mixed settings transaction");
        Check(d.SelectedChart.Id == "hard" && d.SelectedChart.Audio is null && d.Title != "Other title" &&
            d.PendingResourceReplacements.Count == 0 && !d.IsDirty && !d.CanUndo && d.ChangeVersion == version,
            "failed mixed settings roll back media and editable fields without changing history");

        var original = Settings(d);
        Apply(d, original with
        {
            Title = "Other title", ChartId = "renamed", ChartFile = "charts/renamed.json",
            ChartAudioSource = replacement, AudioOffsetSec = .125,
        });
        Check(d.CanUndo && d.SelectedChart.Id == "renamed" && d.SelectedChart.AudioOffsetSec == .125,
            "mixed settings apply rename and offset along with the recording");
        d.Undo();
        Check(!d.CanUndo && d.CanRedo && d.Title == original.Title && d.SelectedChart.Id == original.ChartId &&
            d.SelectedChart.AudioOffsetSec == original.AudioOffsetSec && session.ResolveDigestAudio() == replacement && d.IsDirty,
            "one undo restores the metadata and timing, but not the chart recording");
        d.Redo();
        Check(d.SelectedChart.Id == "renamed" && session.ResolveDigestAudio() == replacement,
            "redo restores non-media settings without replacing audio again");
    }

    private static void PreviewHistoryDoesNotChangeRecording(string root, string audio, string cover)
    {
        var (session, d, _) = OpenPackage(root, audio, cover);
        var preview = new V2Preview(0, 1);
        Apply(d, Settings(d) with { ChartAudioSource = audio, ChartPreview = preview });
        Apply(d, Settings(d) with { ClearChartAudio = true, ClearChartPreview = true });
        d.Undo();
        Check(d.SelectedChart.Preview == preview && d.SelectedChart.Audio is null,
            "undoing preview removal does not restore a cleared audio override");

        var replacement = DifferentWav(root, audio, "after-clear.wav", 30);
        Apply(d, Settings(d) with { ChartAudioSource = replacement });
        d.Redo();
        Check(d.SelectedChart.Preview is null && session.ResolveDigestAudio() == replacement,
            "replaying a preview-only edit does not touch a later recording");
        d.Undo();
        Check(d.SelectedChart.Preview == preview && session.ResolveDigestAudio() == replacement,
            "undoing preview-only history never writes the audio field");
    }

    private static void CreatedChartKeepsItsCurrentRecording(string root, string audio, string cover)
    {
        var (session, d, _) = OpenPackage(root, audio, cover);
        var original = DifferentWav(root, audio, "initial.wav", 40);
        var replacement = DifferentWav(root, audio, "later.wav", 45);
        session.AddDifficulty(Prepare(session, Request("second", DifficultyAudioSource.External, original)));
        var key = d.SelectedChart.Key;
        Apply(d, Settings(d) with { ChartAudioSource = replacement });
        d.Undo();
        Check(d.Charts.Count == 1 && d.PendingResourceReplacements.Count == 0,
            "undo after replacement removes the newly created chart, not just its recording change");
        File.Delete(original);
        d.Redo();
        Check(d.SelectedChart.Key == key && session.ResolveDigestAudio() == replacement,
            "redo restores the same chart with its latest recording, with no dependency on the previous source");
    }

    private static bool SameBytes(string a, string b) => File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b));
}
