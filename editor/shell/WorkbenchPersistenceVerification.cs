using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

internal static class WorkbenchPersistenceVerification
{
    internal static void Run(string directory, Action<string, bool> check)
    {
        Directory.CreateDirectory(directory);
        var store = new WorkbenchUserStore(directory);
        var preferences = new WorkbenchPreferences { Language = "zh-CN", Grid = 5, PlacementWidth = 2.35,
            ObjectSnap = true, PositionSnap = false, WheelReversed = true, Volume = 37, Effects = "Reduced", Motion = 1 };
        check("Rounded decimal upper bound is rejected", !(preferences with { PlacementWidth = (double)decimal.MaxValue }).IsValid);
        check("Largest convertible double width is accepted", (preferences with { PlacementWidth = Math.BitDecrement((double)decimal.MaxValue) }).IsValid);
        var toolsView = new ToolsView();
        toolsView.RestorePreferences(preferences);
        check("Suspended object magnet ignores saved opt-in", !toolsView.ObjectSnapEnabled);
        check("Object magnet control is absent", toolsView.FindControl<CheckBox>("ObjectSnapChoice") is null);
        check("Position mode still restores independently", !toolsView.PositionSnapEnabled);
        check("Preferences save atomically", store.Save(WorkbenchPreferences.FileName, preferences));
        check("Preferences round trip", new WorkbenchUserStore(directory).Load<WorkbenchPreferences>(WorkbenchPreferences.FileName) == preferences);
        File.WriteAllText(Path.Combine(directory, WorkbenchPreferences.FileName), "{broken");
        var damaged = new WorkbenchUserStore(directory);
        check("Malformed preferences recover", damaged.Load<WorkbenchPreferences>(WorkbenchPreferences.FileName) is null);
        damaged.Save(WorkbenchPreferences.FileName, preferences);
        check("Malformed bytes preserved", Directory.GetFiles(directory, "preferences.json.recovery-*").Any(p => File.ReadAllText(p) == "{broken"));
        var window = new MainWindow(directory);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        check("Preferences restored before interaction", window.SnapDenominator == 5 && window.WheelReversed &&
            window.FindControl<ComboBox>("LanguageChoice")!.SelectedIndex == 1);
        var layout = window.LayoutFactory.CreateLayout();
        var original = window.Stage;
        window.LayoutFactory.FloatStage();
        Dispatcher.UIThread.RunJobs();
        var snapshot = window.LayoutFactory.CaptureLayout();
        snapshot.Validate();
        check("Floating layout captures separate window", snapshot.Windows.Count == 1);
        window.Close();
        Dispatcher.UIThread.RunJobs();
        var reopened = new MainWindow(directory);
        reopened.Show();
        Dispatcher.UIThread.RunJobs();
        check("Floating layout restores", !ReferenceEquals(TopLevel.GetTopLevel(reopened.Stage), reopened));
        var root = reopened.LayoutFactory.CreateLayout();
        reopened.LayoutFactory.RestoreWorkspace();
        Dispatcher.UIThread.RunJobs();
        check("Restored layout resets without destroying root", ReferenceEquals(root, reopened.LayoutFactory.CreateLayout()) &&
            ReferenceEquals(TopLevel.GetTopLevel(reopened.Stage), reopened));
        reopened.Close();
        Dispatcher.UIThread.RunJobs();
        var pinnedWindow = new MainWindow(directory);
        pinnedWindow.Show();
        Dispatcher.UIThread.RunJobs();
        var pinnedRoot = pinnedWindow.LayoutFactory.CreateLayout();
        IDockable Find(IDockable node, string id) => node.Id == id ? node :
            ((node as IDock)?.VisibleDockables ?? Array.Empty<IDockable>()).Select(n =>
            { try { return Find(n, id); } catch (InvalidOperationException) { return null; } }).FirstOrDefault(n => n is not null)
            ?? throw new InvalidOperationException(id);
        var tools = Find(pinnedRoot, "tools");
        pinnedWindow.LayoutFactory.PinDockable(tools);
        Dispatcher.UIThread.RunJobs();
        pinnedWindow.Close();
        Dispatcher.UIThread.RunJobs();
        var pinnedReload = new MainWindow(directory);
        pinnedReload.Show();
        Dispatcher.UIThread.RunJobs();
        check("Pinned tool layout survives restart", pinnedReload.LayoutFactory.CreateLayout().LeftPinnedDockables?.Any(d => d.Id == "tools") == true);
        pinnedReload.Close();
        Dispatcher.UIThread.RunJobs();
        OrdinaryDockRecovery(Path.Combine(directory, "ordinary-dock"), check);
        WorkbenchLocalizer.Shared.SetLanguage("en");
    }

    private static void OrdinaryDockRecovery(string directory, Action<string, bool> check)
    {
        var window = new MainWindow(directory);
        var factory = window.LayoutFactory;
        IDockable Find(IDockable node, string id) => node.Id == id ? node :
            ((node as IDock)?.VisibleDockables ?? Array.Empty<IDockable>()).Select(n =>
            { try { return Find(n, id); } catch (InvalidOperationException) { return null; } }).FirstOrDefault(n => n is not null)
            ?? throw new InvalidOperationException(id);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var root = factory.CreateLayout();
            var stage = Find(root, "stage");
            var main = (IDock)Find(root, "main");
            var left = Find(root, "left");
            var tools = Find(root, "tools");
            var timing = Find(root, "timing");
            factory.FloatDockable(stage);
            Dispatcher.UIThread.RunJobs();
            factory.DockStage();
            Dispatcher.UIThread.RunJobs();
            check("Ordinary stage float returns to visible main layout", ReferenceEquals(Find(root, "stage"), stage) &&
                ReferenceEquals(TopLevel.GetTopLevel(window.Stage), window));
            factory.CloseDockable(tools);
            factory.CloseDockable(timing);
            factory.AddAnalysisPanel();
            check("First analysis panel restores collapsed left dock", Find(root, "audio-analysis").Owner == left);
            var nested = new Dock.Model.Avalonia.Controls.ProportionalDock
                { Id = "verification-nested", VisibleDockables = factory.CreateList<IDockable>() };
            factory.AddDockable(main, nested);
            factory.MoveDockable(main, nested, left, null);
            factory.RestoreWorkspace();
            check("Workspace recovery detaches previous dock parent", main.VisibleDockables!.Contains(left) &&
                !nested.VisibleDockables!.Contains(left) && ReferenceEquals(left.Owner, main));
            factory.RestoreWorkspace();
            check("Workspace recovery is idempotent", main.VisibleDockables.Count(d => ReferenceEquals(d, left)) == 1);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
}
