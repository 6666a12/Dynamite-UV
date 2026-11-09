using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Owns only multi-selection drafts. The host applies validated document commands.</summary>
public sealed partial class SelectionInspectorView : UserControl
{
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private EditableNote[] _notes = [];
    private bool _refreshing = true;
    private string _widthBaseline = "";
    private int _trackBaseline;
    private int _typeBaseline;
    public Func<EditorBatchEdit, string?>? ApplyRequested { get; set; }
    public event Action<string>? LocateRequested;
    private T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
    public SelectionInspectorView()
    {
        AvaloniaXamlLoader.Load(this);
        AttachedToVisualTree += (_, _) => { _text.LanguageChanged += Localize; Localize(); };
        DetachedFromVisualTree += (_, _) => _text.LanguageChanged -= Localize;
        _refreshing = false;
    }
    public void Show(EditorDocument? document, IReadOnlyList<string> ids)
    {
        _refreshing = true;
        try
        {
            _notes = document?.SelectedChart.Notes.Where(n => ids.Contains(n.Id)).Select(n => n.Clone()).ToArray() ?? [];
            C<ComboBox>("TimeMode").SelectedIndex = 0;
            C<ComboBox>("WidthMode").SelectedIndex = 0;
            C<ComboBox>("WidthScope").SelectedIndex = 0;
            ResetTransform(null, new());
            ResetProperties(null, new());
            C<StackPanel>("PathScope").IsVisible = _notes.Any(n => n.Nodes.Count > 0);
            C<ComboBox>("BatchType").IsEnabled = _notes.Length > 0 && _notes.All(n => EditNoteSelectionCommand.CanConvert(n.Type));
            C<ListBox>("SelectedObjects").ItemsSource = _notes.Select(n => $"{n.Id} · {ExactBarTimeText.Format(n.Time)}").ToArray();
            Localize();
        }
        finally { _refreshing = false; }
        UpdateButtons();
    }
    private void Localize()
    {
        var tracks = _notes.GroupBy(n => n.Track).Select(g => $"{_text[$"Inspector.{g.Key}Stage"]} {g.Count()}");
        var types = _notes.GroupBy(n => n.Type).Select(g => $"{_text[$"Inspector.{g.Key}"]} {g.Count()}");
        C<TextBlock>("Summary").Text = string.Join(" · ", tracks) + "\n" + string.Join(" · ", types);
        C<TextBox>("WidthInput").Watermark = _text["Batch.Mixed"];
        C<TextBlock>("TypeWarning").Text = C<ComboBox>("BatchType").IsEnabled ? "" : _text["Batch.TypeWarning"];
        UpdateButtons();
    }
    private bool WidthDirty => C<TextBox>("WidthInput").Text != _widthBaseline;
    private bool TrackDirty => C<ComboBox>("BatchTrack").SelectedIndex > 0 && C<ComboBox>("BatchTrack").SelectedIndex != _trackBaseline;
    private bool TypeDirty => C<ComboBox>("BatchType").IsEnabled && C<ComboBox>("BatchType").SelectedIndex > 0 && C<ComboBox>("BatchType").SelectedIndex != _typeBaseline;
    private int PendingCount => (WidthDirty ? 1 : 0) + (TrackDirty ? 1 : 0) + (TypeDirty ? 1 : 0);
    private void UpdateButtons()
    {
        if (_refreshing) return;
        var valid = TryTransform(out var transform);
        var delta = transform.Time ?? ExactBarTime.Zero;
        var timeChange = transform.MoveStart ? _notes.Length > 0 && delta != _notes.Min(n => n.Time) : delta != ExactBarTime.Zero;
        C<Button>("ApplyTransformButton").IsEnabled = _notes.Length > 1 && valid && (timeChange || transform.PositionDelta != 0);
        C<Button>("ResetTransformButton").IsVisible = C<TextBox>("TimeInput").Text != (C<ComboBox>("TimeMode").SelectedIndex == 1 && _notes.Length > 0 ? ExactBarTimeText.Format(_notes.Min(n => n.Time)) : "0") || C<TextBox>("PositionInput").Text != "0";
        C<Button>("ApplyPropertiesButton").Content = _text.Format("Batch.ApplyProperties", PendingCount);
        C<Button>("ApplyPropertiesButton").IsEnabled = _notes.Length > 1 && PendingCount > 0 && (!WidthDirty || TryDouble("WidthInput", out _));
        C<Button>("ResetPropertiesButton").IsVisible = PendingCount > 0;
        C<TextBlock>("WidthLabel").Text = (WidthDirty ? "● " : "") + _text["Inspector.Width"];
        C<TextBlock>("TrackLabel").Text = (TrackDirty ? "● " : "") + _text["Inspector.Track"];
        C<TextBlock>("TypeLabel").Text = (TypeDirty ? "● " : "") + _text["Inspector.NoteType"];
        C<TextBlock>("WidthWarning").IsVisible = C<ComboBox>("WidthScope").SelectedIndex == 1;
        C<TextBlock>("WidthWarning").Text = _text[C<ComboBox>("WidthMode").SelectedIndex == 0 ? "Batch.WidthWarning" : "Batch.WidthAddWarning"];
    }
    private void Changed(object? sender, TextChangedEventArgs e) => UpdateButtons();
    private void WidthChanged(object? sender, TextChangedEventArgs e) => UpdateButtons();
    private void TrackChanged(object? sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void TypeChanged(object? sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void TransformModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) ResetTransform(sender, new());
    }
    private void WidthModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) ResetWidth();
    }
    private void ScopeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) ResetWidth();
    }
    private void ResetTransform(object? sender, RoutedEventArgs e)
    {
        C<TextBox>("TimeInput").Text = C<ComboBox>("TimeMode").SelectedIndex == 1 && _notes.Length > 0 ? ExactBarTimeText.Format(_notes.Min(n => n.Time)) : "0";
        C<TextBox>("PositionInput").Text = "0";
        C<TextBlock>("TransformError").IsVisible = false;
        UpdateButtons();
    }
    private void ResetWidth()
    {
        var widths = _notes.SelectMany(n => C<ComboBox>("WidthScope").SelectedIndex == 1
            ? new[] { n.Width }.Concat(n.Nodes.Select(p => p.Width)) : new[] { n.Width }).Distinct().ToArray();
        _widthBaseline = C<ComboBox>("WidthMode").SelectedIndex == 1 ? "0" : widths.Length == 1 ? widths[0].ToString("R", CultureInfo.InvariantCulture) : "";
        C<TextBox>("WidthInput").Text = _widthBaseline;
        UpdateButtons();
    }
    private void ResetProperties(object? sender, RoutedEventArgs e)
    {
        var tracks = _notes.Select(n => n.Track).Distinct().ToArray();
        var types = _notes.Select(n => n.Type).Distinct().ToArray();
        _trackBaseline = tracks.Length == 1 ? (int)tracks[0] + 1 : 0;
        _typeBaseline = types.Length == 1 && EditNoteSelectionCommand.CanConvert(types[0]) ? (int)types[0] + 1 : 0;
        C<ComboBox>("BatchTrack").SelectedIndex = _trackBaseline;
        C<ComboBox>("BatchType").SelectedIndex = _typeBaseline;
        ResetWidth();
        C<TextBlock>("PropertiesError").IsVisible = false;
        UpdateButtons();
    }
    private void ApplyTransform(object? sender, RoutedEventArgs e)
    {
        if (C<Button>("ApplyTransformButton").IsEnabled && TryTransform(out var edit)) Submit(edit, "TransformError");
    }
    private void ApplyProperties(object? sender, RoutedEventArgs e)
    {
        if (!C<Button>("ApplyPropertiesButton").IsEnabled) return;
        double? width = null;
        if (WidthDirty) { if (!TryDouble("WidthInput", out var parsed)) return; width = parsed; }
        Submit(new EditorBatchEdit { Width = width, AddWidth = C<ComboBox>("WidthMode").SelectedIndex == 1,
            WholePath = C<ComboBox>("WidthScope").SelectedIndex == 1,
            Track = TrackDirty ? (EditorTrack?)(C<ComboBox>("BatchTrack").SelectedIndex - 1) : null,
            Type = TypeDirty ? (V2NoteType?)(C<ComboBox>("BatchType").SelectedIndex - 1) : null }, "PropertiesError");
    }
    private void Submit(EditorBatchEdit edit, string errorControl)
    {
        var error = ApplyRequested is null ? _text["Batch.Unavailable"] : ApplyRequested(edit);
        // Success is refreshed by the host from the authoritative document; failure retains drafts.
        C<TextBlock>(errorControl).Text = error ?? "";
        C<TextBlock>(errorControl).IsVisible = error is not null;
    }
    private bool TryTransform(out EditorBatchEdit edit)
    {
        edit = new();
        var text = (C<TextBox>("TimeInput").Text ?? "").Trim();
        var negative = text.StartsWith('-');
        if (text.StartsWith('+') || negative) text = text[1..];
        if (!ExactBarTimeText.TryParse(text, out var time) || !TryDouble("PositionInput", out var position)) return false;
        if (negative) time = -time;
        var moveStart = C<ComboBox>("TimeMode").SelectedIndex == 1;
        if (moveStart && time < ExactBarTime.Zero) return false;
        edit = new EditorBatchEdit { Time = time, MoveStart = moveStart, PositionDelta = position };
        return true;
    }
    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
        {
            if (ReferenceEquals(e.Source, C<TextBox>("TimeInput")) || ReferenceEquals(e.Source, C<TextBox>("PositionInput")))
            { ApplyTransform(null, new()); e.Handled = true; }
            else if (ReferenceEquals(e.Source, C<TextBox>("WidthInput")))
            { ApplyProperties(null, new()); e.Handled = true; }
        }
        base.OnKeyDown(e);
    }

    private bool TryDouble(string name, out double value) => double.TryParse(C<TextBox>(name).Text,
        NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    private void LocateSelected(object? sender, SelectionChangedEventArgs e)
    {
        var index = C<ListBox>("SelectedObjects").SelectedIndex;
        if (!_refreshing && index >= 0 && index < _notes.Length) LocateRequested?.Invoke(_notes[index].Id);
    }
}
