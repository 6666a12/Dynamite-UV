using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Exercises the actual toolbar, pointer/keyboard routing, detached Dock host and delayed IPC.</summary>
internal static class WorkbenchModalVerification
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var pack = Path.Combine(root, "pack");
        Verification.CreatePackage(pack);
        var window = new MainWindow(Path.Combine(root, "preferences"));
        try
        {
            window.Show();
            await window.OpenPackageForVerification(pack);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var doc = window.CurrentDocument!;
            var add = window.GetVisualDescendants().OfType<Button>()
                .Single(b => Equals(b.Content, WorkbenchLocalizer.Shared["File.AddChartShort"]));
            var layer = window.FindControl<Border>("WorkbenchModalLayer")!;
            var body = window.FindControl<Grid>("WorkbenchBody")!;
            var slot = window.FindControl<ContentControl>("WorkbenchModalSlot")!;
            void Click(Control c)
            {
                var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
                window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }
            void Press(Key key, RawInputModifiers mods, PhysicalKey physical)
            {
                window.KeyPress(key, mods, physical, null); window.KeyRelease(key, mods, physical, null);
                Dispatcher.UIThread.RunJobs();
            }
            void Place(long epoch) => window.StageEventForVerification("place",
                $$"""{"type":0,"track":1,"bar":2,"center":2.5,"width":1,"tailBar":2,"workbenchEpoch":{{epoch}}}""");
            window.LayoutFactory.FloatStage();
            Dispatcher.UIThread.RunJobs();
            var floating = TopLevel.GetTopLevel(window.Stage) as Window;
            if (floating == null || floating == window) throw new InvalidOperationException("Fixture stage did not float");
            var version = doc.ChangeVersion;
            window.Activate(); Click(add);
            check("Toolbar opens a single sheet inside the main window; both workspaces lock", layer.IsVisible &&
                slot.Content is AddDifficultyForm && !body.IsEnabled && !floating.IsEnabled);
            var sheet = slot.Content;
            floating.Close(); Dispatcher.UIThread.RunJobs();
            check("A floating stage cannot close or silently dock behind a modal sheet", floating.IsVisible && TopLevel.GetTopLevel(window.Stage) == floating);
            Click(add); Place(0); Place(1);
            Press(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
            Press(Key.S, RawInputModifiers.Control, PhysicalKey.S);
            Press(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete);
            check("Background click, shortcuts and native input cannot modify the document", ReferenceEquals(slot.Content, sheet) && doc.ChangeVersion == version);
            Press(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab);
            check("Keyboard focus stays in the sheet", window.FocusManager?.GetFocusedElement() is Control focused && focused.GetVisualAncestors().Contains(slot));
            Press(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
            check("Escape dismisses without editing and restores the original floating stage", !layer.IsVisible && body.IsEnabled && floating.IsEnabled &&
                TopLevel.GetTopLevel(window.Stage) == floating && doc.ChangeVersion == version);
            Place(0); Place(1);
            check("Queued input from before/during the sheet cannot edit after dismissal", doc.ChangeVersion == version);
            Click(add); window.Close(); Dispatcher.UIThread.RunJobs();
            check("Closing a window with a sheet discards only the uncommitted form", window.IsVisible && !layer.IsVisible && doc.ChangeVersion == version);
            Place(4);
            check("New input is accepted again after closing the form", doc.ChangeVersion > version);
            var original = doc.SelectedChartId;
            var count = doc.Charts.Count;
            Click(add);
            var submitted = (AddDifficultyForm)slot.Content!;
            await submitted.SubmitAsync();
            check("Successful sheet commit unlocks both workspaces and selects exactly one empty chart", submitted.Added &&
                !layer.IsVisible && body.IsEnabled && floating.IsEnabled && doc.Charts.Count == count + 1 &&
                doc.SelectedChartId != original && doc.SelectedChart.Notes.Count == 0);
            Press(Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
            check("One workbench undo removes the new difficulty and restores its reference", doc.Charts.Count == count && doc.SelectedChartId == original);
            window.LayoutFactory.DockStage();
        }
        finally
        {
            window.ClosePromptForVerification = () => Task.FromResult(UnsavedCloseChoice.Discard);
            window.Close(); await window.CloseDecisionForVerification;
            Dispatcher.UIThread.RunJobs();
        }
    }
}
