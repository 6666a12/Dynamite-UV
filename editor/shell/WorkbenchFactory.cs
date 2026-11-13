using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using SukiUI.ControlsAnimation;

namespace DynamiteUniverse.Editor.Shell;

public sealed class WorkbenchFactory : Factory
{
    private readonly WorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private readonly Document _stage;
    private readonly Document _placeholder = new() { Id = "stage-floating", CanClose = false };
    private readonly Tool _properties;
    private readonly Tool _toolset;
    private readonly Tool _timing;
    private readonly ToolDock _leftDock;
    private readonly ToolDock _rightDock;
    private readonly ProportionalDock _main;
    private Tool? _analysis;
    private bool _stageRestoreQueued;
    public bool IsShuttingDown { get; set; }

    public override bool OnWindowClosing(IDockWindow? window)
    {
        if (!IsShuttingDown && window?.Layout is { } layout && ContainsStage(layout))
        {
            // Do not destroy the floating HWND while it still owns Godot. Move the same
            // document back after this close callback; Dock will close the now-empty host.
            if (!_stageRestoreQueued)
            {
                _stageRestoreQueued = true;
                Dispatcher.UIThread.Post(() =>
                {
                    _stageRestoreQueued = false;
                    if (!IsShuttingDown) DockStage();
                });
            }
            return false;
        }
        return base.OnWindowClosing(window);
    }

    private bool ContainsStage(IDockable dockable) => ReferenceEquals(dockable, _stage) ||
        dockable is IDock dock && dock.VisibleDockables?.Any(ContainsStage) == true;

    public WorkbenchFactory(Control stage, Control inspector, Control tools, Control timing)
    {
        _stage = new Document { Id = "stage", Content = stage, CanClose = false };
        _properties = new Tool { Id = "properties", Content = inspector };
        _toolset = new Tool { Id = "tools", Content = tools };
        _timing = new Tool { Id = "timing", Content = timing };
        _leftDock = new ToolDock
        {
            Id = "left", Alignment = Alignment.Left, Proportion = .17,
            VisibleDockables = CreateList<IDockable>(_toolset, _timing), ActiveDockable = _toolset,
        };
        _stageDock = new DocumentDock
        {
            Id = "stage-dock", Proportion = .60,
            VisibleDockables = CreateList<IDockable>(_stage), ActiveDockable = _stage,
        };
        _rightDock = new ToolDock
        {
            Id = "right", Alignment = Alignment.Right, Proportion = .23,
            VisibleDockables = CreateList<IDockable>(_properties), ActiveDockable = _properties,
        };
        _main = new ProportionalDock
        {
            Id = "main", Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(_leftDock,
                new ProportionalDockSplitter { CanResize = true }, _stageDock,
                new ProportionalDockSplitter { CanResize = true }, _rightDock),
        };
        _root = new RootDock
        {
            Id = "root", VisibleDockables = CreateList<IDockable>(_main),
            // Native Godot child windows cannot be covered by an Avalonia overlay.
            // Reserve layout space for auto-hidden tools instead of covering the stage.
            PinnedDockDisplayMode = PinnedDockDisplayMode.Inline,
            ActiveDockable = _main, DefaultDockable = _main,
        };
        _text.LanguageChanged += UpdateText;
        UpdateText();
    }

    private readonly DocumentDock _stageDock;
    private readonly IRootDock _root;
    public override IRootDock CreateLayout() => _root;

    internal WorkbenchLayoutSnapshot CaptureLayout() => WorkbenchLayoutSnapshot.Capture(_root);
    internal void RestoreLayoutBeforeAttach(WorkbenchLayoutSnapshot snapshot)
    {
        snapshot.Validate();
        if (snapshot.Root.AllChildren().Any(HasAnalysis) || snapshot.Windows.Any(w => HasAnalysis(w.Root))) AddAnalysisPanel();
        IDockable? Resolve(string id) => id switch
        {
            "root" => _root, "main" => _main, "left" => _leftDock, "right" => _rightDock,
            "stage-dock" => _stageDock, "stage" => _stage, "stage-floating" => _placeholder,
            "tools" => _toolset, "properties" => _properties, "timing" => _timing, "audio-analysis" => _analysis, _ => null,
        };
        WorkbenchLayoutRestore.Apply(snapshot, this, Resolve);
        // Closed tools absent from the serialized tree must not retain stale default ownership.
        foreach (var tool in new[] { _toolset, _properties, _timing }) tool.Owner = null;
    }
    private static bool HasAnalysis(LayoutItem item) => item.Id == "audio-analysis" || item.AllChildren().Any(HasAnalysis);

    public void FloatStage()
    {
        if (_stage.Owner is not null && !ReferenceEquals(_stage.Owner, _stageDock)) return;
        if (_stageDock.VisibleDockables?.Contains(_placeholder) != true) AddDockable(_stageDock, _placeholder);
        FloatDockable(_stage);
        if (_root.Windows?.LastOrDefault() is { } window)
        {
            window.Host?.SetSize(960, 650);
            window.Host?.SetPosition(60, 80);
        }
    }

    public void DockStage()
    {
        AttachDock(_stageDock);
        if (_stage.Owner is IDock source && !ReferenceEquals(source, _stageDock))
            MoveDockable(source, _stageDock, _stage, null);
        else if (_stageDock.VisibleDockables?.Contains(_stage) != true)
            AddDockable(_stageDock, _stage);
        if (_stageDock.VisibleDockables?.Contains(_placeholder) == true)
            RemoveDockable(_placeholder, false);
        _stageDock.ActiveDockable = _stage;
    }

    public void RestoreWorkspace()
    {
        DockStage();
        RestoreTool(_toolset, _leftDock);
        RestoreTool(_timing, _leftDock);
        RestoreTool(_properties, _rightDock);
        if (_analysis is not null) RestoreTool(_analysis, _leftDock);
        _leftDock.ActiveDockable = _toolset;
        _rightDock.ActiveDockable = _properties;
        _leftDock.Proportion = .17;
        _stageDock.Proportion = .60;
        _rightDock.Proportion = .23;
    }

    private void RestoreTool(Tool tool, ToolDock destination)
    {
        UnpinDockable(tool);
        RestoreDockable(tool);
        AttachDock(destination);
        if (destination.VisibleDockables?.Contains(tool) != true)
        {
            if (tool.Owner is IDock owner && owner.VisibleDockables?.Contains(tool) == true)
                MoveDockable(owner, destination, tool, null);
            else AddDockable(destination, tool);
        }
        destination.ActiveDockable = tool;
    }

    private void AttachDock(IDock destination)
    {
        if (_main.VisibleDockables?.Contains(destination) == true) return;
        // Detach first: assigning Owner alone leaves a second parent collection behind.
        if (destination.Owner is IDock owner && owner.VisibleDockables?.Contains(destination) == true)
            RemoveDockable(destination, false);
        if (_root.VisibleDockables?.Contains(_main) != true)
            AddDockable(_root, _main);
        var children = _main.VisibleDockables ??= CreateList<IDockable>();
        var index = ReferenceEquals(destination, _leftDock) ? 0 :
            ReferenceEquals(destination, _stageDock) && children.Contains(_rightDock)
                ? children.IndexOf(_rightDock) : children.Count;
        children.Insert(index, destination);
        if (children.Count > 1)
        {
            var splitter = new ProportionalDockSplitter { CanResize = true };
            children.Insert(index < children.Count - 1 ? index + 1 : index, splitter);
            InitDockable(splitter, _main);
        }
        InitDockable(destination, _main);
        _root.ActiveDockable = _main;
    }

    public void AddAnalysisPanel()
    {
        if (_analysis is not null)
        {
            RestoreTool(_analysis, _leftDock);
            return;
        }
        var play = new Ursa.Controls.IconButton { Classes = { "active" } };
        play.Bind(ContentControl.ContentProperty, Translation("Dock.RunAnalysis"));
        SukiPressMotion.SetEnable(play, true);
        var heading = new TextBlock { Classes = { "section" } };
        heading.Bind(TextBlock.TextProperty, Translation("Dock.AudioHeading"));
        var description = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "caption" } };
        description.Bind(TextBlock.TextProperty, Translation("Dock.AudioDescription"));
        _analysis = new Tool
        {
            Id = "audio-analysis",
            Content = new Border { Padding = new Avalonia.Thickness(18), Child = new StackPanel
            {
                Spacing = 12,
                Children = { heading, description, play },
            } },
        };
        UpdateText();
        RestoreTool(_analysis, _leftDock);
    }

    private void UpdateText()
    {
        _stage.Title = _text["Dock.Stage"];
        _properties.Title = _text["Dock.Properties"];
        _toolset.Title = _text["Dock.Tools"];
        _timing.Title = _text["Dock.Timing"];
        _leftDock.Title = _text["Dock.Left"];
        _stageDock.Title = _text["Dock.Chart"];
        _rightDock.Title = _text["Dock.Right"];
        _root.Title = _text["Dock.Root"];
        _placeholder.Title = _text["Dock.FloatingTitle"];
        _placeholder.Content = new TextBlock { Text = _text["Dock.FloatingHint"], Margin = new Avalonia.Thickness(18) };
        if (_analysis is not null) _analysis.Title = _text["Dock.AudioTitle"];
    }

    public void Release() => _text.LanguageChanged -= UpdateText;

    private Binding Translation(string key) => _text.CreateBinding(key);
}
