using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

public sealed partial class ToolsView : UserControl
{
    public event Action<string>? ToolSelected;
    public event Action? AuthoringChanged;
    public event Action? AddPanelRequested;
    public event Action<string>? Notice;

    public V2NoteType PlacementType => (V2NoteType)this.FindControl<ComboBox>("PlacementTypeChoice")!.SelectedIndex;
    public EditorTrack PlacementTrack => (EditorTrack)this.FindControl<ComboBox>("PlacementTrackChoice")!.SelectedIndex;
    public double PlacementWidth => (double)(this.FindControl<NumericUpDown>("PlacementWidthValue")!.Value ?? 1m);

    public ToolsView() => AvaloniaXamlLoader.Load(this);

    internal void ConfigurePlacementForVerification(V2NoteType type, EditorTrack track, double width)
    {
        this.FindControl<ComboBox>("PlacementTypeChoice")!.SelectedIndex = (int)type;
        this.FindControl<ComboBox>("PlacementTrackChoice")!.SelectedIndex = (int)track;
        this.FindControl<NumericUpDown>("PlacementWidthValue")!.Value = (decimal)width;
    }

    public void SelectTool(string tool)
    {
        foreach (var item in this.GetLogicalDescendants().OfType<Ursa.Controls.IconButton>())
            if (item.Tag is string tag) item.Classes.Set("active", tag == tool);
        ToolSelected?.Invoke(tool);
    }

    private void ToolClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Ursa.Controls.IconButton button) SelectTool(button.Tag?.ToString() ?? "SELECT");
    }

    private void OnAuthoringChanged(object? sender, RoutedEventArgs e) => AuthoringChanged?.Invoke();
    private void AddPanelClicked(object? sender, RoutedEventArgs e) => AddPanelRequested?.Invoke();
    private void NoticeClicked(object? sender, RoutedEventArgs e) => Notice?.Invoke("WorkspaceNotice");
}
