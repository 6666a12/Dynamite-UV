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
            PositionSnap = false, WheelReversed = true, Volume = 37, Effects = "Reduced", Motion = 1 };
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
        WorkbenchLocalizer.Shared.SetLanguage("en");
    }
}
