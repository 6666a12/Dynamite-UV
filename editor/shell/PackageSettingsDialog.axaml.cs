using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

public sealed partial class PackageSettingsDialog : Window
{
    private static readonly FilePickerFileType AudioFiles = new("Audio")
    {
        Patterns = new[] { "*.wav", "*.mp3", "*.flac", "*.ogg" },
    };
    private static readonly FilePickerFileType ImageFiles = new("Images")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" },
    };
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private readonly Dictionary<string, (double Original, decimal? Displayed)> _numbers = new();
    private readonly EditableChart _chart;
    private readonly V2Preview? _packPreview;
    private bool _packPreviewEdited;
    private string? _packAudioSource;
    private string? _packCoverSource;
    private string? _chartAudioSource;

    public PackageSettingsDialog()
    {
        AvaloniaXamlLoader.Load(this);
        _chart = null!;
    }

    public PackageSettingsDialog(EditorDocument document) : this()
    {
        _chart = document.SelectedChart;
        this.FindControl<TextBox>("PackIdValue")!.Text = document.PackId;
        this.FindControl<TextBox>("TitleValue")!.Text = document.Title;
        this.FindControl<TextBox>("ArtistValue")!.Text = document.Artist;
        this.FindControl<TextBox>("PackAudioPathValue")!.Text = document.Audio ?? "";
        this.FindControl<TextBox>("PackCoverPathValue")!.Text = document.Cover ?? "";
        var packPreview = document.Preview ?? new V2Preview(0, 15);
        _packPreview = document.Preview;
        LoadNumber("PackPreviewStartValue", packPreview.StartSec);
        LoadNumber("PackPreviewDurationValue", packPreview.DurationSec);
        this.FindControl<NumericUpDown>("PackPreviewStartValue")!.ValueChanged += PackPreviewEdited;
        this.FindControl<NumericUpDown>("PackPreviewDurationValue")!.ValueChanged += PackPreviewEdited;

        this.FindControl<TextBox>("ChartIdValue")!.Text = _chart.Id;
        this.FindControl<TextBox>("ChartFileValue")!.Text = _chart.File;
        this.FindControl<ComboBox>("DifficultyChoice")!.SelectedIndex = (int)_chart.Difficulty;
        this.FindControl<TextBox>("CustomKeyValue")!.Text = _chart.DifficultyKey ?? "";
        this.FindControl<NumericUpDown>("LevelValue")!.Value = _chart.Level ?? 12;
        this.FindControl<CheckBox>("UnratedValue")!.IsChecked = _chart.Unrated;
        this.FindControl<TextBox>("ChartersValue")!.Text = string.Join(", ", _chart.Charters);
        LoadNumber("OffsetValue", _chart.AudioOffsetSec);
        this.FindControl<TextBox>("ChartAudioPathValue")!.Text = _chart.Audio ?? "";
        this.FindControl<CheckBox>("ChartAudioEnabledValue")!.IsChecked = _chart.Audio is not null;
        var chartPreview = _chart.Preview ?? packPreview;
        this.FindControl<CheckBox>("ChartPreviewEnabledValue")!.IsChecked = _chart.Preview is not null;
        LoadNumber("ChartPreviewStartValue", chartPreview.StartSec);
        LoadNumber("ChartPreviewDurationValue", chartPreview.DurationSec);
        UpdateOptionalFields();
    }

    public EditorPackageSettingsRequest? Result { get; private set; }

    private EditorPackageSettingsRequest BuildRequest()
    {
        var charters = this.FindControl<TextBox>("ChartersValue")!.Text?
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray() ?? Array.Empty<string>();
        var chartPreviewEnabled = this.FindControl<CheckBox>("ChartPreviewEnabledValue")!.IsChecked == true;
        var chartAudioEnabled = this.FindControl<CheckBox>("ChartAudioEnabledValue")!.IsChecked == true;
        return new EditorPackageSettingsRequest(
            this.FindControl<TextBox>("PackIdValue")!.Text?.Trim() ?? "",
            this.FindControl<TextBox>("TitleValue")!.Text?.Trim() ?? "",
            this.FindControl<TextBox>("ArtistValue")!.Text?.Trim() ?? "",
            this.FindControl<TextBox>("ChartIdValue")!.Text?.Trim() ?? "",
            this.FindControl<TextBox>("ChartFileValue")!.Text?.Trim() ?? "",
            (V2Difficulty)this.FindControl<ComboBox>("DifficultyChoice")!.SelectedIndex,
            this.FindControl<TextBox>("CustomKeyValue")!.Text?.Trim(),
            (int?)this.FindControl<NumericUpDown>("LevelValue")!.Value,
            this.FindControl<CheckBox>("UnratedValue")!.IsChecked == true,
            charters,
            ReadNumber("OffsetValue", 0),
            ReadPackPreview(),
            chartPreviewEnabled ? Preview("ChartPreview") : null,
            _packAudioSource,
            _packCoverSource,
            // An unchecked override never submits its browsed source; clearing wins by contract.
            chartAudioEnabled ? _chartAudioSource : null,
            !chartAudioEnabled,
            !chartPreviewEnabled);
    }

    /// <summary>
    /// A package without authored preview metadata keeps none: the (0, 15) display fallback is not
    /// metadata, and submitting it would fabricate a preview (and can fail the next save's
    /// resolved-preview duration check on short audio). Only an explicit edit is submitted.
    /// </summary>
    private V2Preview? ReadPackPreview()
    {
        var value = Preview("PackPreview");
        return _packPreview is null && !_packPreviewEdited ? null : value;
    }

    private void PackPreviewEdited(object? sender, NumericUpDownValueChangedEventArgs e) =>
        _packPreviewEdited = true;

    private void LoadNumber(string name, double value)
    {
        var control = this.FindControl<NumericUpDown>(name)!;
        control.Value = (decimal)Math.Clamp(value, (double)control.Minimum, (double)control.Maximum);
        _numbers[name] = (value, control.Value);
    }

    private double ReadNumber(string name, decimal fallback)
    {
        var value = this.FindControl<NumericUpDown>(name)!.Value;
        return _numbers.TryGetValue(name, out var loaded) && value == loaded.Displayed
            ? loaded.Original : (double)(value ?? fallback);
    }

    internal EditorPackageSettingsRequest RequestForVerification() => BuildRequest();

    private V2Preview Preview(string prefix) => new(
        ReadNumber(prefix + "StartValue", 0), ReadNumber(prefix + "DurationValue", 15));

    private void Confirm(object? sender, RoutedEventArgs e)
    {
        try
        {
            Result = BuildRequest();
            if (Result.Charters.Count == 0) throw new InvalidOperationException(_text["Create.Charter"]);
            if (this.FindControl<CheckBox>("ChartAudioEnabledValue")!.IsChecked == true &&
                _chart.Audio is null && _chartAudioSource is null)
                throw new InvalidOperationException(_text["Settings.ChartAudioRequired"]);
            Close(Result);
        }
        catch (Exception ex)
        {
            this.FindControl<TextBlock>("ErrorText")!.Text = ex.Message;
            this.FindControl<TextBlock>("ErrorText")!.IsVisible = true;
        }
    }

    private void UpdateOptionalFields()
    {
        this.FindControl<StackPanel>("CustomKeyField")!.IsVisible =
            this.FindControl<ComboBox>("DifficultyChoice")!.SelectedIndex == (int)V2Difficulty.Custom;
        this.FindControl<NumericUpDown>("LevelValue")!.IsEnabled =
            this.FindControl<CheckBox>("UnratedValue")!.IsChecked != true;
        this.FindControl<Grid>("ChartPreviewFields")!.IsEnabled =
            this.FindControl<CheckBox>("ChartPreviewEnabledValue")!.IsChecked == true;
        this.FindControl<Grid>("ChartAudioFields")!.IsEnabled =
            this.FindControl<CheckBox>("ChartAudioEnabledValue")!.IsChecked == true;
    }

    private async Task PickFile(string kind, FilePickerFileType filter)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { filter },
            Title = _text["Create.Browse"],
        });
        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        switch (kind)
        {
            case "packAudio": _packAudioSource = path; this.FindControl<TextBox>("PackAudioPathValue")!.Text = path; break;
            case "packCover": _packCoverSource = path; this.FindControl<TextBox>("PackCoverPathValue")!.Text = path; break;
            default: _chartAudioSource = path; this.FindControl<TextBox>("ChartAudioPathValue")!.Text = path; break;
        }
    }

    private void DifficultyChanged(object? sender, SelectionChangedEventArgs e) => UpdateOptionalFields();
    private void UnratedChanged(object? sender, RoutedEventArgs e) => UpdateOptionalFields();
    private void ChartPreviewChanged(object? sender, RoutedEventArgs e) => UpdateOptionalFields();
    private void ChartAudioChanged(object? sender, RoutedEventArgs e) => UpdateOptionalFields();
    private async void BrowsePackAudio(object? sender, RoutedEventArgs e) => await PickFile("packAudio", AudioFiles);
    private async void BrowsePackCover(object? sender, RoutedEventArgs e) => await PickFile("packCover", ImageFiles);
    private async void BrowseChartAudio(object? sender, RoutedEventArgs e) => await PickFile("chartAudio", AudioFiles);
    private void Cancel(object? sender, RoutedEventArgs e) => Close(null);
}
