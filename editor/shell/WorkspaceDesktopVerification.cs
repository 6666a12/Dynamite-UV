using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Model.Core;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Real pointer input across dock chrome, pinned tabs, menus and tool controls.</summary>
internal static class WorkspaceDesktopVerification
{
    internal static async Task Run(MainWindow window, Func<Window, int, int, Task> click,
        Action<string> report)
    {
        async Task Click(Control control)
        {
            if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0)
                throw new InvalidOperationException($"Control not visible: {control.Name}");
            var p = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
            await click(window, p.X, p.Y);
        }
        Button Button(string key) => window.GetVisualDescendants().OfType<Button>()
            .First(b => b.IsEffectivelyVisible && b.Content?.ToString() == WorkbenchLocalizer.Shared[key]);
        var factory = window.LayoutFactory;
        var root = factory.CreateLayout();
        var child = window.Stage.Host.Diagnostics().Child;
        var dockControl = window.FindControl<Dock.Avalonia.Controls.DockControl>("Dock")!;
        var layout = dockControl.Layout;
        var originals = new[] { "stage", "tools", "timing", "properties" }
            .Select(id => factory.Find(d => d.Id == id).Single()).ToArray();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            if (cycle > 0)
            {
                factory.FloatStage();
                await Task.Delay(700);
            }
            var top = (Window)TopLevel.GetTopLevel(window.Stage)!;
            var button = window.Stage.FindControl<Button>("DockButton")!;
            var point = button.PointToScreen(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2));
            await click(top, point.X, point.Y);
            await Task.Delay(700);
            var diagnostic = window.Stage.Host.Diagnostics();
            if (!ReferenceEquals(dockControl.Layout, layout) ||
                originals.Any(item => factory.Find(d => d.Id == item.Id).SingleOrDefault() != item) ||
                !ReferenceEquals(TopLevel.GetTopLevel(window.Stage), window) ||
                !diagnostic.Embedded || diagnostic.Child != child)
                throw new InvalidOperationException($"Real Dock button destroyed workspace cycle {cycle}");
        }
        report("PASS real Dock button while docked and twice from floating preserves all panels and native HWND");
        foreach (var id in new[] { "tools", "timing", "properties" })
        {
            var tool = factory.Find(d => d.Id == id).Single();
            if (tool.Owner is IDock owner) owner.ActiveDockable = tool;
            await Task.Delay(200);
            for (var cycle = 0; cycle < 2; cycle++)
            {
                var pin = window.GetVisualDescendants().OfType<Button>()
                    .SingleOrDefault(b => b.Name == "PART_PinButton" && ReferenceEquals(b.CommandParameter, tool) && b.IsEffectivelyVisible)
                    ?? throw new InvalidOperationException("Pin button missing: " + string.Join("; ", window.GetVisualDescendants().OfType<Button>()
                        .Where(b => b.IsEffectivelyVisible).Select(b => $"{b.Name}/{b.DataContext?.GetType().Name}/{(b.DataContext as IDockable)?.Id}/{b.CommandParameter}")));
                await Click(pin);
                if (!factory.IsDockablePinned(tool)) throw new InvalidOperationException($"{id}: pin click failed point={pin.PointToScreen(new Point(pin.Bounds.Width / 2, pin.Bounds.Height / 2))} bounds={pin.Bounds} enabled={pin.IsEnabled} owner={tool.Owner?.Id} pinned={string.Join(',', root.LeftPinnedDockables?.Select(x => x.Id) ?? [])} command={pin.Command?.CanExecute(pin.CommandParameter)}");
                var candidates = window.GetVisualDescendants().OfType<Button>()
                    .Where(b => b.IsEffectivelyVisible && b.GetVisualAncestors()
                        .Any(a => a.GetType().Name == "ToolPinItemControl")).ToArray();
                var tab = candidates.SingleOrDefault(b => ReferenceEquals(b.CommandParameter, tool) || ReferenceEquals(b.DataContext, tool))
                    ?? throw new InvalidOperationException("Pinned tab missing: " + string.Join("; ",
                        window.GetVisualDescendants().Where(c => c.GetType().Name.Contains("Pin"))
                            .Select(c => $"{c.GetType().Name}/{(c as Control)?.DataContext}/{c.IsEffectivelyVisible}/{c.Bounds}")));
                // Hover can already open the preview; close it so the click assertion is deterministic.
                factory.HidePreviewingDockables(root);
                await Click(tab);
                if (root.PinnedDock?.ActiveDockable != tool)
                    throw new InvalidOperationException($"{id}: pinned tab did not open");
                var content = (Control)((Dock.Model.Avalonia.Controls.Tool)tool).Content!;
                var a = content.TranslatePoint(default, window)!.Value;
                var b = window.Stage.Host.TranslatePoint(default, window)!.Value;
                if (new Rect(a, content.Bounds.Size).Intersects(new Rect(b, window.Stage.Host.Bounds.Size)))
                    throw new InvalidOperationException($"{id}: expanded panel overlaps native stage");
                var unpin = window.GetVisualDescendants().OfType<Button>()
                    .Single(x => x.Name == "PART_PinButton" && ReferenceEquals(x.CommandParameter, tool) && x.IsEffectivelyVisible);
                await Click(unpin);
                if (factory.IsDockablePinned(tool)) throw new InvalidOperationException($"{id}: unpin click failed");
            }
            report($"PASS native {id} collapse / expand / pin twice, no stage overlap");
        }
        await Click(Button("Menu.Help"));
        if (!window.ShortcutsVisible) throw new InvalidOperationException("Help mouse click failed");
        await Click(Button("Help.Close"));
        if (window.ShortcutsVisible) throw new InvalidOperationException("Help close mouse click failed");
        foreach (var key in new[] { "Menu.File", "Menu.Edit" })
        {
            var menu = Button(key);
            await Click(menu);
            if (menu.Flyout?.IsOpen != true) throw new InvalidOperationException($"{key} flyout did not open");
            menu.Flyout.Hide();
        }
        await Click(Button("Menu.View"));
        if (!window.Stage.Host.Diagnostics().Embedded || window.Stage.Host.Diagnostics().Child != child)
            throw new InvalidOperationException("Workspace recovery lost the original native stage");
        report("PASS native Help open/close, File/Edit flyouts, workspace recovery retain stage");
    }
}
