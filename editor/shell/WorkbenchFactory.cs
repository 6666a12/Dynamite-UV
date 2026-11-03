using Avalonia.Controls;
using Avalonia.Data;
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
    private readonly ToolDock _leftDock;
    private readonly ToolDock _rightDock;
    private readonly ProportionalDock _main;
    private Tool? _analysis;

    public WorkbenchFactory(Control stage, Control inspector, Control tools)
    {
        _stage = new Document { Id = "stage", Content = stage, CanClose = false };
        _properties = new Tool { Id = "properties", Content = inspector };
        _toolset = new Tool { Id = "tools", Content = tools };
        _leftDock = new ToolDock
        {
            Id = "left", Alignment = Alignment.Left, Proportion = .17,
            VisibleDockables = CreateList<IDockable>(_toolset), ActiveDockable = _toolset,
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
            ActiveDockable = _main, DefaultDockable = _main,
        };
        _text.LanguageChanged += UpdateText;
        UpdateText();
    }

    private readonly DocumentDock _stageDock;
    private readonly IRootDock _root;
    public override IRootDock CreateLayout() => _root;

    public void FloatStage()
    {
        if (_placeholder.Owner is null) AddDockable(_stageDock, _placeholder);
        FloatDockable(_stage);
        if (_root.Windows?.LastOrDefault() is { } window)
        {
            window.Host?.SetSize(960, 650);
            window.Host?.SetPosition(60, 80);
        }
    }

    public void DockStage()
    {
        if (_stage.Owner is IDock source && !ReferenceEquals(source, _stageDock))
            MoveDockable(source, _stageDock, _stage, null);
        if (_placeholder.Owner is not null)
            RemoveDockable(_placeholder, false);
        _stageDock.ActiveDockable = _stage;
    }

    public void AddAnalysisPanel()
    {
        if (_analysis is not null)
        {
            _leftDock.ActiveDockable = _analysis;
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
            Id = "audio-analysis", Owner = _leftDock,
            Content = new Border { Padding = new Avalonia.Thickness(18), Child = new StackPanel
            {
                Spacing = 12,
                Children = { heading, description, play },
            } },
        };
        UpdateText();
        _leftDock.VisibleDockables!.Add(_analysis);
        _leftDock.ActiveDockable = _analysis;
    }

    private void UpdateText()
    {
        _stage.Title = _text["Dock.Stage"];
        _properties.Title = _text["Dock.Properties"];
        _toolset.Title = _text["Dock.Tools"];
        _leftDock.Title = _text["Dock.Left"];
        _stageDock.Title = _text["Dock.Chart"];
        _rightDock.Title = _text["Dock.Right"];
        _root.Title = _text["Dock.Root"];
        _placeholder.Title = _text["Dock.FloatingTitle"];
        _placeholder.Content = new TextBlock { Text = _text["Dock.FloatingHint"], Margin = new Avalonia.Thickness(18) };
        if (_analysis is not null) _analysis.Title = _text["Dock.AudioTitle"];
    }

    private Binding Translation(string key) => _text.CreateBinding(key);
}
