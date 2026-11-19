using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>One edit session for a new difficulty. The core owns media probing and document mutation.</summary>
public sealed partial class AddDifficultyForm : UserControl
{
    private readonly EditorDocumentSession _session;
    private readonly EditorDocument _document;
    private readonly EditableChart _reference;
    private readonly long _version;
    private readonly CancellationTokenSource _work;
    private readonly Func<AddDifficultyRequest, CancellationToken, Task<PreparedDifficulty>> _prepare;
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private bool _ready, _closed, _busy, _choosingFile;

    public event Action? DismissRequested;
    public bool Added { get; private set; }

    public AddDifficultyForm(EditorDocumentSession session, CancellationToken lifetime)
        : this(session, lifetime, (r, ct) => session.PrepareDifficultyAsync(r, cancellationToken: ct)) { }

    internal AddDifficultyForm(EditorDocumentSession session, CancellationToken lifetime,
        Func<AddDifficultyRequest, CancellationToken, Task<PreparedDifficulty>> prepare)
    {
        _session = session;
        _document = session.Document ?? throw new InvalidOperationException("No package is open.");
        _reference = _document.SelectedChart;
        _version = _document.ChangeVersion;
        _prepare = prepare;
        _work = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        AvaloniaXamlLoader.Load(this);
        var index = 2;
        while (_document.Charts.Any(chart => chart.Id == $"chart-{index}")) index++;
        C<TextBox>("ChartIdValue").Text = $"chart-{index}";
        C<TextBox>("CharterValue").Text = _reference.Charters.FirstOrDefault() ?? "";
        C<ComboBox>("DifficultyChoice").SelectedIndex = (int)_reference.Difficulty;
        C<NumericUpDown>("LevelValue").Value = _reference.Level ?? 12;
        C<CheckBox>("UnratedValue").IsChecked = _reference.Unrated;
        C<ComboBoxItem>("PackAudioOption").IsEnabled = _document.Audio is not null;
        C<ComboBox>("AudioChoice").SelectedIndex = (int)DifficultyAudioSource.Current;
        _ready = true;
        UpdateIdentity();
        UpdateSource();
        RefreshText();
    }

    public void Cancel() => Close(notify: true);

    private void Close(bool notify)
    {
        if (_closed) return;
        _closed = true;
        _work.Cancel();
        if (!_busy) _work.Dispose();
        if (notify) DismissRequested?.Invoke();
    }

    private T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
    private DifficultyAudioSource AudioSource => (DifficultyAudioSource)C<ComboBox>("AudioChoice").SelectedIndex;
    private bool PreviewEnabled => C<CheckBox>("PreviewOverride").IsChecked == true;
    private bool ReferenceUnchanged => ReferenceEquals(_session.Document, _document) &&
        _document.ChangeVersion == _version && _document.SelectedChartId == _reference.Id;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _text.LanguageChanged += RefreshText;
        RefreshText();
        Dispatcher.UIThread.Post(() => { if (!_closed) C<TextBox>("ChartIdValue").Focus(); }, DispatcherPriority.Input);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _text.LanguageChanged -= RefreshText;
        Close(notify: false);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Cancel(); return; }
        // Plain Enter keeps native behaviour for buttons, drop-downs, IME and numeric input.
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        { e.Handled = true; _ = SubmitAsync(); return; }
        base.OnKeyDown(e);
    }

    private void UpdateIdentity()
    {
        C<StackPanel>("CustomKeyField").IsVisible = C<ComboBox>("DifficultyChoice").SelectedIndex == (int)V2Difficulty.Custom;
        C<NumericUpDown>("LevelValue").IsEnabled = C<CheckBox>("UnratedValue").IsChecked != true;
    }

    private void UpdateSource()
    {
        bool current = AudioSource == DifficultyAudioSource.Current;
        bool same = current || AudioSource == DifficultyAudioSource.PackageDefault &&
            string.Equals(_reference.Audio ?? _document.Audio, _document.Audio, StringComparison.OrdinalIgnoreCase);
        C<Grid>("FileField").IsVisible = AudioSource == DifficultyAudioSource.External;
        C<ComboBox>("TimingChoice").SelectedIndex = same ? 1 : 0;
        // An explicit chart excerpt belongs to the reference. Package/external start by inheriting the pack.
        C<CheckBox>("PreviewOverride").IsChecked = current && _reference.Preview is not null;
        var preview = (current ? _reference.Preview : null) ?? _document.Preview;
        C<TextBox>("PreviewStart").Text = (preview?.StartSec ?? 0).ToString("R", CultureInfo.InvariantCulture);
        C<TextBox>("PreviewLength").Text = preview?.DurationSec.ToString("R", CultureInfo.InvariantCulture) ?? "";
        UpdateTiming();
        RefreshText();
    }

    private void UpdateTiming() => C<Grid>("NewTimingFields").IsVisible = C<ComboBox>("TimingChoice").SelectedIndex == 2;

    private void RefreshText()
    {
        if (!_ready || _closed) return;
        C<TextBlock>("ReferenceInfo").Text = _text.Format("AddForm.Reference", _reference.Id,
            _reference.Bpms.Count, _reference.AudioOffsetSec.ToString("R", CultureInfo.InvariantCulture));
        var recording = AudioSource switch
        {
            DifficultyAudioSource.Current => _reference.Audio ?? _document.Audio,
            DifficultyAudioSource.PackageDefault => _document.Audio,
            _ => C<TextBox>("AudioPathValue").Text?.Trim(),
        };
        // A draft or pending replacement can resolve a short package slot (e.g. audio.wav)
        // to a completely different source file. Show that source, not just the slot.
        string? sourceFile = null;
        if (AudioSource != DifficultyAudioSource.External && recording is not null)
            sourceFile = _document.PendingResourceReplacements.FirstOrDefault(m =>
                string.Equals(m.PackageRelativePath, recording, StringComparison.OrdinalIgnoreCase))?.ExternalSourcePath
                ?? _session.Draft?.ExternalResources.GetValueOrDefault(recording);
        C<TextBlock>("AudioInfo").Text = string.IsNullOrEmpty(recording)
            ? _text["AddForm.NoAudio"] : sourceFile is not null
                ? _text.Format("AddForm.ImportAudio", recording, sourceFile)
                : _text.Format("AddForm.EffectiveAudio", recording);
        C<Grid>("PreviewFields").IsVisible = PreviewEnabled;
        C<TextBlock>("PreviewInfo").Text = PreviewEnabled
            ? (TryPreview(out var p) ? _text.Format("AddForm.PreviewCustom", p.StartSec, p.DurationSec) : _text["AddForm.BadPreview"])
            : (_document.Preview is { } pack ? _text.Format("AddForm.PreviewPack", pack.StartSec, pack.DurationSec) : _text["AddForm.PreviewDefault"]);
    }

    private static bool Number(string? text, out double number) =>
        (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) && double.IsFinite(number);

    private bool TryPreview(out V2Preview p)
    {
        p = null!;
        if (!Number(C<TextBox>("PreviewStart").Text, out var start) || start < 0 ||
            !Number(C<TextBox>("PreviewLength").Text, out var length) || length <= 0) return false;
        p = new V2Preview(start, length);
        return true;
    }

    private AddDifficultyRequest ReadRequest()
    {
        string Field(string key) => C<TextBox>(key).Text?.Trim() ?? "";
        InvalidOperationException Error(string key) => new(_text["AddForm." + key]);
        if (!ReferenceUnchanged) throw Error("SourceChanged");
        var id = Field("ChartIdValue");
        if (id.Length is 0 or > 64 || !char.IsAsciiLetterOrDigit(id[0]) ||
            id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('.' or '_' or '-'))) throw Error("NeedId");
        if (_document.Charts.Any(c => c.Id == id)) throw Error("IdTaken");
        var difficulty = C<ComboBox>("DifficultyChoice").SelectedIndex;
        var charter = Field("CharterValue");
        if (charter.Length == 0) throw Error("NeedCharter");
        var custom = Field("CustomKeyValue");
        if (difficulty == (int)V2Difficulty.Custom &&
            (custom.EnumerateRunes().Count() is < 1 or > 24 || !custom.IsNormalized(NormalizationForm.FormC) ||
             custom.Any(char.IsControl) || _document.Charts.Any(c => c.Difficulty == V2Difficulty.Custom &&
                 string.Equals(c.DifficultyKey, custom, StringComparison.OrdinalIgnoreCase)))) throw Error("NeedCustomKey");
        var lv = C<NumericUpDown>("LevelValue").Value;
        var unrated = C<CheckBox>("UnratedValue").IsChecked == true;
        if (!unrated && (lv is null or < 1 or > 99 || lv != decimal.Truncate(lv.Value))) throw Error("NeedLevel");
        var file = AudioSource == DifficultyAudioSource.External ? Field("AudioPathValue") : null;
        if (file is not null && (file.Length == 0 || !Path.IsPathFullyQualified(file))) throw Error("NeedAudio");
        var timing = C<ComboBox>("TimingChoice").SelectedIndex;
        if (timing is not (1 or 2)) throw Error("NeedTiming");
        double bpm = 150, offset = 0;
        if (timing == 2 && (!Number(Field("BpmValue"), out bpm) || bpm <= 0 || !Number(Field("OffsetValue"), out offset)))
            throw Error("BadTiming");
        V2Preview? preview = null;
        if (PreviewEnabled && !TryPreview(out preview)) throw Error("BadPreview");
        var form = new EditorCreateForm(_document.PackId, _document.Title, _document.Artist, file ?? "",
            id, difficulty, custom, unrated ? null : (int)lv!.Value, unrated, charter, bpm, offset, null);
        return new AddDifficultyRequest(form, AudioSource, timing == 1, C<CheckBox>("CopyScroll").IsChecked == true, file, preview);
    }

    internal async Task SubmitAsync()
    {
        if (_busy || _closed || _work.IsCancellationRequested) return;
        _busy = true;
        C<Button>("AddButton").IsEnabled = C<StackPanel>("Fields").IsEnabled = false;
        var feedback = C<TextBlock>("Feedback");
        feedback.Text = _text["AddForm.Checking"];
        var token = _work.Token;
        try
        {
            var prepared = await _prepare(ReadRequest(), token);
            token.ThrowIfCancellationRequested();
            if (_closed) return;
            if (!ReferenceUnchanged) throw new InvalidOperationException(_text["AddForm.SourceChanged"]);
            _session.AddDifficulty(prepared);
            Added = true;
            Close(notify: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (!_closed && !token.IsCancellationRequested) feedback.Text = _text.Format("AddForm.PendingError", e.Message);
        }
        finally
        {
            _busy = false;
            if (_closed) _work.Dispose();
            else C<Button>("AddButton").IsEnabled = C<StackPanel>("Fields").IsEnabled = true;
        }
    }

    private void ConfirmClicked(object? sender, RoutedEventArgs e) => _ = SubmitAsync();
    private void CancelClicked(object? sender, RoutedEventArgs e) => Cancel();
    private void DifficultyChanged(object? sender, RoutedEventArgs e) { if (_ready) UpdateIdentity(); }
    private void TimingChanged(object? sender, SelectionChangedEventArgs e) { if (_ready) UpdateTiming(); }
    private void AudioChanged(object? sender, SelectionChangedEventArgs e) { if (_ready) UpdateSource(); }
    private void PreviewChanged(object? sender, RoutedEventArgs e) { if (_ready) RefreshText(); }
    private void ExternalChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_ready || AudioSource != DifficultyAudioSource.External) return;
        C<ComboBox>("TimingChoice").SelectedIndex = 0;
        RefreshText();
    }

    private async void BrowseAudio(object? sender, RoutedEventArgs e)
    {
        if (_choosingFile || _busy || _closed) return;
        _choosingFile = true;
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage?.CanOpen != true) throw new InvalidOperationException(_text["AddForm.AudioPath"]);
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false, Title = _text["AddForm.ExternalAudio"],
                FileTypeFilter = [new FilePickerFileType("WAV / MP3 / FLAC / Ogg Vorbis") { Patterns = ["*.wav", "*.mp3", "*.flac", "*.ogg"] }],
            });
            if (!_closed && !_work.IsCancellationRequested && files.Count > 0 && files[0].TryGetLocalPath() is { } path)
                C<TextBox>("AudioPathValue").Text = path;
        }
        catch (Exception error)
        {
            if (!_closed) C<TextBlock>("Feedback").Text = _text.Format("AddForm.PickerError", error.Message);
        }
        finally { _choosingFile = false; }
    }
}
