using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Editor.Shell.VideoExport;

public sealed partial class VideoExportSettingsDialog : Window
{
    private static readonly FilePickerFileType Mp4 = new("MPEG-4 (*.mp4)") { Patterns = ["*.mp4"] };
    private readonly IWorkbenchLocalizer _text;
    private readonly Func<string?, CancellationToken, Task<VideoExportCapabilities>> _probe, _probeExplicit;
    private VideoExportCapabilities? _capabilities;
    private CancellationTokenSource? _probeCancellation;
    internal Task ProbeCleanup { get; private set; } = Task.CompletedTask;
    private bool _ready, _closed, _pickerOpen;

    public VideoExportSettingsDialog(IWorkbenchLocalizer localizer, VideoExportSource source,
        string? configuredFfmpegPath, Func<string?, CancellationToken, Task<VideoExportCapabilities>> probe,
        Func<string?, CancellationToken, Task<VideoExportCapabilities>> probeExplicit)
    {
        (_text, _probe, _probeExplicit) = (localizer, probe, probeExplicit);
        AvaloniaXamlLoader.Load(this);
        C<TextBlock>("SourceText").Text = _text.Format("VideoExport.Source", source.Title, source.Difficulty);
        C<TextBlock>("UnsavedHint").IsVisible = source.HasUnsavedChanges;
        var preview = C<NumericUpDown>("PreviewValue");
        var max = double.IsFinite(source.PreviewMaximumSeconds) ? source.PreviewMaximumSeconds : 0;
        preview.Maximum = (decimal)Math.Clamp(max, 0, (double)VideoExportChoices.MaximumSegmentSeconds);
        preview.Value = Math.Min(2m, preview.Maximum);
        preview.IsEnabled = preview.Maximum > 0;
        C<TextBlock>("PreviewHint").Text = preview.IsEnabled
            ? _text.Format("VideoExport.PreviewHint", preview.Maximum)
            : _text["VideoExport.PreviewUnavailable"];
        C<TextBox>("OutputValue").Text = source.SuggestedOutputPath;
        var speed = C<ComboBox>("FallSpeedValue");
        speed.ItemsSource = Enumerable.Range(GameplayFallSpeed.MinimumLevel, GameplayFallSpeed.MaximumLevel - GameplayFallSpeed.MinimumLevel + 1)
            .Select(level => _text.Format("VideoExport.FallSpeedChoice", GameplayFallSpeed.Multiplier(level))).ToArray();
        speed.SelectedIndex = GameplayFallSpeed.Clamp(source.FallSpeedLevel) - GameplayFallSpeed.MinimumLevel;
        _ready = true;
        ApplyCodec();
        Opened += (_, _) => StartProbe(configuredFfmpegPath);
        Closed += (_, _) => { _closed = true; _probeCancellation?.Cancel(); };
    }

    private T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
    private VideoExportCodec Codec => VideoExportChoices.Codecs[Math.Max(0, C<ComboBox>("CodecValue").SelectedIndex)];

    private Task StartProbe(string? path, bool explicitPath = false) => ProbeCleanup = ProbeAsync(path, explicitPath);

    private async Task ProbeAsync(string? path, bool explicitPath)
    {
        using var cancellation = new CancellationTokenSource();
        _probeCancellation = cancellation;
        _capabilities = null;
        Refresh();
        try { _capabilities = await (explicitPath ? _probeExplicit : _probe)(path, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { _capabilities = new(null, [], error.Message); }
        finally
        {
            _probeCancellation = null;
            if (!_closed) Refresh();
        }
    }

    private void CodecChanged(object? sender, SelectionChangedEventArgs e) { if (_ready) ApplyCodec(); }

    private void ApplyCodec()
    {
        var codec = Codec;
        var quality = C<NumericUpDown>("CrfValue");
        quality.Minimum = codec.MinCrf;
        quality.Maximum = codec.MaxCrf;
        quality.Value = codec.DefaultCrf;
        C<TextBlock>("CrfHint").Text = _text.Format("VideoExport.CrfHint", codec.DefaultCrf, codec.MinCrf, codec.MaxCrf);
        Refresh();
    }

    private void OptionsChanged(object? sender, RoutedEventArgs e) { if (_ready) Refresh(); }

    private void Refresh()
    {
        var available = _capabilities is { FfmpegPath.Length: > 0 } capabilities
            ? VideoExportChoices.Codecs.Where(codec => capabilities.Codecs.Contains(codec.Id)).ToArray()
            : [];
        var selected = available.Any(codec => codec.Id == Codec.Id);
        var probing = _probeCancellation is not null;
        C<TextBlock>("EncoderStatus").Text = probing ? _text["VideoExport.Checking"] : available.Length > 0
            ? _text.Format("VideoExport.Available", string.Join(", ", available.Select(codec => _text[codec.Label])))
            : _capabilities?.Error is { Length: > 0 } error ? _text.Format("VideoExport.EncoderError", error)
            : _text["VideoExport.EncoderMissing"];
        var path = C<TextBlock>("EncoderPath");
        path.Text = _capabilities?.FfmpegPath;
        path.IsVisible = !string.IsNullOrWhiteSpace(path.Text);
        ToolTip.SetTip(path, path.Text);
        C<TextBlock>("CodecHint").Text = !probing && available.Length > 0 && !selected ? _text["VideoExport.CodecUnavailable"] : "";
        C<Button>("LocateButton").IsEnabled = !probing && !_pickerOpen;
        C<Button>("BrowseOutputButton").IsEnabled = !_pickerOpen;
        var validOutput = TryOutput(out _);
        SetOutputHint(validOutput ? "" : _text["VideoExport.InvalidOutput"]);
        C<Button>("StartButton").IsEnabled = selected && !_pickerOpen && validOutput
            && C<NumericUpDown>("PreviewValue").Value is not null && C<NumericUpDown>("ResultHoldValue").Value is not null
            && C<ComboBox>("FallSpeedValue").SelectedIndex >= 0 && TryCrf(out _);
    }

    private bool TryOutput(out string output)
    {
        output = C<TextBox>("OutputValue").Text?.Trim() ?? "";
        if (!Path.IsPathFullyQualified(output) || !Path.GetExtension(output).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            output = Path.GetFullPath(output);
            var fileName = Path.GetFileNameWithoutExtension(output);
            return !string.IsNullOrWhiteSpace(fileName) && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private async void BrowseOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (_pickerOpen) return;
        _pickerOpen = true;
        Refresh();
        try
        {
            if (!StorageProvider.CanSave) throw new InvalidOperationException(_text["VideoExport.NoPicker"]);
            var fileName = TryOutput(out var output) ? Path.GetFileName(output) : "chart.mp4";
            using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = _text["VideoExport.Output"], SuggestedFileName = fileName,
                DefaultExtension = "mp4", FileTypeChoices = [Mp4], ShowOverwritePrompt = false,
            });
            if (!_closed && file?.TryGetLocalPath() is { } path) C<TextBox>("OutputValue").Text = path;
        }
        catch (Exception error) { if (!_closed) SetPickerError(error); }
        finally { _pickerOpen = false; if (!_closed) RefreshButtonsAfterPicker(); }
    }

    private async void LocateClicked(object? sender, RoutedEventArgs e)
    {
        if (_pickerOpen || _probeCancellation is not null) return;
        _pickerOpen = true;
        Refresh();
        try
        {
            if (!StorageProvider.CanOpen) throw new InvalidOperationException(_text["VideoExport.NoPicker"]);
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _text["VideoExport.Locate"], AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("FFmpeg (ffmpeg.exe)") { Patterns = ["ffmpeg.exe"] }],
            });
            if (files.Count == 0) return;
            using var file = files[0];
            if (!_closed && file.TryGetLocalPath() is { } path) await StartProbe(path, explicitPath: true);
        }
        catch (Exception error) { if (!_closed) SetPickerError(error); }
        finally { _pickerOpen = false; if (!_closed) RefreshButtonsAfterPicker(); }
    }

    private void RefreshButtonsAfterPicker()
    {
        var previousHint = C<TextBlock>("OutputHint").Text;
        Refresh();
        if (!string.IsNullOrEmpty(previousHint)) SetOutputHint(previousHint);
    }

    private void SetPickerError(Exception error) => SetOutputHint(_text.Format("VideoExport.PickerError", error.Message));
    private void SetOutputHint(string hint)
    {
        var block = C<TextBlock>("OutputHint");
        block.Text = hint;
        block.IsVisible = hint.Length > 0;
    }

    private bool TryCrf(out int crf)
    {
        crf = 0;
        var codec = Codec;
        if (C<NumericUpDown>("CrfValue").Value is not { } value || value != decimal.Truncate(value)
            || value < codec.MinCrf || value > codec.MaxCrf) return false;
        crf = (int)value;
        return true;
    }

    private void StartClicked(object? sender, RoutedEventArgs e)
    {
        if (!C<Button>("StartButton").IsEnabled || !TryOutput(out var output) || !TryCrf(out var crf)
            || _capabilities?.FfmpegPath is not { Length: > 0 } ffmpeg
            || C<NumericUpDown>("PreviewValue").Value is not { } preview || C<NumericUpDown>("ResultHoldValue").Value is not { } hold) return;
        if (!Directory.Exists(Path.GetDirectoryName(output))) { SetOutputHint(_text["VideoExport.OutputDirectoryMissing"]); return; }
        if (File.Exists(output) || Directory.Exists(output)) { SetOutputHint(_text["VideoExport.OutputExists"]); return; }
        var preset = C<ComboBox>("PresetValue").SelectedIndex;
        var speed = C<ComboBox>("FallSpeedValue").SelectedIndex + GameplayFallSpeed.MinimumLevel;
        if (!GameplayFallSpeed.IsValid(speed)) return;
        Close(new VideoExportOptions(output, ffmpeg, Codec.Id, VideoExportChoices.Presets[Math.Max(0, preset)], crf,
            (double)preview, (double)hold, speed));
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
