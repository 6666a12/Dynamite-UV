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
        VerifyDockButton(window, check);
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
        VerifyContextHelp(window, check);
        VerifyPreviewLabels(window, check);
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

    private static void VerifyContextHelp(MainWindow window, Action<string, bool> check)
    {
        var text = WorkbenchLocalizer.Shared;
        var language = text.CurrentLanguage;
        var button = window.GetVisualDescendants().OfType<ContextHelpButton>()
            .Single(b => b.IsEffectivelyVisible && b.Text == text["Tools.GestureHint"]);
        try
        {
            check("Zero chart risks leave no permanent risk summary",
                !window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "ChartRiskSummary" && t.IsEffectivelyVisible));
            check("Placement help sits directly after its heading", button.Parent is StackPanel heading &&
                heading.Orientation == Avalonia.Layout.Orientation.Horizontal && heading.Children[0] is TextBlock title &&
                title.Text == text["Tools.Placement"]);
            var position = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(position);
            Dispatcher.UIThread.RunJobs();
            check("Mouse hover opens help without clicking", ToolTip.GetIsOpen(button));
            var tip = (ToolTip)ToolTip.GetTip(button)!;
            if (TopLevel.GetTopLevel(tip) is not null)
            {
                var corner = tip.PointToScreen(default);
                var anchor = button.PointToScreen(new Point(0, button.Bounds.Height));
                check("Compact tooltip aligns its top-left corner with the icon", corner.X == anchor.X &&
                    corner.Y == anchor.Y + 2 && tip.Bounds.Width is > 0 and <= 232);
            }
            else check("Compact tooltip aligns its top-left corner with the icon", false);
            foreach (var locale in new[] { "zh-CN", "en" })
            {
                text.SetLanguage(locale);
                Dispatcher.UIThread.RunJobs();
                check($"Context help body and accessible name update ({locale})",
                    tip.Content is TextBlock body && body.Text == text["Tools.GestureHint"] &&
                    Avalonia.Automation.AutomationProperties.GetName(button) == text["Help.Context"] &&
                    Avalonia.Automation.AutomationProperties.GetHelpText(button) == body.Text);
            }
            window.MouseMove(new Point(1, 1));
            Dispatcher.UIThread.RunJobs();
            check("Help hides on mouse exit", !ToolTip.GetIsOpen(button));
            button.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Dispatcher.UIThread.RunJobs();
            check("Keyboard opens placement help", ToolTip.GetIsOpen(button));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            check("Escape dismisses help without moving focus", !ToolTip.GetIsOpen(button) && button.IsFocused);
        }
        finally
        {
            ToolTip.SetIsOpen(button, false);
            text.SetLanguage(language);
        }
    }

    private static void VerifyPreviewLabels(MainWindow window, Action<string, bool> check)
    {
        var text = WorkbenchLocalizer.Shared;
        var language = text.CurrentLanguage;
        var settings = new PackageSettingsDialog(window.CurrentDocument!);
        settings.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var packLabel = settings.FindControl<TextBlock>("PackPreviewStartLabel")!;
            foreach (var locale in new[] { "zh-CN", "en" })
            {
                text.SetLanguage(locale);
                Dispatcher.UIThread.RunJobs();
                check($"Unset package preview is labelled without submitting a placeholder ({locale})",
                    packLabel.Text == text["Settings.PreviewStartUnset"] && settings.RequestForVerification().PackPreview is null &&
                    settings.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text["Settings.ChartPreviewStart"]));
            }
            settings.FindControl<NumericUpDown>("PackPreviewStartValue")!.Value = 0.5m;
            Dispatcher.UIThread.RunJobs();
            check("Explicit preview edit updates the label and submits the entered value",
                packLabel.Text == text["Settings.PreviewStart"] && settings.RequestForVerification().PackPreview?.StartSec == 0.5);
        }
        finally
        {
            settings.Close();
            text.SetLanguage(language);
        }
    }

    private static void VerifyDockButton(MainWindow window, Action<string, bool> check)
    {
        var dock = (Dock.Avalonia.Controls.DockControl)window.FindControl<Border>("ProjectDockHost")!.Child!;
        var layout = dock.Layout;
        var factory = window.LayoutFactory;
        var originals = new[] { "stage", "tools", "timing", "properties" }
            .Select(id => factory.Find(d => d.Id == id).Single()).ToArray();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            // Exercise the actual button -> MainWindow handler, not Factory.DockStage directly.
            Click(window, window.Stage.FindControl<Button>("DockButton")!);
            check($"Dock button while docked preserves entire workspace {cycle}",
                ReferenceEquals(dock.Layout, layout) && originals.All(item =>
                    factory.Find(d => d.Id == item.Id).SingleOrDefault() == item) &&
                ReferenceEquals(TopLevel.GetTopLevel(window.Stage), window));
        }
        window.ShortcutForVerification(Key.D, KeyModifiers.Control | KeyModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        check("Dock shortcut preserves layout and stage", ReferenceEquals(dock.Layout, layout) &&
            ReferenceEquals(TopLevel.GetTopLevel(window.Stage), window));
        for (var cycle = 0; cycle < 2; cycle++)
        {
            factory.FloatStage();
            Dispatcher.UIThread.RunJobs();
            var floating = TopLevel.GetTopLevel(window.Stage) as Window;
            if (floating is null || ReferenceEquals(floating, window))
                throw new InvalidOperationException("Stage did not float before Dock button regression");
            Click(floating, window.Stage.FindControl<Button>("DockButton")!);
            check($"Dock button from floating preserves workspace {cycle}",
                ReferenceEquals(dock.Layout, layout) && originals.All(item =>
                    factory.Find(d => d.Id == item.Id).SingleOrDefault() == item) &&
                ReferenceEquals(TopLevel.GetTopLevel(window.Stage), window));
        }
    }

    private static void VerifyLabels(MainWindow window, Action<string, bool> check)
    {
        // Hidden editors no longer have active selection presenters. Test the visible editors
        // in their real selection context, not an empty Inspector with controls mounted for testing.
        var previous = window.SelectedNoteId;
        window.SelectNoteForVerification(window.CurrentDocument!.SelectedChart.Notes.First().Id);
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var choices = window.GetVisualDescendants().OfType<ComboBox>()
            .Where(choice => choice.IsEffectivelyVisible && choice.Classes.Contains("localized")).ToArray();
        check("Language regression reaches the actual note type and track editors",
            choices.Any(choice => choice.Name == "NoteTypeChoice") && choices.Any(choice => choice.Name == "TrackChoice"));
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
            window.SelectNoteForVerification(previous ?? "");
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
