using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using Ursa.Controls;

namespace DynamiteUniverse.Editor.Shell;

internal static class SaveInteractionVerification
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var source = Path.Combine(root, "package");
        Verification.CreatePackage(source);
        var window = new MainWindow(Path.Combine(root, "preferences"));
        var interceptor = typeof(V2PackageWriter).GetProperty("PublicationInterceptor", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var arrived = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        try
        {
            window.Show();
            await window.OpenPackageForVerification(source);
            var document = window.CurrentDocument!;
            var id = document.SelectedChart.Notes[0].Id;
            window.SelectNoteForVerification(id);
            window.ApplySpeedForVerification(1.3);
            var button = window.GetVisualDescendants().OfType<IconButton>()
                .Single(b => Equals(b.Content, WorkbenchLocalizer.Shared["Header.Save"]));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForSave(window);
            check("Actual SAVE button publishes chart bytes", ReadSpeed() == 1.3 && !document.HasUnsavedChanges);

            window.SelectNoteForVerification(id);
            window.ApplySpeedForVerification(1.7);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var text = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsVisible && t.IsEnabled);
            text.Focus();
            check("Save shortcut fixture has text focus", text.IsFocused);
            window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
            window.KeyRelease(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
            await WaitForSave(window);
            check("Ctrl+S from text input writes to disk", ReadSpeed() == 1.7 && !document.HasUnsavedChanges);

            window.ApplySpeedForVerification(1.8);
            var firstAudio = ReplacePackageAudio("first.wav", 1);
            interceptor.SetValue(null, (Action<string>)(_ =>
            {
                arrived.Set();
                if (!resume.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Save publication barrier");
            }));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 500 && !arrived.IsSet; i++) await Task.Delay(10);
            if (!arrived.IsSet) throw new TimeoutException("Save did not reach publication");
            window.Close();
            check("Window cannot close while save is publishing", window.IsVisible);
            window.ApplySpeedForVerification(1.9);
            var secondAudio = ReplacePackageAudio("second.wav", 2);
            resume.Set();
            await WaitForSave(window);
            interceptor.SetValue(null, null);
            check("Save snapshot does not mark concurrent edit saved", ReadSpeed() == 1.8 &&
                document.SelectedChart.Notes[0].ScrollSpeedMultiplier == 1.9 && document.HasUnsavedChanges);
            check("Saving a media snapshot leaves a newer replacement pending at the same slot",
                File.ReadAllBytes(Path.Combine(source, document.Audio!)).SequenceEqual(File.ReadAllBytes(firstAudio)) &&
                document.PendingResourceReplacements.Single().ExternalSourcePath == secondAudio);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForSave(window);
            check("Second save publishes edits made during first save", ReadSpeed() == 1.9 && !document.HasUnsavedChanges);
            check("Saving the current media snapshot clears its pending import and dirty state",
                File.ReadAllBytes(Path.Combine(source, document.Audio!)).SequenceEqual(File.ReadAllBytes(secondAudio)) &&
                document.PendingResourceReplacements.Count == 0 && !document.HasUnsavedChanges);

            string ReplacePackageAudio(string file, byte lastByte)
            {
                var path = Path.Combine(root, file);
                var bytes = File.ReadAllBytes(Path.Combine(root, "fixture.wav"));
                bytes[^1] = lastByte;
                File.WriteAllBytes(path, bytes);
                var chart = document.SelectedChart;
                window.ApplyPackageSettingsForVerification(new EditorPackageSettingsRequest(document.PackId, document.Title,
                    document.Artist, chart.Id, chart.File, chart.Difficulty, chart.DifficultyKey, chart.Level, chart.Unrated,
                    chart.Charters, chart.AudioOffsetSec, document.Preview, chart.Preview, path, null, null, false, false));
                return path;
            }

            window.ApplySpeedForVerification(2.0);
            window.ClosePromptForVerification = () => Task.FromResult(UnsavedCloseChoice.Cancel);
            window.Close();
            await window.CloseDecisionForVerification;
            check("Dirty close cancellation preserves document and window", window.IsVisible && document.HasUnsavedChanges);
            window.ClosePromptForVerification = () => Task.FromResult(UnsavedCloseChoice.Save);
            window.Close();
            await window.CloseDecisionForVerification;
            check("Dirty close saves before closing", !window.IsVisible && ReadSpeed() == 2.0 && !document.HasUnsavedChanges);
        }
        finally
        {
            resume.Set();
            interceptor.SetValue(null, null);
            await WaitForSave(window);
            window.ClosePromptForVerification = () => Task.FromResult(UnsavedCloseChoice.Discard);
            window.Close();
            await window.CloseDecisionForVerification;
            Dispatcher.UIThread.RunJobs();
        }
        double ReadSpeed() => EditorPackageRepository.Open(source).SelectedChart.Notes[0].ScrollSpeedMultiplier;
    }

    private static async Task WaitForSave(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_ioBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var i = 0; i < 1000 && (bool)field.GetValue(window)!; i++) await Task.Delay(10);
        if ((bool)field.GetValue(window)!) throw new TimeoutException("UI save did not complete");
    }
}
