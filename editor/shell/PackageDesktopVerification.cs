using Avalonia.Controls;
using Avalonia.Input;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Real HWND/IPC/audio smoke of an externally generated clean-room persistence package.</summary>
internal static class PackageDesktopVerification
{
    internal static async Task Run(MainWindow window, string source, string output,
        Action<string> report, Func<string, Task> capture)
    {
        var expected = EditorPackageRepository.Open(source);
        await window.OpenPackageForVerification(source);
        await Task.Delay(800);
        var document = window.CurrentDocument ?? throw new InvalidOperationException(window.StatusMessage);
        AssertSnapshots(expected, document);
        foreach (var chart in document.Charts)
        {
            window.SelectChartForVerification(chart.Id);
            var runtimeCount = V2RuntimeAdapter.Adapt(document.BuildChartSnapshot(chart.Id)).RuntimeChart.AllNotes.Count();
            var revision = window.StageRevision;
            for (var i = 0; i < 300 && !(window.LastStageInput.Contains($"chart={chart.Id} notes={runtimeCount} audio=True") &&
                     window.LastStageInput.Contains($"revision={revision} ")); i++) await Task.Delay(50);
            if (!window.LastStageInput.Contains($"chart={chart.Id} notes={runtimeCount} audio=True") ||
                !window.LastStageInput.Contains($"revision={revision} "))
                throw new InvalidOperationException("Live stage did not load chart/audio: " + window.LastStageInput);
            window.SeekForVerification(2);
            window.ShortcutForVerification(Key.Space);
            await Task.Delay(350);
            var first = await window.StageServiceForVerification.AudioSnapshotAsync();
            await Task.Delay(350);
            var second = await window.StageServiceForVerification.AudioSnapshotAsync();
            window.ShortcutForVerification(Key.Space);
            if (!first.Playing || second.AudioSecond <= first.AudioSecond)
                throw new InvalidOperationException($"Audio did not advance for {chart.Id}: {first} -> {second}");
            report($"PASS live stage {chart.Id}: {chart.Notes.Count} parents, audio advances");
        }
        await capture("01-loaded-package.png");
        var saved = Path.Combine(Path.GetFullPath(output), "saved-package");
        if (Directory.Exists(saved)) throw new IOException("Smoke output must be new: " + saved);
        await window.SavePackageToForVerification(saved);
        if (document.HasUnsavedChanges || document.PackageDirectory != saved)
            throw new InvalidOperationException("Shell Save As failed: " + window.StatusMessage);
        AssertSnapshots(expected, EditorPackageRepository.Open(saved));
        report("PASS Shell Save As preserves all metadata and chart fields");

        window.SelectChartForVerification("hard");
        var tap = document.SelectedChart.Notes.First(n => n.Type == V2NoteType.Tap);
        var before = tap.ScrollSpeedMultiplier;
        window.SelectNoteForVerification(tap.Id);
        var after = before == 1.6 ? 1.4 : 1.6;
        window.ApplySpeedForVerification(after);
        await Task.Delay(300);
        if (!document.HasUnsavedChanges || tap.ScrollSpeedMultiplier != after)
            throw new InvalidOperationException("Inspector did not edit the actual document");
        window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
        if (document.SelectedChart.Notes.Single(n => n.Id == tap.Id).ScrollSpeedMultiplier != before)
            throw new InvalidOperationException("Shell undo failed");
        window.ShortcutForVerification(Key.Y, KeyModifiers.Control);
        window.ShortcutForVerification(Key.S, KeyModifiers.Control);
        for (var i = 0; i < 100 && document.HasUnsavedChanges; i++) await Task.Delay(50);
        if (document.HasUnsavedChanges) throw new InvalidOperationException("Ctrl+S failed: " + window.StatusMessage);
        var reopened = EditorPackageRepository.Open(saved);
        AssertSnapshots(document, reopened);
        if (reopened.Charts.Single(c => c.Id == "hard").Notes.Single(n => n.Id == tap.Id).ScrollSpeedMultiplier != after)
            throw new InvalidOperationException("Inspector edit did not survive disk reload");
        report("PASS live Inspector edit / Ctrl+Z / Ctrl+Y / Ctrl+S / strict disk reload");
        await window.OpenPackageForVerification(saved);
        await Task.Delay(600);
        AssertSnapshots(reopened, window.CurrentDocument!);
        await capture("02-saved-reopened.png");
        if (window.Stage.FindControl<CheckBox>("GamePreviewToggle") is not null)
            throw new InvalidOperationException("Game preview must not have a public UI entry");
        window.Stage.SetGamePreviewForVerification(true);
        window.SeekForVerification(1);
        await Task.Delay(600);
        await window.StageServiceForVerification.AudioSnapshotAsync(); // ordered IPC barrier
        await capture("03-game-preview.png");
        window.Stage.SetGamePreviewForVerification(false);
        await Task.Delay(300);
        AssertSnapshots(reopened, window.CurrentDocument!);
        if (window.CurrentDocument!.HasUnsavedChanges) throw new InvalidOperationException("Preview dirtied the document");
        report("PASS game preview toggle and seek preserve the saved document");
        report("PASS Shell reopens saved package with the native stage still running");
    }

    private static void AssertSnapshots(EditorDocument expected, EditorDocument actual)
    {
        if (!V2JsonEncoder.EncodePack(expected.BuildPackSnapshot()).SequenceEqual(
                V2JsonEncoder.EncodePack(actual.BuildPackSnapshot())))
            throw new InvalidOperationException("Package metadata changed across Shell persistence");
        foreach (var chart in expected.Charts)
            if (!V2JsonEncoder.EncodeChart(expected.BuildChartSnapshot(chart.Id)).SequenceEqual(
                    V2JsonEncoder.EncodeChart(actual.BuildChartSnapshot(chart.Id))))
                throw new InvalidOperationException("Chart data changed across Shell persistence: " + chart.Id);
    }
}
