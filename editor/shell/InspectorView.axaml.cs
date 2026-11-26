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
    public Func<EditorBatchEdit, string?>? BatchEditRequested { get; set; }
    public event Action<string>? BatchLocateRequested;
    public Func<string, string?, V2PathCurve, string?, string?>? CurveChangeRequested { get; set; }
    internal CustomCurveEditor Formula => this.FindControl<CustomCurveEditor>("PathFormula")!;
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
        Formula.AttachChoice(this.FindControl<ComboBox>("CurveChoice")!);
        var batch = this.FindControl<SelectionInspectorView>("BatchInspector")!;
        batch.ApplyRequested = edit => BatchEditRequested?.Invoke(edit) ?? null;
        batch.LocateRequested += id => BatchLocateRequested?.Invoke(id);
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
        var choice = this.FindControl<ComboBox>("CurveChoice")!;
        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, curve.ToString()));
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

    private T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
    private bool IsMultiple => _ids.Count > 1;
    private EditableNote? SelectedNote => _document?.SelectedChart.Notes.SingleOrDefault(note => note.Id == _primary);
    private EditablePathNode? SelectedNode => !IsMultiple && _document?.Selection is { Kind: EditorSelectionKind.PathNode, Id: { } id }
        ? SelectedNote?.Nodes.SingleOrDefault(node => node.Id == id) : null;
    private static bool IsTail(EditableNote? note, EditablePathNode? node) =>
        node is not null && note is { Nodes.Count: > 0 } && node == note.Nodes[^1];
    private bool CanEditCurve => !IsMultiple && SelectedNote is { Type: V2NoteType.Hold or V2NoteType.Mixer } note &&
        !IsTail(note, SelectedNode);
    private bool CanEditHoldJudge => SelectedNote is { Type: V2NoteType.Hold } note &&
        SelectedNode is { } node && !IsTail(note, node);
    // A lone path node has editable geometry; type and track belong to its parent and are edited on the parent selection.
    private bool CanEditGeometry => !IsMultiple && SelectedNote is not null;
    private bool CanEditKind => CanEditGeometry && SelectedNode is null;
    private bool _refreshingObjects;
    public event Action<string, string?>? LocateRequested;

    private void UpdateLocalizedLabels()
    {
        var note = SelectedNote;
        var node = SelectedNode;
        var type = note is null ? "" : _text[$"Inspector.{note.Type}"];
        C<TextBlock>("NoteTitle").Text = IsMultiple ? _text.Format("Inspector.Notes", _ids.Count) :
            note is null ? _text["Inspector.NoSelection"] : node is null ? type :
            _text[IsTail(note, node) ? "Inspector.TailNode" : "Inspector.PathNode"];
        var meta = C<TextBlock>("NoteMeta");
        meta.IsVisible = CanEditGeometry;
        meta.Text = note is not null && meta.IsVisible
            ? (node is null ? "" : type + " · ") + _text[$"Inspector.{note.Track}Stage"] : "";
        C<TextBlock>("NoteIdLabel").Text = _text[node is null ? "Inspector.NoteId" : "Inspector.ParentNoteId"];
        UpdateSpatialWarnings(node?.Id);
    }

    private void UpdateInspector()
    {
        var note = SelectedNote;
        var node = SelectedNode;
        var single = CanEditGeometry;
        var kind = CanEditKind;
        var outgoing = CanEditCurve;
        var judge = CanEditHoldJudge;
        var tail = IsTail(note, node);
        C<StackPanel>("IdentityPanel").IsVisible = single;
        C<StackPanel>("NodeIdentity").IsVisible = node is not null;
        C<TextBox>("NoteIdValue").Text = single ? note?.Id : "";
        C<TextBox>("NodeIdValue").Text = node?.Id ?? "";
        C<StackPanel>("LocatePanel").IsVisible = !IsMultiple;
        C<StackPanel>("SinglePanel").IsVisible = single;
        C<StackPanel>("KindPanel").IsVisible = kind;
        C<StackPanel>("SinglePathPanel").IsVisible = outgoing || tail;
        C<StackPanel>("CurvePanel").IsVisible = outgoing;
        C<TextBlock>("TailCurveState").IsVisible = tail;
        C<StackPanel>("HoldJudgePanel").IsVisible = judge;
        C<StackPanel>("HoldTailJudgeState").IsVisible = tail && note?.Type == V2NoteType.Hold;
        var nudge = C<Expander>("ExistingBatchTools");
        nudge.IsVisible = note is not null;
        nudge.IsExpanded = single;
        var batch = C<SelectionInspectorView>("BatchInspector");
        batch.IsVisible = IsMultiple;
        batch.Show(_document, _ids);
        UpdateLocalizedLabels();
        C<StackPanel>("SpeedPanel").IsVisible = note is not null;
        C<TextBlock>("ParentSpeedHint").IsVisible = node is not null;
        C<TextBox>("BarTimeValue").Text = note is null ? "" : ExactBarTimeText.Format(node?.Time ?? note.Time);
        C<TextBox>("WidthValue").Text = (node?.Width ?? note?.Width)?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        C<TextBox>("PositionValue").Text = (node?.Center ?? note?.Center)?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        _loadedSpeed = note?.ScrollSpeedMultiplier ?? 1.0;
        var speedControl = C<NumericUpDown>("SpeedValue");
        speedControl.Value = (decimal)Math.Clamp(_loadedSpeed, (double)speedControl.Minimum, (double)speedControl.Maximum);
        _displayedSpeed = speedControl.Value;
        C<ComboBox>("NoteTypeChoice").SelectedIndex = note is null ? 0 : (int)note.Type;
        C<ComboBox>("TrackChoice").SelectedIndex = note is null ? (int)EditorTrack.Center : (int)note.Track;
        var curve = (node is null ? note?.CurveToNext : node.CurveToNext) ?? V2PathCurve.Linear;
        // Binding no outgoing target hides the editor, but its per-point draft remains owned by Formula.
        Formula.ShowTarget(_document, outgoing ? node?.Key ?? note?.Key : null, node?.Id ?? note?.Id,
            curve.ToString(), node is null ? note?.CustomExpr : node.CustomExpr);
        C<ComboBox>("CurveChoice").IsEnabled = outgoing;
        C<Button>("ApplyCurveButton").IsEnabled = outgoing;
        C<CheckBox>("HoldJudgeValue").IsChecked = node?.Judge == true;
        C<CheckBox>("HoldJudgeValue").IsEnabled = judge;
        C<Button>("ApplyHoldJudgeButton").IsEnabled = judge;
        C<TextBox>("BarTimeValue").IsEnabled = single;
        C<TextBox>("WidthValue").IsEnabled = single;
        C<TextBox>("PositionValue").IsEnabled = single;
        C<Button>("ApplyGeometryButton").IsEnabled = single;
        C<ComboBox>("NoteTypeChoice").IsEnabled = kind;
        C<ComboBox>("TrackChoice").IsEnabled = kind;
        C<Button>("ApplyNoteKindButton").IsEnabled = kind;
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

    private void UpdateSpatialWarnings(string? selectedNodeId)
    {
        var objects = C<ComboBox>("ObjectChoice");
        _refreshingObjects = true;
        try
        {
            var targets = new List<ObjectTarget>();
            var overflow = 0; var invisible = 0; var unreachable = 0; var numeric = 0;
            var selectedLines = new List<string>();
            var chart = _document?.SelectedChart;
            foreach (var item in chart?.Notes ?? Enumerable.Empty<EditableNote>())
            {
                var report = EditorSpatialAnalysis.Analyze(item, chart!.CurveCache);
                var risk = report.Risks;
                if (risk.HasFlag(EditorSpatialRisk.RecommendedOverflow)) overflow++;
                if (risk.HasFlag(EditorSpatialRisk.Invisible)) invisible++;
                if (risk.HasFlag(EditorSpatialRisk.Unreachable)) unreachable++;
                if (risk.HasFlag(EditorSpatialRisk.NumericRange)) numeric++;
                var marker = risk == EditorSpatialRisk.None ? "" : "⚠ ";
                targets.Add(new(item.Id, null, $"{marker}{item.Id} · {ExactBarTimeText.Format(item.Time)}"));
                targets.AddRange(item.Nodes.Select(n => new ObjectTarget(item.Id, n.Id,
                    $"  {n.Id} · {ExactBarTimeText.Format(n.Time)}")));
                if (IsMultiple || item.Id != _primary) continue;
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
            objects.IsEnabled = targets.Count > 0;
            objects.SelectedItem = targets.FirstOrDefault(t => t.NoteId == _primary && t.NodeId == selectedNodeId);
            var selectedWarnings = C<TextBlock>("SpatialWarnings");
            selectedWarnings.Text = string.Join("\n", selectedLines);
            selectedWarnings.IsVisible = selectedLines.Count > 0;
            var chartRisks = new List<string>();
            if (overflow > 0) chartRisks.Add(_text.Format("Risk.ChartOverflow", overflow));
            if (invisible > 0) chartRisks.Add(_text.Format("Risk.ChartInvisible", invisible));
            if (unreachable > 0) chartRisks.Add(_text.Format("Risk.ChartUnreachable", unreachable));
            if (numeric > 0) chartRisks.Add(_text.Format("Risk.ChartNumeric", numeric));
            var summary = C<TextBlock>("ChartRiskSummary");
            summary.IsVisible = chartRisks.Count > 0;
            summary.Text = summary.IsVisible ? _text.Format("Risk.Chart", string.Join(" · ", chartRisks)) : "";
            C<StackPanel>("RiskSection").IsVisible = summary.IsVisible || selectedWarnings.IsVisible;
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
        if (this.FindControl<ComboBox>("CurveChoice")!.SelectedItem is ComboBoxItem { Tag: string name } &&
            Enum.TryParse<V2PathCurve>(name, out var curve))
            Formula.Submit(expr => CurveChangeRequested is null ? _text["Timing.Error.NoDocument"] : CurveChangeRequested(_primary, SelectedNode?.Id, curve, expr));
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
