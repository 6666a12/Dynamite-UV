using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

public sealed partial class ToolsView : UserControl
{
    public event Action<string>? ToolSelected;
    public event Action? AuthoringChanged;
    public event Action? AddPanelRequested;
    public event Action? CoverStudioRequested;
    public event Action<string>? Notice;

    public V2NoteType PlacementType => (V2NoteType)this.FindControl<ComboBox>("PlacementTypeChoice")!.SelectedIndex;
    public EditorTrack PlacementTrack => (EditorTrack)this.FindControl<ComboBox>("PlacementTrackChoice")!.SelectedIndex;
    public double PlacementWidth => (double)(this.FindControl<NumericUpDown>("PlacementWidthValue")!.Value ?? 1m);

    internal void SetPlacementWidth(double width) =>
        this.FindControl<NumericUpDown>("PlacementWidthValue")!.Value = (decimal)width;

    public ToolsView() => AvaloniaXamlLoader.Load(this);

    internal void ConfigurePlacementForVerification(V2NoteType type, EditorTrack track, double width)
    {
        this.FindControl<ComboBox>("PlacementTypeChoice")!.SelectedIndex = (int)type;
        this.FindControl<ComboBox>("PlacementTrackChoice")!.SelectedIndex = (int)track;
        this.FindControl<NumericUpDown>("PlacementWidthValue")!.Value = (decimal)width;
    }

    internal void RestorePreferences(WorkbenchPreferences preferences)
    {
        this.FindControl<ComboBox>("PositionModeChoice")!.SelectedIndex = preferences.PositionSnap ? 0 : 1;
        SetPlacementWidth(preferences.PlacementWidth);
    }

    // Experimental object magnet is suspended: never restore a previously enabled preference.
    public bool ObjectSnapEnabled => false;
    public bool PositionSnapEnabled => this.FindControl<ComboBox>("PositionModeChoice")?.SelectedIndex != 1;

    public void SelectPlacementType(V2NoteType type)
    {
        this.FindControl<ComboBox>("PlacementTypeChoice")!.SelectedIndex = (int)type;
        SelectTool("PLACE");
    }

    public void SelectTrack(EditorTrack track) =>
        this.FindControl<ComboBox>("PlacementTrackChoice")!.SelectedIndex = (int)track;

    public void CycleTrack()
    {
        this.FindControl<ComboBox>("PlacementTrackChoice")!.SelectedIndex = PlacementTrack switch
        {
            EditorTrack.Center => (int)EditorTrack.Left,
            EditorTrack.Left => (int)EditorTrack.Right,
            _ => (int)EditorTrack.Center,
        };
    }

    public void SelectTool(string tool)
    {
        foreach (var item in this.FindControl<UniformGrid>("ToolModes")!.Children.OfType<Button>())
            item.Classes.Set("current-mode", item.Tag as string == tool);
        ToolSelected?.Invoke(tool);
    }

    private void ToolModesSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Two 82-DIP cells plus the gap; measure inside the scroll viewer's available width.
        if (sender is UniformGrid modes) modes.Columns = e.NewSize.Width >= 170 ? 2 : 1;
    }

    private void ToolClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tool }) SelectTool(tool);
    }

    private void OnAuthoringChanged(object? sender, RoutedEventArgs e) => AuthoringChanged?.Invoke();
    private void AddPanelClicked(object? sender, RoutedEventArgs e) => AddPanelRequested?.Invoke();
    private void CoverStudioClicked(object? sender, RoutedEventArgs e) => CoverStudioRequested?.Invoke();
    private void NoticeClicked(object? sender, RoutedEventArgs e) => Notice?.Invoke("WorkspaceNotice");
}
