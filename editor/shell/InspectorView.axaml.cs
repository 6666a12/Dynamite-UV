using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

public sealed partial class InspectorView : UserControl
{
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private EditorDocument? _document;
    private IReadOnlyList<string> _ids = Array.Empty<string>();
    private string? _primary;

    public event Action<string, double>? SpeedChanged;

    public InspectorView()
    {
        AvaloniaXamlLoader.Load(this);
        _text.LanguageChanged += UpdateInspector;
        UpdateInspector();
    }

    public event Action<string, string?, V2PathCurve>? CurveChanged;

    public void ShowDocument(EditorDocument? document)
    {
        _document = document;
        _primary = document?.Selection switch
        {
            { Kind: EditorSelectionKind.Note, Id: { } id } => id,
            { Kind: EditorSelectionKind.PathNode, Id: { } nodeId } => document.SelectedChart.Notes
                .FirstOrDefault(note => note.Nodes.Any(node => node.Id == nodeId))?.Id,
            _ => null,
        };
        _ids = _primary is null ? Array.Empty<string>() : new[] { _primary };
        UpdateInspector();
    }

    public void Select(EditorDocument? document, IReadOnlyList<string> ids, string primary)
    {
        _document = document;
        _ids = ids;
        _primary = primary;
        UpdateInspector();
    }

    internal void SetSpeedForVerification(double speed)
    {
        this.FindControl<NumericUpDown>("SpeedValue")!.Value = (decimal)speed;
        ApplySpeed(null, new RoutedEventArgs());
    }

    internal void SetCurveForVerification(V2PathCurve curve)
    {
        this.FindControl<ComboBox>("CurveChoice")!.SelectedIndex = (int)curve;
        ApplyCurve(null, new RoutedEventArgs());
    }

    private EditableNote? SelectedNote => _document?.SelectedChart.Notes.SingleOrDefault(note => note.Id == _primary);
    private EditablePathNode? SelectedNode => _document?.Selection is { Kind: EditorSelectionKind.PathNode, Id: { } id }
        ? SelectedNote?.Nodes.SingleOrDefault(node => node.Id == id) : null;
    private bool CanEditCurve => SelectedNote is { Type: V2NoteType.Hold or V2NoteType.Mixer } note &&
        (SelectedNode is null || SelectedNode != note.Nodes[^1]);

    private void UpdateInspector()
    {
        var note = SelectedNote;
        var node = SelectedNode;
        this.FindControl<Border>("SelectionBadge")!.IsVisible = note is not null;
        this.FindControl<TextBlock>("NoteTitle")!.Text = _ids.Count > 1
            ? _text.Format("Inspector.Notes", _ids.Count)
            : node?.Id ?? (_primary is null ? _text["Inspector.NoSelection"] : _primary);
        var track = note?.Track switch
        {
            EditorTrack.Left => "Inspector.LeftStage",
            EditorTrack.Right => "Inspector.RightStage",
            EditorTrack.Center => "Inspector.CenterStage",
            _ => null,
        };
        this.FindControl<TextBlock>("NoteMeta")!.Text = track is null ? "" :
            _text[track] + (node is null ? "" : " · " + _text["Inspector.PathNode"]);
        this.FindControl<TextBox>("BarTimeValue")!.Text = note is null ? "" : ExactBarTimeText.Format(node?.Time ?? note.Time);
        this.FindControl<TextBox>("WidthValue")!.Text = (node?.Width ?? note?.Width)?.ToString("0.###") ?? "";
        this.FindControl<TextBox>("PositionValue")!.Text = (node?.Center ?? note?.Center)?.ToString("0.###") ?? "";
        this.FindControl<NumericUpDown>("SpeedValue")!.Value = (decimal)(note?.ScrollSpeedMultiplier ?? 1.0);
        this.FindControl<ComboBox>("NoteTypeChoice")!.SelectedIndex = note is null ? 0 : (int)note.Type;
        var curve = node?.CurveToNext ?? (node is null ? note?.CurveToNext : null) ?? V2PathCurve.Linear;
        this.FindControl<ComboBox>("CurveChoice")!.SelectedIndex = (int)curve;
        this.FindControl<ComboBox>("CurveChoice")!.IsEnabled = CanEditCurve;
        this.FindControl<Button>("ApplyCurveButton")!.IsEnabled = CanEditCurve;
        RefreshComboSelection(this.FindControl<ComboBox>("CurveChoice")!);
        RefreshComboSelection(this.FindControl<ComboBox>("NoteTypeChoice")!);
    }

    private static void RefreshComboSelection(ComboBox combo)
    {
        var index = combo.SelectedIndex;
        combo.SelectedIndex = -1;
        combo.SelectedIndex = index;
    }

    private void ApplySpeed(object? sender, RoutedEventArgs e)
    {
        if (_primary is null || SelectedNote is null) return;
        var value = (double)(this.FindControl<NumericUpDown>("SpeedValue")!.Value ?? 1m);
        SpeedChanged?.Invoke(_primary, value);
    }

    private void ApplyCurve(object? sender, RoutedEventArgs e)
    {
        if (_primary is null || !CanEditCurve) return;
        var curve = (V2PathCurve)this.FindControl<ComboBox>("CurveChoice")!.SelectedIndex;
        CurveChanged?.Invoke(_primary, SelectedNode?.Id, curve);
    }
}
