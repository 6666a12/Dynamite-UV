using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Workspace lifecycle regression, separate from chart-command assertions.</summary>
internal static class WorkspaceVerification
{
    internal static void Run(MainWindow window, Action<string, bool> check)
    {
        var factory = window.LayoutFactory;
        var root = factory.CreateLayout();
        check("Pinned previews reserve space for native stage", root.PinnedDockDisplayMode == PinnedDockDisplayMode.Inline);
        foreach (var id in new[] { "tools", "timing", "properties" })
        {
            var tool = (Dock.Model.Avalonia.Controls.Tool)factory.Find(d => d.Id == id).Single();
            for (var cycle = 0; cycle < 2; cycle++)
            {
                factory.PinDockable(tool);
                factory.PreviewPinnedDockable(tool);
                Dispatcher.UIThread.RunJobs();
                check($"{id} auto-hide preview opens {cycle + 1}", root.PinnedDock?.ActiveDockable == tool);
                factory.HidePreviewingDockables(root);
                factory.PreviewPinnedDockable(tool);
                Dispatcher.UIThread.RunJobs();
                check($"{id} auto-hide preview reopens {cycle + 1}", root.PinnedDock?.ActiveDockable == tool);
                factory.RestoreWorkspace();
                Dispatcher.UIThread.RunJobs();
                check($"{id} restored after auto-hide {cycle + 1}",
                    tool.Owner is IDock owner && owner.VisibleDockables?.Contains(tool) == true &&
                    !factory.IsDockablePinned(tool));
                factory.CloseDockable(tool);
                factory.RestoreWorkspace();
                Dispatcher.UIThread.RunJobs();
                check($"{id} restored after close {cycle + 1}",
                    tool.Owner is IDock dock && dock.VisibleDockables?.Contains(tool) == true &&
                    ((Control)tool.Content!).IsEffectivelyVisible);
            }
        }
        factory.AddAnalysisPanel();
        var analysis = factory.Find(d => d.Id == "audio-analysis").Single();
        factory.CloseDockable(analysis);
        factory.AddAnalysisPanel();
        Dispatcher.UIThread.RunJobs();
        check("Add panel reopens closed analysis", analysis.Owner is IDock ownerDock &&
            ownerDock.VisibleDockables?.Contains(analysis) == true && ownerDock.ActiveDockable == analysis);
        factory.RestoreWorkspace();
        factory.RestoreWorkspace();
        Dispatcher.UIThread.RunJobs();
        check("Repeated workspace restore keeps single stage", factory.Find(d => d.Id == "stage").Count() == 1);

        // Actual headless mouse/key input (not ClickEvent or direct command invocation).
        var help = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content?.ToString() == WorkbenchLocalizer.Shared["Menu.Help"]);
        var position = help.TranslatePoint(new Point(help.Bounds.Width / 2, help.Bounds.Height / 2), window)!.Value;
        window.MouseDown(position, MouseButton.Left);
        window.MouseUp(position, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        check("Mouse opens Help overlay", window.ShortcutsVisible);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        check("Keyboard Escape dismisses Help overlay", !window.ShortcutsVisible);
        foreach (var key in new[] { "Menu.File", "Menu.Edit" })
        {
            var button = window.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Content?.ToString() == WorkbenchLocalizer.Shared[key]);
            Click(window, button);
            check($"Mouse opens {key} flyout", button.Flyout?.IsOpen == true);
            button.Flyout?.Hide();
        }
        foreach (var toolName in new[] { "PLACE", "PATH", "PAN", "SELECT" })
        {
            var button = window.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Tag?.ToString() == toolName && b.IsEffectivelyVisible);
            Click(window, button);
            check($"Mouse selects {toolName} tool", window.CurrentTool == toolName);
        }
        VerifyLabels(window, check);
        var dialog = new CreateChartDialog();
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Click(dialog, dialog.FindControl<Button>("ConfirmButton")!);
            check("Invalid create stays open and displays error", dialog.IsVisible && dialog.Result is null &&
                dialog.FindControl<TextBlock>("ErrorText")!.IsVisible);
            var choice = dialog.FindControl<ComboBox>("DifficultyChoice")!;
            choice.SelectedIndex = 6;
            check("Custom difficulty reveals key input", dialog.FindControl<StackPanel>("CustomKeyField")!.IsVisible);
            choice.SelectedIndex = 2;
            check("Standard difficulty hides custom key input", !dialog.FindControl<StackPanel>("CustomKeyField")!.IsVisible);
            var cancel = dialog.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Content?.ToString() == WorkbenchLocalizer.Shared["Create.Cancel"]);
            Click(dialog, cancel);
            check("Mouse cancels create without result", !dialog.IsVisible && dialog.Result is null);
        }
        finally { dialog.Close(); }
    }

    private static void VerifyLabels(MainWindow window, Action<string, bool> check)
    {
        var choices = window.GetVisualDescendants().OfType<ComboBox>()
            .Where(choice => choice.Classes.Contains("localized")).ToArray();
        var originals = choices.Select(choice => choice.SelectedIndex).ToArray();
        // Pending, unapplied choices must survive a language switch too.
        foreach (var choice in choices) choice.SelectedIndex = Math.Min(1, choice.ItemCount - 1);
        var selection = choices.Select(choice => choice.SelectedIndex).ToArray();
        var changes = 0;
        void Changed(object? sender, SelectionChangedEventArgs args) => changes++;
        foreach (var choice in choices) choice.SelectionChanged += Changed;
        var text = WorkbenchLocalizer.Shared;
        var original = text.CurrentLanguage;
        try
        {
            foreach (var language in new[] { "zh-CN", "en", "zh-CN" })
            {
                text.SetLanguage(language);
                Dispatcher.UIThread.RunJobs();
                foreach (var choice in choices)
                {
                    if (choice.SelectedItem is not ComboBoxItem item) continue;
                    var label = item.Content?.ToString();
                    check($"Selected label refreshes immediately {language} / {choice.Name}",
                        choice.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == label));
                }
            }
            check("Language switch preserves option selection without edit events", changes == 0 &&
                choices.Select(choice => choice.SelectedIndex).SequenceEqual(selection));
        }
        finally
        {
            foreach (var choice in choices) choice.SelectionChanged -= Changed;
            for (var i = 0; i < choices.Length; i++) choices[i].SelectedIndex = originals[i];
            text.SetLanguage(original);
            Dispatcher.UIThread.RunJobs();
        }
        foreach (var chrome in window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.ToolChromeControl>())
        {
            var active = chrome.IsActive;
            try
            {
                foreach (var state in new[] { false, true, false })
                {
                    chrome.SetCurrentValue(Dock.Avalonia.Controls.ToolChromeControl.IsActiveProperty, state);
                    Dispatcher.UIThread.RunJobs();
                    var title = chrome.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
                    var grip = title.GetVisualAncestors().OfType<Grid>().Single(g => g.Name == "PART_Grip");
                    var background = grip.Background as Avalonia.Media.ISolidColorBrush;
                    var foreground = title.Foreground as Avalonia.Media.ISolidColorBrush;
                    check($"Dock title remains readable active={state} / {title.Text}",
                        !string.IsNullOrWhiteSpace(title.Text) && background is not null && foreground is not null &&
                        background.Color == Avalonia.Media.Color.Parse(state ? "#14273D" : "#0E1528") &&
                        foreground.Color != background.Color);
                }
            }
            finally { chrome.SetCurrentValue(Dock.Avalonia.Controls.ToolChromeControl.IsActiveProperty, active); }
        }
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
