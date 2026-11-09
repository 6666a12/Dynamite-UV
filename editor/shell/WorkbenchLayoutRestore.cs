using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Rehydrates data before Dock is attached, reusing the factory's live panel instances.</summary>
internal static class WorkbenchLayoutRestore
{
    public static void Apply(WorkbenchLayoutSnapshot snapshot, WorkbenchFactory factory,
        Func<string, IDockable?> resolve)
    {
        snapshot.Validate();
        var reused = new HashSet<IDockable>();
        IDockable Build(LayoutItem item)
        {
            var existing = resolve(item.Id);
            if (existing is not null && !reused.Add(existing)) existing = null;
            IDockable result = item.Kind switch
            {
                "leaf" => existing ?? throw new InvalidDataException("Missing panel instance."),
                "root" => existing as IRootDock ?? new RootDock(),
                "proportional" => existing as IProportionalDock ?? new ProportionalDock(),
                "tool" => existing as IToolDock ?? new ToolDock(),
                "document" => existing as IDocumentDock ?? new DocumentDock(),
                "split" => new ProportionalDockSplitter { CanResize = true },
                _ => throw new InvalidDataException("Unknown container."),
            };
            result.Id = item.Id;
            result.Proportion = item.Proportion;
            if (result is IProportionalDock proportional) proportional.Orientation = (Orientation)item.Orientation;
            if (result is IToolDock tool) tool.Alignment = (Alignment)item.Alignment;
            if (result is IDock dock)
            {
                dock.VisibleDockables = factory.CreateList(item.Children.Select(Build).ToArray());
                dock.ActiveDockable = dock.VisibleDockables.FirstOrDefault(d => d.Id == item.Active) ?? dock.VisibleDockables.FirstOrDefault();
                dock.DefaultDockable = dock.ActiveDockable;
            }
            if (result is IRootDock root)
            {
                root.PinnedDockDisplayMode = PinnedDockDisplayMode.Inline;
                root.HiddenDockables = factory.CreateList(item.Hidden.Select(Build).ToArray());
                root.LeftPinnedDockables = factory.CreateList(item.Left.Select(Build).ToArray());
                root.RightPinnedDockables = factory.CreateList(item.Right.Select(Build).ToArray());
                root.TopPinnedDockables = factory.CreateList(item.Top.Select(Build).ToArray());
                root.BottomPinnedDockables = factory.CreateList(item.Bottom.Select(Build).ToArray());
            }
            return result;
        }
        var root = (IRootDock)Build(snapshot.Root);
        root.Windows = factory.CreateList<IDockWindow>();
        foreach (var saved in snapshot.Windows)
        {
            var window = factory.CreateDockWindow();
            window.X = saved.X; window.Y = saved.Y; window.Width = saved.Width; window.Height = saved.Height;
            window.Layout = (IRootDock)Build(saved.Root);
            root.Windows.Add(window);
        }
    }
}
