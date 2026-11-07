using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>
/// Compact BPM and Scroll event editor for the selected chart. The view never executes commands
/// and never owns saving; it only raises <see cref="CommandRequested"/> with core
/// <see cref="IEditorCommand"/> instances and reports invalid input through
/// <see cref="ErrorRequested"/> plus an inline status line.
/// </summary>
public sealed partial class TimingView : UserControl
{
    private static readonly V2ScrollCurve[] ScrollCurves = Enum.GetValues<V2ScrollCurve>();

    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private EditorDocument? _document;
    private double _currentBar;
    private bool _updating;
    private double? _loadedBpm;
    private decimal? _displayedBpm;
    private double? _loadedScroll;
    private decimal? _displayedScroll;

    public event Action<IEditorCommand>? CommandRequested;
    public event Action<string>? ErrorRequested;

    public TimingView()
    {
        AvaloniaXamlLoader.Load(this);
        _text.LanguageChanged += OnLanguageChanged;
        UpdateView();
    }

    /// <summary>Swaps the document shown by the panel; call again after every executed command.</summary>
    public void SetDocument(EditorDocument? document)
    {
        _document = document;
        UpdateView();
    }

    /// <summary>Keeps the playhead bar in sync; empty add-time fields default to this bar.</summary>
    public void SetCurrentBar(double bar)
    {
        _currentBar = bar;
        UpdatePlayheadCaption();
    }

    private EditableChart? Chart => _document?.SelectedChart;
    private ListBox BpmListBox => this.FindControl<ListBox>("BpmList")!;
    private ListBox ScrollListBox => this.FindControl<ListBox>("ScrollList")!;
    private TextBlock ErrorBlock => this.FindControl<TextBlock>("TimingError")!;

    private void OnLanguageChanged()
    {
        UpdatePlayheadCaption();
        ClearError();
    }

    private void UpdatePlayheadCaption()
    {
        this.FindControl<TextBlock>("PlayheadCaption")!.Text =
            _text.Format("Timing.PlayheadCaption", _currentBar.ToString("0.##", CultureInfo.InvariantCulture));
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
            _document?.Select(new EditorSelection(EditorSelectionKind.Bpm, ExactBarTimeText.Format(time)));
        }
        else if (_document?.Selection is { Kind: EditorSelectionKind.Bpm })
        {
            _document.Select(EditorSelection.None);
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
            var curve = scroll.CurveToNext;
            this.FindControl<ComboBox>("ScrollCurveChoice")!.SelectedIndex =
                curve is null ? 0 : Array.IndexOf(ScrollCurves, curve.Value) + 1;
            _document?.Select(new EditorSelection(EditorSelectionKind.Scroll, ExactBarTimeText.Format(time)));
        }
        else if (_document?.Selection is { Kind: EditorSelectionKind.Scroll })
        {
            _document.Select(EditorSelection.None);
        }
        ClearOtherTimingSelection(BpmListBox);
        UpdateEnabledState();
        ClearError();
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
        if (!TryReadScrollValue(out var value)) return;
        Raise(new AddScrollCommand(time, value, ReadScrollCurve(time)));
    }

    private void ApplyScroll(object? sender, RoutedEventArgs e)
    {
        if (!RequireDocument() || SelectedScrollTime is not { } original) return;
        if (!TryReadTime(this.FindControl<TextBox>("ScrollTimeValue")!, out var time)) return;
        if (!TryReadScrollValue(out var value, preserveLoaded: true)) return;
        Raise(new EditScrollCommand(original, time, value, ReadScrollCurve(time)));
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

    /// <summary>Empty time fields default to the playhead bar, parsed exactly in the BarTime domain.</summary>
    private bool TryReadTime(TextBox field, out ExactBarTime time)
    {
        if (string.IsNullOrWhiteSpace(field.Text))
        {
            time = ExactBarTimeText.Parse(Math.Round(_currentBar, 6).ToString("0.######", CultureInfo.InvariantCulture));
            return true;
        }
        if (ExactBarTimeText.TryParse(field.Text, out time)) return true;
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

    private V2ScrollCurve? ReadScrollCurve(ExactBarTime time)
    {
        if (Chart is null) return null;
        return Chart.ScrollSpeeds.Any(item => item.Time > time) ? ReadScrollCurve() : null;
    }

    private V2ScrollCurve? ReadScrollCurve()
    {
        var index = this.FindControl<ComboBox>("ScrollCurveChoice")!.SelectedIndex;
        return index <= 0 ? null : ScrollCurves[index - 1];
    }

    private void Raise(IEditorCommand command)
    {
        ClearError();
        CommandRequested?.Invoke(command);
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
