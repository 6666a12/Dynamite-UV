using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>
/// Timing editor for the selected difficulty. BPM/Scroll requests go to the workbench;
/// live offset gestures use a chart-owned undo token. Neither route owns saving.
/// </summary>
public sealed partial class TimingView : UserControl
{
    internal CustomCurveEditor Formula => this.FindControl<CustomCurveEditor>("ScrollFormula")!;

    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private EditorDocument? _document;
    private double _currentBar;
    private ExactBarTime? _exactPlayhead;
    private bool _updating;
    private double? _loadedBpm;
    private decimal? _displayedBpm;
    private double? _loadedScroll;
    private decimal? _displayedScroll;

    public Func<IEditorCommand, string?>? CommandRequested { get; set; }
    public event Action<EditorSelection>? RowSelectionChanged;
    public event Action<string>? ErrorRequested;

    public TimingView()
    {
        AvaloniaXamlLoader.Load(this);
        Formula.AttachChoice(this.FindControl<ComboBox>("ScrollCurveChoice")!);
        _text.LanguageChanged += OnLanguageChanged;
        UpdateView();
    }

    /// <summary>Swaps the document shown by the panel; call again after every executed command.</summary>
    public void SetDocument(EditorDocument? document)
    {
        var switched = !ReferenceEquals(_document, document) || _offsetChartId != document?.SelectedChartId;
        if (switched) EndOffsetNudge();
        _document = document;
        _offsetChartId = document?.SelectedChartId;
        UpdateOffset(switched);
        UpdateView();
    }

    /// <summary>Keeps the playhead bar in sync; empty add-time fields default to this bar.</summary>
    public void SetCurrentBar(double bar, ExactBarTime? exactBar = null)
    {
        _currentBar = bar;
        _exactPlayhead = exactBar;
        UpdatePlayheadCaption();
    }

    private EditableChart? Chart => _document?.SelectedChart;
    private ListBox BpmListBox => this.FindControl<ListBox>("BpmList")!;
    private ListBox ScrollListBox => this.FindControl<ListBox>("ScrollList")!;
    private TextBlock ErrorBlock => this.FindControl<TextBlock>("TimingError")!;

    private string? _offsetChartId, _offsetShown;
    private ChartOffsetNudge? _offsetNudge;
    public event Action? OffsetPreviewChanged;

    public void EndOffsetNudge() { _offsetNudge?.End(); _offsetNudge = null; }
    public bool NudgeOffset(int direction, bool fine)
    {
        if (_document is null || direction is not (1 or -1)) return false;
        _offsetNudge ??= _document.BeginChartOffsetNudge();
        try { if (!_offsetNudge.Step(direction * (fine ? .001 : .010))) return false; }
        catch (Exception ex) when (ex is ArithmeticException or ArgumentException or InvalidOperationException)
        { EndOffsetNudge(); ShowError(ex.Message); return false; }
        UpdateOffset(false); ClearError(); OffsetPreviewChanged?.Invoke(); return true;
    }

    private void UpdateOffset(bool replaceDraft)
    {
        var field = this.FindControl<TextBox>("OffsetMsInput")!;
        var chart = Chart;
        var display = chart is null ? "" : ChartOffsetUnits.FormatMilliseconds(chart.AudioOffsetSec, signed: true);
        this.FindControl<TextBlock>("OffsetReadout")!.Text = chart is null ? "—" : _text.Format("Timing.OffsetStatus", chart.Id, display);
        if (replaceDraft || field.Text == _offsetShown || _offsetShown == null) field.Text = display;
        _offsetShown = display;
        field.IsEnabled = this.FindControl<Button>("OffsetApplyButton")!.IsEnabled = chart is not null;
    }

    private void ApplyOffset(object? sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        EndOffsetNudge();
        var input = this.FindControl<TextBox>("OffsetMsInput")!.Text?.Trim();
        if (input == _offsetShown) return; // Do not round-trip a previously loaded binary64 value.
        if (!ChartOffsetUnits.TryParseMilliseconds(input, out var seconds))
        { ShowError(_text["Timing.Error.InvalidOffset"]); return; }
        var changed = seconds != _document.SelectedChart.AudioOffsetSec;
        if (changed) _document.Execute(new EditChartOffsetCommand(seconds));
        UpdateOffset(true); ClearError(); if (changed) OffsetPreviewChanged?.Invoke();
    }

    private void OnLanguageChanged()
    {
        UpdatePlayheadCaption();
        UpdateOffset(false);
        ClearError();
    }

    private void UpdatePlayheadCaption()
    {
        this.FindControl<TextBlock>("PlayheadCaption")!.Text =
            _text.Format("Timing.PlayheadCaption", _exactPlayhead is { } exact
                ? ExactBarTimeText.Format(exact) : _currentBar.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private void UpdateView()
    {
        _updating = true;
        try
        {
            FillBpmList();
            FillScrollList();
        }
        finally
        {
            _updating = false;
        }
        RestoreSelection();
        UpdateEnabledState();
        ClearError();
    }

    private void FillBpmList()
    {
        var list = BpmListBox;
        list.Items.Clear();
        if (Chart is { } chart)
        {
            foreach (var bpm in chart.Bpms.OrderBy(item => item.Time))
            {
                list.Items.Add(new ListBoxItem
                {
                    Tag = bpm.Time,
                    Content = $"{ExactBarTimeText.Format(bpm.Time)}  ·  {bpm.Bpm.ToString("0.###", CultureInfo.InvariantCulture)}",
                });
            }
        }
    }

    private void FillScrollList()
    {
        var list = ScrollListBox;
        list.Items.Clear();
        if (Chart is { } chart)
        {
            foreach (var scroll in chart.ScrollSpeeds.OrderBy(item => item.Time))
            {
                list.Items.Add(new ListBoxItem
                {
                    Tag = scroll.Time,
                    Content = $"{ExactBarTimeText.Format(scroll.Time)}  ·  ×{scroll.Value.ToString("0.##", CultureInfo.InvariantCulture)}",
                });
            }
        }
    }

    private void RestoreSelection()
    {
        if (_document?.Selection is not { } selection || Chart is null) return;
        var list = selection.Kind == EditorSelectionKind.Bpm ? BpmListBox
            : selection.Kind == EditorSelectionKind.Scroll ? ScrollListBox : null;
        if (list is null || !ExactBarTimeText.TryParse(selection.Id, out var time)) return;
        foreach (var item in list.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is ExactBarTime itemTime && itemTime == time)
            {
                list.SelectedItem = item;
                break;
            }
        }
    }

    private void UpdateEnabledState()
    {
        var hasDocument = Chart is not null;
        var bpmTime = SelectedBpmTime;
        var scrollTime = SelectedScrollTime;
        var bpmSelected = bpmTime.HasValue;
        var scrollSelected = scrollTime.HasValue;
        var bpmBase = bpmTime is { } selectedBpm && selectedBpm == ExactBarTime.Zero;
        var scrollBase = scrollTime is { } selectedScroll && selectedScroll == ExactBarTime.Zero;

        // The base event time stays editable: the same field also serves Add, and the core
        // commands reject moving a bar 0 base event, so Apply surfaces that error honestly.
        this.FindControl<TextBox>("BpmTimeValue")!.IsReadOnly = !hasDocument;
        this.FindControl<NumericUpDown>("BpmValueInput")!.IsEnabled = hasDocument;
        this.FindControl<TextBox>("ScrollTimeValue")!.IsReadOnly = !hasDocument;
        this.FindControl<NumericUpDown>("ScrollValueInput")!.IsEnabled = hasDocument;
        this.FindControl<TextBlock>("BpmBaseLocked")!.IsVisible = bpmBase;
        this.FindControl<TextBlock>("ScrollBaseLocked")!.IsVisible = scrollBase;
        this.FindControl<Button>("AddBpmButton")!.IsEnabled = hasDocument;
        this.FindControl<Button>("ApplyBpmButton")!.IsEnabled = bpmSelected;
        this.FindControl<Button>("DeleteBpmButton")!.IsEnabled = bpmSelected && !bpmBase;
        this.FindControl<Button>("AddScrollButton")!.IsEnabled = hasDocument;
        this.FindControl<Button>("ApplyScrollButton")!.IsEnabled = scrollSelected;
        this.FindControl<Button>("DeleteScrollButton")!.IsEnabled = scrollSelected &&
            !(scrollBase && Chart!.ScrollSpeeds.Count > 1);

        // The tail Scroll event must omit curveToNext; the core command enforces this too.
        var scrollTail = hasDocument && scrollSelected && Chart!.ScrollSpeeds.Count > 0 &&
            Chart.ScrollSpeeds.Max(item => item.Time) == scrollTime!.Value;
        this.FindControl<ComboBox>("ScrollCurveChoice")!.IsEnabled = hasDocument && scrollSelected && !scrollTail;
        var current = scrollSelected ? Chart?.ScrollSpeeds.FirstOrDefault(s => s.Time == scrollTime!.Value) : null;
        Formula.ShowTarget(_document, scrollTail ? null : current?.Key, current is null ? null : "Scroll " + ExactBarTimeText.Format(current.Time),
            current?.CurveToNext?.ToString() ?? "None", current?.CustomExpr);
    }

    private ExactBarTime? SelectedBpmTime =>
        BpmListBox.SelectedItem is ListBoxItem { Tag: ExactBarTime time } ? time : null;

    private ExactBarTime? SelectedScrollTime =>
        ScrollListBox.SelectedItem is ListBoxItem { Tag: ExactBarTime time } ? time : null;

    private void BpmSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (SelectedBpmTime is { } time && Chart?.Bpms.SingleOrDefault(item => item.Time == time) is { } bpm)
        {
            this.FindControl<TextBox>("BpmTimeValue")!.Text = ExactBarTimeText.Format(time);
            _loadedBpm = bpm.Bpm;
            _displayedBpm = DisplayValue(bpm.Bpm);
            this.FindControl<NumericUpDown>("BpmValueInput")!.Value = _displayedBpm;
            if (_document?.Selection is not { Kind: EditorSelectionKind.Bpm, Id: var current } || current != ExactBarTimeText.Format(time))
                RowSelectionChanged?.Invoke(new EditorSelection(EditorSelectionKind.Bpm, ExactBarTimeText.Format(time)));
        }
        else if (_document?.Selection is { Kind: EditorSelectionKind.Bpm })
        {
            RowSelectionChanged?.Invoke(EditorSelection.None);
        }
        ClearOtherTimingSelection(ScrollListBox);
        UpdateEnabledState();
        ClearError();
    }

    private void ScrollSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (SelectedScrollTime is { } time && Chart?.ScrollSpeeds.SingleOrDefault(item => item.Time == time) is { } scroll)
        {
            this.FindControl<TextBox>("ScrollTimeValue")!.Text = ExactBarTimeText.Format(time);
            _loadedScroll = scroll.Value;
            _displayedScroll = DisplayValue(scroll.Value);
            this.FindControl<NumericUpDown>("ScrollValueInput")!.Value = _displayedScroll;
            if (_document?.Selection is not { Kind: EditorSelectionKind.Scroll, Id: var current } || current != ExactBarTimeText.Format(time))
                RowSelectionChanged?.Invoke(new EditorSelection(EditorSelectionKind.Scroll, ExactBarTimeText.Format(time)));
        }
        else if (_document?.Selection is { Kind: EditorSelectionKind.Scroll })
        {
            RowSelectionChanged?.Invoke(EditorSelection.None);
        }
        ClearOtherTimingSelection(BpmListBox);
        UpdateEnabledState();
        ClearError();
    }

    internal void ClearRowSelection()
    {
        if (BpmListBox.SelectedItem is null && ScrollListBox.SelectedItem is null) return;
        _updating = true;
        try { BpmListBox.SelectedItem = ScrollListBox.SelectedItem = null; }
        finally { _updating = false; }
        UpdateEnabledState();
    }

    /// <summary>The two timing lists share one selection; keep the document mirror exclusive too.</summary>
    private void ClearOtherTimingSelection(ListBox other)
    {
        _updating = true;
        try { other.SelectedItem = null; }
        finally { _updating = false; }
    }

    private void AddBpm(object? sender, RoutedEventArgs e)
    {
        if (!RequireDocument() || !TryReadTime(this.FindControl<TextBox>("BpmTimeValue")!, out var time)) return;
        if (!TryReadBpm(out var bpm)) return;
        Raise(new AddBpmCommand(time, bpm));
    }

    private void ApplyBpm(object? sender, RoutedEventArgs e)
    {
        if (!RequireDocument() || SelectedBpmTime is not { } original) return;
        if (!TryReadTime(this.FindControl<TextBox>("BpmTimeValue")!, out var time)) return;
        if (!TryReadBpm(out var bpm, preserveLoaded: true)) return;
        Raise(new EditBpmCommand(original, time, bpm));
    }

    private void DeleteBpm(object? sender, RoutedEventArgs e)
    {
        if (!RequireDocument() || SelectedBpmTime is not { } time) return;
        Raise(new DeleteBpmCommand(time));
    }

    private void AddScroll(object? sender, RoutedEventArgs e)
    {
        if (!RequireDocument() || !TryReadTime(this.FindControl<TextBox>("ScrollTimeValue")!, out var time)) return;
        if (!TryReadScrollValue(out var value) || !TryReadCurve(time, out var curve) || !Formula.TryReadInput(out var expr)) return;
        if (Raise(new AddScrollCommand(time, value, curve, expr)) is { } error) ShowError(error);
    }

    private void ApplyScroll(object? sender, RoutedEventArgs e)
    {
        if (!RequireDocument() || SelectedScrollTime is not { } original) return;
        if (!TryReadTime(this.FindControl<TextBox>("ScrollTimeValue")!, out var time)) return;
        if (!TryReadScrollValue(out var value, preserveLoaded: true) || !TryReadCurve(time, out var curve)) return;
        Formula.Submit(expr => Raise(new EditScrollCommand(original, time, value, curve, expr)));
    }

    private void DeleteScroll(object? sender, RoutedEventArgs e)
    {
        if (!RequireDocument() || SelectedScrollTime is not { } time) return;
        Raise(new DeleteScrollCommand(time));
    }

    private bool RequireDocument()
    {
        if (Chart is not null) return true;
        ShowError(_text["Timing.Error.NoDocument"]);
        return false;
    }

    /// <summary>Empty time fields use the playhead anchor; continuous time is explicitly rounded on commit only.</summary>
    private bool TryReadTime(TextBox field, out ExactBarTime time)
    {
        if (string.IsNullOrWhiteSpace(field.Text))
        {
            if (_exactPlayhead is { } anchor) { time = anchor; return true; }
            if (ExactBarTimeText.TryParse(Math.Round(_currentBar, 6).ToString("0.######", CultureInfo.InvariantCulture), out time))
                return true;
        }
        else if (ExactBarTimeText.TryParse(field.Text, out time)) return true;
        ShowError(_text["Timing.Error.InvalidTime"]);
        return false;
    }

    // NumericUpDown uses decimal, while the chart domain uses all finite positive doubles.
    // Retain the loaded value when Apply changes only time/curve, including underflow and overflow.
    private static decimal DisplayValue(double value) => value >= (double)decimal.MaxValue
        ? decimal.MaxValue : value <= (double)decimal.MinValue ? decimal.MinValue : (decimal)value;

    private bool TryReadBpm(out double bpm, bool preserveLoaded = false)
    {
        var input = this.FindControl<NumericUpDown>("BpmValueInput")!.Value;
        bpm = preserveLoaded && _loadedBpm is { } original && input == _displayedBpm
            ? original : (double)(input ?? 0m);
        if (double.IsFinite(bpm) && bpm > 0) return true;
        ShowError(_text["Timing.Error.InvalidBpm"]);
        return false;
    }

    private bool TryReadScrollValue(out double value, bool preserveLoaded = false)
    {
        var input = this.FindControl<NumericUpDown>("ScrollValueInput")!.Value;
        value = preserveLoaded && _loadedScroll is { } original && input == _displayedScroll
            ? original : (double)(input ?? 0m);
        if (double.IsFinite(value) && value is > 0 and <= 64) return true;
        ShowError(_text["Timing.Error.InvalidScroll"]);
        return false;
    }

    private bool TryReadCurve(ExactBarTime time, out V2ScrollCurve? curve)
    {
        var tag = (this.FindControl<ComboBox>("ScrollCurveChoice")!.SelectedItem as ComboBoxItem)?.Tag as string;
        curve = Enum.TryParse<V2ScrollCurve>(tag, out var selected) ? selected : null;
        if (Chart?.ScrollSpeeds.Any(item => item.Time > time) != true)
        {
            if (curve == V2ScrollCurve.Custom) { ShowError(_text["Curve.Tail"]); return false; }
            curve = null;
        }
        return true;
    }

    private string? Raise(IEditorCommand command)
    {
        ClearError();
        return CommandRequested is null ? _text["Timing.Error.NoDocument"] : CommandRequested(command);
    }

    private void ShowError(string message)
    {
        ErrorBlock.Text = message;
        ErrorBlock.IsVisible = true;
        ErrorRequested?.Invoke(message);
    }

    private void ClearError()
    {
        ErrorBlock.Text = string.Empty;
        ErrorBlock.IsVisible = false;
    }
}
