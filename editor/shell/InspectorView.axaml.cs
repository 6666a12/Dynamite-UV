using System.Globalization;
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
    private double _loadedSpeed = 1;
    private decimal? _displayedSpeed;

    public event Action<string, double>? SpeedChanged;
    public event Action<string, string?, V2PathCurve>? CurveChanged;
    public event Action<string, string, bool>? HoldJudgeChanged;
    public event Action<int, int>? NudgeRequested;

    /// <summary>
    /// One user "apply geometry" request for the primary selection. <paramref name="nodeId"/> is null when the
    /// parent Note itself is selected; MainWindow should issue a single command
    /// (<see cref="MoveNoteCommand"/>, <see cref="MovePathNoteCommand"/>, or <see cref="EditPathNodeCommand"/>).
    /// </summary>
    public event Action<string, string?, ExactBarTime, double, double>? GeometryApplyRequested;

    /// <summary>
    /// One user "apply type / track" request. MainWindow should issue <see cref="ChangeNoteTypeCommand"/> and/or
    /// <see cref="SetNoteTrackCommand"/> for the changed values and combine them into one CompositeCommand.
    /// For Hold/Mixer conversions, <c>new ChangeNoteTypeCommand(noteId, type)</c> already builds a default tail
    /// node at the note's own time plus one grid step, so no current time is needed from this control; pass an
    /// explicit terminal node only if a custom tail is required.
    /// </summary>
    public event Action<string, V2NoteType, EditorTrack>? NoteKindApplyRequested;

    public InspectorView()
    {
        AvaloniaXamlLoader.Load(this);
        _text.LanguageChanged += UpdateLocalizedLabels;
        UpdateInspector();
    }

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

    internal void SetHoldJudgeForVerification(bool judge)
    {
        this.FindControl<CheckBox>("HoldJudgeValue")!.IsChecked = judge;
        ApplyHoldJudge(null, new RoutedEventArgs());
    }

    internal void NudgeForVerification(int timeDirection, int positionDirection) =>
        NudgeRequested?.Invoke(timeDirection, positionDirection);

    internal void SetGeometryForVerification(string barTime, double center, double width)
    {
        this.FindControl<TextBox>("BarTimeValue")!.Text = barTime;
        this.FindControl<TextBox>("PositionValue")!.Text = center.ToString("R", CultureInfo.InvariantCulture);
        this.FindControl<TextBox>("WidthValue")!.Text = width.ToString("R", CultureInfo.InvariantCulture);
        ApplyGeometry(null, new RoutedEventArgs());
    }

    internal void SetNoteKindForVerification(V2NoteType type, EditorTrack track)
    {
        this.FindControl<ComboBox>("NoteTypeChoice")!.SelectedIndex = (int)type;
        this.FindControl<ComboBox>("TrackChoice")!.SelectedIndex = (int)track;
        ApplyNoteKind(null, new RoutedEventArgs());
    }

    private EditableNote? SelectedNote => _document?.SelectedChart.Notes.SingleOrDefault(note => note.Id == _primary);
    private EditablePathNode? SelectedNode => _document?.Selection is { Kind: EditorSelectionKind.PathNode, Id: { } id }
        ? SelectedNote?.Nodes.SingleOrDefault(node => node.Id == id) : null;
    private bool CanEditCurve => SelectedNote is { Type: V2NoteType.Hold or V2NoteType.Mixer } note &&
        (SelectedNode is null || SelectedNode != note.Nodes[^1]);
    private bool CanEditHoldJudge => SelectedNote is { Type: V2NoteType.Hold } note &&
        SelectedNode is not null && SelectedNode != note.Nodes[^1];
    /// <summary>Geometry, track and type edits target one parent Note only; path-node and multi selections stay read-only.</summary>
    private bool CanEditGeometry => SelectedNote is not null && _ids.Count <= 1;
    private bool CanEditKind => CanEditGeometry && SelectedNode is null;
    private bool _refreshingObjects;
    public event Action<string, string?>? LocateRequested;

    private void UpdateLocalizedLabels()
    {
        var note = SelectedNote;
        var node = SelectedNode;
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
        UpdateSpatialWarnings();
    }

    private void UpdateInspector()
    {
        var note = SelectedNote;
        var node = SelectedNode;
        UpdateLocalizedLabels();
        this.FindControl<Border>("SelectionBadge")!.IsVisible = note is not null;
        this.FindControl<TextBox>("BarTimeValue")!.Text = note is null ? "" : ExactBarTimeText.Format(node?.Time ?? note.Time);
        this.FindControl<TextBox>("WidthValue")!.Text = (node?.Width ?? note?.Width)?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        this.FindControl<TextBox>("PositionValue")!.Text = (node?.Center ?? note?.Center)?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        _loadedSpeed = note?.ScrollSpeedMultiplier ?? 1.0;
        var speedControl = this.FindControl<NumericUpDown>("SpeedValue")!;
        speedControl.Value = (decimal)Math.Clamp(_loadedSpeed, (double)speedControl.Minimum, (double)speedControl.Maximum);
        _displayedSpeed = speedControl.Value;
        this.FindControl<ComboBox>("NoteTypeChoice")!.SelectedIndex = note is null ? 0 : (int)note.Type;
        this.FindControl<ComboBox>("TrackChoice")!.SelectedIndex = note is null ? (int)EditorTrack.Center : (int)note.Track;
        var curve = node?.CurveToNext ?? (node is null ? note?.CurveToNext : null) ?? V2PathCurve.Linear;
        this.FindControl<ComboBox>("CurveChoice")!.SelectedIndex = (int)curve;
        this.FindControl<ComboBox>("CurveChoice")!.IsEnabled = CanEditCurve;
        this.FindControl<Button>("ApplyCurveButton")!.IsEnabled = CanEditCurve;
        this.FindControl<StackPanel>("HoldJudgePanel")!.IsVisible = note?.Type == V2NoteType.Hold && node is not null;
        this.FindControl<CheckBox>("HoldJudgeValue")!.IsChecked = node?.Judge == true;
        this.FindControl<CheckBox>("HoldJudgeValue")!.IsEnabled = CanEditHoldJudge;
        this.FindControl<Button>("ApplyHoldJudgeButton")!.IsEnabled = CanEditHoldJudge;
        this.FindControl<TextBox>("BarTimeValue")!.IsEnabled = CanEditGeometry;
        this.FindControl<TextBox>("WidthValue")!.IsEnabled = CanEditGeometry;
        this.FindControl<TextBox>("PositionValue")!.IsEnabled = CanEditGeometry;
        this.FindControl<Button>("ApplyGeometryButton")!.IsEnabled = CanEditGeometry;
        this.FindControl<ComboBox>("NoteTypeChoice")!.IsEnabled = CanEditKind;
        this.FindControl<ComboBox>("TrackChoice")!.IsEnabled = CanEditKind;
        this.FindControl<Button>("ApplyNoteKindButton")!.IsEnabled = CanEditKind;
        this.FindControl<TextBlock>("GeometryLockedHint")!.IsVisible = note is not null && !CanEditGeometry;
        ClearGeometryError();
    }

    private sealed record ObjectTarget(string NoteId, string? NodeId, string Label)
    {
        public override string ToString() => Label;
    }

    private void LocateObject(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingObjects && this.FindControl<ComboBox>("ObjectChoice")!.SelectedItem is ObjectTarget target)
            LocateRequested?.Invoke(target.NoteId, target.NodeId);
    }

    private void UpdateSpatialWarnings()
    {
        var objects = this.FindControl<ComboBox>("ObjectChoice")!;
        _refreshingObjects = true;
        try
        {
            var targets = new List<ObjectTarget>();
            var overflow = 0; var invisible = 0; var unreachable = 0;
            var selectedLines = new List<string>();
            foreach (var item in _document?.SelectedChart.Notes ?? Enumerable.Empty<EditableNote>())
            {
                var report = EditorSpatialAnalysis.Analyze(item);
                var risk = report.Risks;
                if (risk.HasFlag(EditorSpatialRisk.RecommendedOverflow)) overflow++;
                if (risk.HasFlag(EditorSpatialRisk.Invisible)) invisible++;
                if (risk.HasFlag(EditorSpatialRisk.Unreachable)) unreachable++;
                var marker = risk == EditorSpatialRisk.None ? "" : "⚠ ";
                targets.Add(new(item.Id, null, $"{marker}{item.Id} · {ExactBarTimeText.Format(item.Time)}"));
                targets.AddRange(item.Nodes.Select(n => new ObjectTarget(item.Id, n.Id,
                    $"  {n.Id} · {ExactBarTimeText.Format(n.Time)}")));
                if (!_ids.Contains(item.Id)) continue;
                if (risk.HasFlag(EditorSpatialRisk.RecommendedOverflow)) selectedLines.Add(_text["Risk.Overflow"]);
                if (risk.HasFlag(EditorSpatialRisk.Invisible)) selectedLines.Add(_text["Risk.Invisible"]);
                if (risk.HasFlag(EditorSpatialRisk.Unreachable)) selectedLines.Add(_text[item.Type switch
                {
                    V2NoteType.Mine => "Risk.Mine", V2NoteType.Mixer => "Risk.Mixer", _ => "Risk.Unreachable",
                }]);
                if (risk.HasFlag(EditorSpatialRisk.NumericRange)) selectedLines.Add(_text["Risk.Numeric"]);
                if (report.PathSampled) selectedLines.Add(_text["Risk.Sampled"]);
            }
            objects.ItemsSource = targets;
            objects.SelectedItem = targets.FirstOrDefault(t => t.NoteId == _primary && t.NodeId == SelectedNode?.Id);
            this.FindControl<TextBlock>("SpatialWarnings")!.Text = string.Join("\n", selectedLines.Distinct());
            this.FindControl<TextBlock>("ChartRiskSummary")!.Text = _document is null ? "" :
                _text.Format("Risk.Summary", overflow, invisible, unreachable) + "\n" + _text["Risk.Scope"];
        }
        finally { _refreshingObjects = false; }
    }

    private void ApplySpeed(object? sender, RoutedEventArgs e)
    {
        if (_primary is null || SelectedNote is null) return;
        var displayed = this.FindControl<NumericUpDown>("SpeedValue")!.Value;
        var value = displayed == _displayedSpeed ? _loadedSpeed : (double)(displayed ?? 1m);
        SpeedChanged?.Invoke(_primary, value);
    }

    private void ApplyCurve(object? sender, RoutedEventArgs e)
    {
        if (_primary is null || !CanEditCurve) return;
        var curve = (V2PathCurve)this.FindControl<ComboBox>("CurveChoice")!.SelectedIndex;
        CurveChanged?.Invoke(_primary, SelectedNode?.Id, curve);
    }

    private void ApplyHoldJudge(object? sender, RoutedEventArgs e)
    {
        if (_primary is null || SelectedNode is null || !CanEditHoldJudge) return;
        HoldJudgeChanged?.Invoke(_primary, SelectedNode.Id,
            this.FindControl<CheckBox>("HoldJudgeValue")!.IsChecked == true);
    }

    private void ApplyGeometry(object? sender, RoutedEventArgs e)
    {
        if (_primary is null || !CanEditGeometry) return;
        if (!TryReadGeometry(out var time, out var center, out var width)) return;
        GeometryApplyRequested?.Invoke(_primary, SelectedNode?.Id, time, center, width);
    }

    private void ApplyNoteKind(object? sender, RoutedEventArgs e)
    {
        if (_primary is null || !CanEditKind) return;
        var type = (V2NoteType)this.FindControl<ComboBox>("NoteTypeChoice")!.SelectedIndex;
        var track = (EditorTrack)this.FindControl<ComboBox>("TrackChoice")!.SelectedIndex;
        NoteKindApplyRequested?.Invoke(_primary, type, track);
    }

    private bool TryReadGeometry(out ExactBarTime time, out double center, out double width)
    {
        var barText = this.FindControl<TextBox>("BarTimeValue")!.Text ?? "";
        if (!ExactBarTimeText.TryParse(barText, out time))
        {
            center = 0;
            width = 0;
            return ShowGeometryError("Inspector.InvalidBarTime");
        }
        width = ParseDouble(this.FindControl<TextBox>("WidthValue")!.Text);
        if (!double.IsFinite(width) || width <= 0)
        {
            center = 0;
            return ShowGeometryError("Inspector.InvalidWidth");
        }
        center = ParseDouble(this.FindControl<TextBox>("PositionValue")!.Text);
        if (!double.IsFinite(center))
            return ShowGeometryError("Inspector.InvalidPosition");
        ClearGeometryError();
        return true;
    }

    private static double ParseDouble(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN;

    private bool ShowGeometryError(string key)
    {
        var error = this.FindControl<TextBlock>("GeometryError")!;
        error.Text = _text[key];
        error.IsVisible = true;
        return false;
    }

    private void ClearGeometryError()
    {
        var error = this.FindControl<TextBlock>("GeometryError")!;
        error.Text = "";
        error.IsVisible = false;
    }

    private void NudgeTimeBack(object? sender, RoutedEventArgs e) => NudgeRequested?.Invoke(-1, 0);
    private void NudgeTimeForward(object? sender, RoutedEventArgs e) => NudgeRequested?.Invoke(1, 0);
    private void NudgePositionBack(object? sender, RoutedEventArgs e) => NudgeRequested?.Invoke(0, -1);
    private void NudgePositionForward(object? sender, RoutedEventArgs e) => NudgeRequested?.Invoke(0, 1);
}
