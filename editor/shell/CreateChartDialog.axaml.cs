using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor.Shell;

public sealed partial class CreateChartDialog : Window
{
    private static readonly FilePickerFileType AudioFiles = new("Audio")
    {
        Patterns = new[] { "*.wav", "*.mp3", "*.flac", "*.ogg" },
    };
    private static readonly FilePickerFileType ImageFiles = new("Images")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" },
    };
    private readonly EditorCreateMode _mode;
    private readonly EditorDocument? _document;
    private readonly int _gridDivisor;
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private bool _loaded;

    public CreateChartDialog() : this(EditorCreateMode.NewPackage, null, EditorDocument.DefaultGridDivisor) { }

    public CreateChartDialog(EditorCreateMode mode, EditorDocument? document, int gridDivisor)
    {
        _mode = mode;
        _document = document;
        _gridDivisor = gridDivisor;
        AvaloniaXamlLoader.Load(this);
        _loaded = true;
        var creating = mode == EditorCreateMode.NewPackage;
        this.FindControl<StackPanel>("PackageFields")!.IsVisible = creating;
        this.FindControl<Grid>("TimingFields")!.IsVisible = creating;
        this.FindControl<StackPanel>("AddCharterField")!.IsVisible = !creating;
        this.FindControl<TextBlock>("DialogTitle")!.Text = _text[creating ? "Create.NewTitle" : "Create.AddTitle"];
        this.FindControl<TextBlock>("DialogSubtitle")!.Text = _text[creating ? "Create.NewSubtitle" : "Create.AddSubtitle"];
        this.FindControl<Button>("ConfirmButton")!.Content = _text[creating ? "Create.Create" : "Create.Add"];
        this.FindControl<TextBlock>("SaveHint")?.IsVisible = creating;
        if (creating && document is not null)
        {
            this.FindControl<TextBox>("CharterValue")!.Text = document.SelectedChart.Charters.FirstOrDefault() ?? "Community";
        }
        if (!creating && document is not null)
        {
            this.FindControl<TextBox>("AddCharterValue")!.Text = document.SelectedChart.Charters.FirstOrDefault() ?? "Community";
            this.FindControl<TextBox>("ChartIdValue")!.Text = NextChartId(document);
        }
    }

    public EditorCreateResult? Result { get; private set; }

    internal void SetFormForVerification(EditorCreateForm form)
    {
        this.FindControl<TextBox>("PackIdValue")!.Text = form.PackId;
        this.FindControl<TextBox>("TitleValue")!.Text = form.Title;
        this.FindControl<TextBox>("ArtistValue")!.Text = form.Artist;
        this.FindControl<TextBox>("AudioPathValue")!.Text = form.AudioPath;
        this.FindControl<TextBox>("CoverPathValue")!.Text = form.CoverPath ?? "";
        this.FindControl<TextBox>("ChartIdValue")!.Text = form.ChartId;
        this.FindControl<ComboBox>("DifficultyChoice")!.SelectedIndex = form.DifficultyIndex;
        this.FindControl<TextBox>("CustomKeyValue")!.Text = form.CustomKey;
        this.FindControl<NumericUpDown>("LevelValue")!.Value = form.Level;
        this.FindControl<CheckBox>("UnratedValue")!.IsChecked = form.Unrated;
        this.FindControl<TextBox>("CharterValue")!.Text = form.Charter;
        this.FindControl<TextBox>("AddCharterValue")!.Text = form.Charter;
        this.FindControl<NumericUpDown>("BpmValue")!.Value = (decimal)form.Bpm;
        this.FindControl<NumericUpDown>("OffsetValue")!.Value = (decimal)form.OffsetSec;
    }

    internal EditorCreateResult? ConfirmForVerification() => BuildResult();

    private EditorCreateForm BuildForm()
    {
        var creating = _mode == EditorCreateMode.NewPackage;
        return new EditorCreateForm(
            this.FindControl<TextBox>("PackIdValue")!.Text?.Trim() ?? "",
            this.FindControl<TextBox>("TitleValue")!.Text?.Trim() ?? "",
            this.FindControl<TextBox>("ArtistValue")!.Text?.Trim() ?? "",
            this.FindControl<TextBox>("AudioPathValue")!.Text?.Trim() ?? "",
            this.FindControl<TextBox>("ChartIdValue")!.Text?.Trim() ?? "",
            this.FindControl<ComboBox>("DifficultyChoice")!.SelectedIndex,
            this.FindControl<TextBox>("CustomKeyValue")!.Text?.Trim() ?? "",
            (int?)(this.FindControl<NumericUpDown>("LevelValue")!.Value ?? 12),
            this.FindControl<CheckBox>("UnratedValue")!.IsChecked == true,
            this.FindControl<TextBox>(creating ? "CharterValue" : "AddCharterValue")!.Text?.Trim() ?? "",
            (double)(this.FindControl<NumericUpDown>("BpmValue")!.Value ?? 150),
            (double)(this.FindControl<NumericUpDown>("OffsetValue")!.Value ?? 0),
            this.FindControl<TextBox>("CoverPathValue")!.Text?.Trim() is { Length: > 0 } cover ? cover : null);
    }

    private EditorCreateResult? BuildResult()
    {
        var form = BuildForm();
        var package = _document is null ? null : new EditorCreatePackageSummary(
            _document.Charts.Select(chart => chart.Id).ToArray(),
            _document.Charts.Select(chart => new EditorCreateChartSummary(chart.Difficulty, chart.DifficultyKey)).ToArray());
        var error = EditorCreatePreflight.Validate(_mode, form, package,
            File.Exists(form.AudioPath), form.CoverPath is null || File.Exists(form.CoverPath));
        try
        {
            if (error is not null) throw new InvalidOperationException(error);
            Result = EditorCreateRequest.Build(_mode, form, _gridDivisor);
            Close(Result);
            return Result;
        }
        catch (Exception ex)
        {
            this.FindControl<TextBlock>("ErrorText")!.Text = ex.Message;
            this.FindControl<TextBlock>("ErrorText")!.IsVisible = true;
            return null;
        }
    }

    private async Task PickFile(TextBox target, FilePickerFileType filter)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { filter },
            Title = _text["Create.Browse"],
        });
        if (files.Count > 0) target.Text = files[0].Path.LocalPath;
    }

    private void DifficultyChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loaded)
            this.FindControl<StackPanel>("CustomKeyField")!.IsVisible =
                this.FindControl<ComboBox>("DifficultyChoice")!.SelectedIndex == 6;
    }

    private async void BrowseAudio(object? sender, RoutedEventArgs e) => await PickFile(this.FindControl<TextBox>("AudioPathValue")!, AudioFiles);
    private async void BrowseCover(object? sender, RoutedEventArgs e) => await PickFile(this.FindControl<TextBox>("CoverPathValue")!, ImageFiles);
    private void Confirm(object? sender, RoutedEventArgs e) => BuildResult();
    private void Cancel(object? sender, RoutedEventArgs e) => Close(null);

    private static string NextChartId(EditorDocument document)
    {
        for (var index = 2; ; index++)
        {
            var id = $"chart-{index}";
            if (document.Charts.All(chart => chart.Id != id)) return id;
        }
    }
}
