using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DynamiteUniverse.ChartEditor.Audio;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.ChartEditor.Localization;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Views;

public sealed partial class NewProjectPage : UserControl
{
    private bool _packIdEdited;
    private string _audioSourcePath = string.Empty;
    private string? _coverSourcePath;

    public NewProjectPage()
    {
        InitializeComponent();
        EditorLocalization.Current.LanguageChanged += (_, _) => UpdateForm();
        UpdateForm();
    }

    public event EventHandler? BackRequested;
    public event EventHandler<EditorProjectDraft>? CreateRequested;

    public void Reset()
    {
        _packIdEdited = false;
        _audioSourcePath = string.Empty;
        _coverSourcePath = null;
        TitleBox.Text = string.Empty;
        ArtistBox.Text = string.Empty;
        PackIdBox.Text = string.Empty;
        CoverPathBox.Text = string.Empty;
        ChartIdBox.Text = "hard";
        CharterBox.Text = string.Empty;
        DifficultyCombo.SelectedIndex = 2;
        DifficultyKeyBox.Text = string.Empty;
        LevelBox.Text = "10";
        UnratedCheck.IsChecked = false;
        AudioPathBox.Text = string.Empty;
        BpmBox.Text = "150";
        OffsetBox.Text = "0";
        GridCombo.SelectedIndex = 3;
        UpdateForm();
    }

    public void UseAudioSource(string path)
    {
        _audioSourcePath = Path.GetFullPath(path);
        AudioPathBox.Text = _audioSourcePath;
        UpdateForm();
    }

    public void UseCoverSource(string? path)
    {
        _coverSourcePath = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        CoverPathBox.Text = _coverSourcePath ?? string.Empty;
        UpdateForm();
    }

    private async void BrowseAudioClicked(object? sender, RoutedEventArgs e)
    {
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Choose chart audio",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Supported chart audio")
                {
                    Patterns = EditorAudioFormatRegistry.FilePickerPatterns,
                }],
            });
        if (files.Count == 1)
            UseAudioSource(files[0].TryGetLocalPath() ?? files[0].Path.LocalPath);
    }

    private async void BrowseCoverClicked(object? sender, RoutedEventArgs e)
    {
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Choose optional community cover",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Image") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }],
            });
        if (files.Count == 1)
            UseCoverSource(files[0].TryGetLocalPath() ?? files[0].Path.LocalPath);
    }

    private void BackClicked(object? sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void CreateClicked(object? sender, RoutedEventArgs e)
    {
        if (!TryBuildRequest(out var request))
        {
            FormStatusText.Text = EditorLocalization.Current.Get("NewProject.StatusIncomplete");
            return;
        }
        try
        {
            CreateRequested?.Invoke(this, EditorProjectDraftFactory.Create(
                request!, EditorAudioProbe.Instance));
        }
        catch (Exception exception)
        {
            FormStatusText.Text = exception.Message;
        }
    }

    private void AudioPathChanged(object? sender, TextChangedEventArgs e)
    {
        _audioSourcePath = (AudioPathBox.Text ?? string.Empty).Trim();
        UpdateForm();
    }

    private void CoverPathChanged(object? sender, TextChangedEventArgs e)
    {
        _coverSourcePath = NullIfEmpty(CoverPathBox.Text);
        UpdateForm();
    }

    private void IdentityChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_packIdEdited)
            PackIdBox.Text = SuggestId(TitleBox.Text);
        UpdateForm();
    }

    private void PackIdChanged(object? sender, TextChangedEventArgs e)
    {
        if (PackIdBox.IsFocused)
            _packIdEdited = true;
        UpdateForm();
    }

    private void FormChanged(object? sender, TextChangedEventArgs e) => UpdateForm();
    private void FormSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateForm();

    private void DifficultyChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CustomDifficultyPanel is null)
            return;
        CustomDifficultyPanel.IsVisible = SelectedDifficulty() == V2Difficulty.Custom;
        UpdateForm();
    }

    private void UnratedChanged(object? sender, RoutedEventArgs e)
    {
        if (LevelBox is null)
            return;
        LevelBox.IsEnabled = UnratedCheck.IsChecked != true;
        UpdateForm();
    }

    private void UpdateForm()
    {
        if (CreateButton is null)
            return;
        var valid = TryBuildRequest(out _, updateErrors: true);
        CreateButton.IsEnabled = valid;
        SummaryTitle.Text = string.IsNullOrWhiteSpace(TitleBox.Text)
            ? EditorLocalization.Current.Get("NewProject.Untitled")
            : TitleBox.Text!.Trim();
        SummaryArtist.Text = string.IsNullOrWhiteSpace(ArtistBox.Text)
            ? EditorLocalization.Current.Get("NewProject.ArtistNotSet")
            : ArtistBox.Text!.Trim();
        SummaryPackId.Text = string.IsNullOrWhiteSpace(PackIdBox.Text) ? "—" : PackIdBox.Text!.Trim();
        var difficulty = SelectedDifficulty();
        var difficultyText = difficulty == V2Difficulty.Custom
            ? (DifficultyKeyBox.Text ?? "CUSTOM").Trim().ToUpperInvariant()
            : difficulty.ToString().ToUpperInvariant();
        SummaryChart.Text = UnratedCheck.IsChecked == true
            ? $"{difficultyText} · {EditorLocalization.Current.Get("NewProject.Unrated")}"
            : $"{difficultyText} · Lv {(LevelBox.Text ?? "—").Trim()}";
        FormStatusText.Text = valid
            ? EditorLocalization.Current.Get("NewProject.StatusReady")
            : EditorLocalization.Current.Get("NewProject.StatusIncomplete");
    }

    private bool TryBuildRequest(out EditorProjectDraftRequest? request, bool updateErrors = true)
    {
        request = null;
        var title = (TitleBox.Text ?? string.Empty).Trim();
        var artist = (ArtistBox.Text ?? string.Empty).Trim();
        var packId = (PackIdBox.Text ?? string.Empty).Trim();
        var chartId = (ChartIdBox.Text ?? string.Empty).Trim();
        var charter = (CharterBox.Text ?? string.Empty).Trim();
        var difficulty = SelectedDifficulty();
        var difficultyKey = difficulty == V2Difficulty.Custom
            ? (DifficultyKeyBox.Text ?? string.Empty).Trim()
            : null;
        var unrated = UnratedCheck.IsChecked == true;

        var titleError = title.Length == 0 ? EditorLocalization.Current.Get("NewProject.ErrorTitleRequired") : null;
        var artistError = artist.Length == 0 ? EditorLocalization.Current.Get("NewProject.ErrorArtistRequired") : null;
        var packIdError = IsValidId(packId) ? null : EditorLocalization.Current.Get("NewProject.ErrorPackId");
        var chartIdError = IsValidId(chartId) ? null : EditorLocalization.Current.Get("NewProject.ErrorChartId");
        var charterError = charter.Length == 0 ? EditorLocalization.Current.Get("NewProject.ErrorCharterRequired") : null;
        var difficultyError = difficulty == V2Difficulty.Custom && string.IsNullOrWhiteSpace(difficultyKey)
            ? "Custom difficulty needs a display key."
            : null;
        var levelValue = 0;
        var levelValid = unrated || int.TryParse(LevelBox.Text, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out levelValue) && levelValue is >= 1 and <= 99;
        var levelError = levelValid ? null : EditorLocalization.Current.Get("NewProject.ErrorLevel");
        var bpmValid = double.TryParse(BpmBox.Text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var bpm) && double.IsFinite(bpm) && bpm > 0;
        var bpmError = bpmValid ? null : EditorLocalization.Current.Get("NewProject.ErrorBpm");
        var offsetValid = double.TryParse(OffsetBox.Text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var offsetMs) && double.IsFinite(offsetMs);
        var offsetError = offsetValid ? null : EditorLocalization.Current.Get("NewProject.ErrorOffset");
        var audioError = _audioSourcePath.Length == 0 ? "Choose a chart audio source file."
            : !EditorAudioFormatRegistry.TryGet(_audioSourcePath, out var audioFormat)
                ? "Use .wav, .mp3, .flac, .ogg, .opus, .m4a, or .aac audio."
                : audioFormat.Backend == EditorAudioBackend.WindowsMediaFoundation &&
                    !OperatingSystem.IsWindows()
                    ? $"{audioFormat.DisplayName} requires Windows Media Foundation."
                    : !File.Exists(_audioSourcePath)
                        ? "The selected audio file no longer exists."
                        : null;

        if (updateErrors)
        {
            TitleError.Text = titleError;
            ArtistError.Text = artistError;
            PackIdError.Text = packIdError;
            ChartIdError.Text = chartIdError;
            CharterError.Text = charterError;
            DifficultyKeyError.Text = difficultyError;
            LevelError.Text = levelError;
            BpmError.Text = bpmError;
            OffsetError.Text = offsetError;
            AudioError.Text = audioError;
        }

        if (new[] { titleError, artistError, packIdError, chartIdError, charterError,
                difficultyError, levelError, bpmError, offsetError, audioError }
            .Any(error => error is not null))
            return false;

        var grid = GridCombo.SelectedItem is ComboBoxItem { Tag: string gridText } &&
            int.TryParse(gridText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedGrid)
                ? parsedGrid
                : 16;
        request = new EditorProjectDraftRequest(packId, title, artist, chartId, difficulty,
            NullIfEmpty(difficultyKey), unrated ? null : levelValue, unrated, charter, bpm,
            offsetMs / 1000.0, grid, _audioSourcePath, _coverSourcePath);
        return true;
    }

    private V2Difficulty SelectedDifficulty()
    {
        if (DifficultyCombo?.SelectedItem is ComboBoxItem { Tag: string value } &&
            Enum.TryParse<V2Difficulty>(value, out var difficulty))
            return difficulty;
        return V2Difficulty.Hard;
    }

    private static bool IsValidId(string value) =>
        value.Length is >= 1 and <= 64 && IsAsciiAlphaNumeric(value[0]) &&
        value.Skip(1).All(character => IsAsciiAlphaNumeric(character) || character is '.' or '_' or '-');

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private static string SuggestId(string? title)
    {
        var value = (title ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder();
        var pendingDash = false;
        foreach (var character in value)
        {
            if (character <= 127 && char.IsAsciiLetterOrDigit(character))
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');
                builder.Append(char.ToLowerInvariant(character));
                pendingDash = false;
            }
            else if (character is ' ' or '-' or '_' or '.')
            {
                pendingDash = builder.Length > 0;
            }
        }
        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0)
            return string.Empty;
        return slug.Length <= 64 ? slug : slug[..64].TrimEnd('-');
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
