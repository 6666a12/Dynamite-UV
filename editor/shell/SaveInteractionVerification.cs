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
            resume.Set();
            await WaitForSave(window);
            interceptor.SetValue(null, null);
            check("Save snapshot does not mark concurrent edit saved", ReadSpeed() == 1.8 &&
                document.SelectedChart.Notes[0].ScrollSpeedMultiplier == 1.9 && document.HasUnsavedChanges);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForSave(window);
            check("Second save publishes edits made during first save", ReadSpeed() == 1.9 && !document.HasUnsavedChanges);
        }
        finally
        {
            resume.Set();
            interceptor.SetValue(null, null);
            window.Close();
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
