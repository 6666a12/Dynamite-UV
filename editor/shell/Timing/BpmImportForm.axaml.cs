using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

public sealed record BpmImportPreviewRow(string Bar, string Bpm, string SourceMs, string SnapMs);

/// <summary>One cancellable preview. Source data is read-only; no chart changes until Apply.</summary>
public sealed partial class BpmImportForm : UserControl
{
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private readonly CancellationToken _lifetime;
    private readonly Action _close;
    private readonly Action<IReadOnlyList<V2BpmEvent>, double> _apply;
    private readonly double _currentOffset;
    private ImportedTiming? _raw, _snapped;
    private bool _osu, _busy, _ready;

    internal BpmImportForm(string chartId, double currentOffset, CancellationToken lifetime, Action close,
        Action<IReadOnlyList<V2BpmEvent>, double> apply)
    {
        _lifetime = lifetime;
        _close = close;
        _apply = apply;
        _currentOffset = currentOffset;
        AvaloniaXamlLoader.Load(this);
        C<TextBlock>("Target").Text = _text.Format("BpmImport.Target", chartId);
        _ready = true;
    }

    private T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
    private ImportedTiming? Selected => C<CheckBox>("Snap").IsChecked == true ? _snapped ?? _raw : _raw;

    private async void BrowseClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        SetBusy(true);
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage?.CanOpen != true) throw new InvalidOperationException(_text["BpmImport.NoPicker"]);
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = _text["BpmImport.Title"],
                FileTypeFilter = [new FilePickerFileType("Malody / osu! (*.mc, *.osu)") { Patterns = ["*.mc", "*.osu"] }],
            });
            if (files.Count == 0) return;
            using var file = files[0];
            if (!_lifetime.IsCancellationRequested) await LoadAsync(file.Name, file.OpenReadAsync);
        }
        catch (Exception ex) when (!_lifetime.IsCancellationRequested) { Error("BpmImport.ReadError", ex); }
        catch (Exception) when (_lifetime.IsCancellationRequested) { }
        finally { if (!_lifetime.IsCancellationRequested) SetBusy(false); }
    }

    internal async Task LoadAsync(string fileName, Func<Task<Stream>> open)
    {
        if (_lifetime.IsCancellationRequested) return;
        SetBusy(true);
        _raw = _snapped = null;
        C<TextBox>("ManualOffset").Text = "";
        C<TextBlock>("FileName").Text = fileName;
        C<TextBlock>("Feedback").Text = _text["BpmImport.Reading"];
        RefreshPreview();
        try
        {
            var extension = Path.GetExtension(fileName);
            _osu = extension.Equals(".osu", StringComparison.OrdinalIgnoreCase);
            using var input = await open();
            using var copy = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0,
                       (int)Math.Min(buffer.Length, TimingFileImport.MaxFileBytes - copy.Length + 1)), _lifetime)) != 0)
            {
                if (copy.Length + read > TimingFileImport.MaxFileBytes)
                    throw new InvalidDataException(_text["BpmImport.FileTooLarge"]);
                copy.Write(buffer, 0, read);
            }
            var (raw, snapped) = await Task.Run(() => ReadPreviews(copy, extension, _osu), _lifetime);
            if (_lifetime.IsCancellationRequested) return;
            (_raw, _snapped) = (raw, snapped);
            C<TextBlock>("Feedback").Text = "";
            RefreshPreview();
        }
        catch (Exception ex) when (!_lifetime.IsCancellationRequested) { Error("BpmImport.ReadError", ex); }
        catch (Exception) when (_lifetime.IsCancellationRequested) { }
        finally { if (!_lifetime.IsCancellationRequested) SetBusy(false); }
    }

    private (ImportedTiming Raw, ImportedTiming? Snapped) ReadPreviews(MemoryStream file, string extension, bool osu)
    {
        file.Position = 0;
        var raw = TimingFileImport.Read(file, extension, snapOsu: false);
        _lifetime.ThrowIfCancellationRequested();
        if (!osu) return (raw, null);
        file.Position = 0;
        try { return (raw, TimingFileImport.Read(file, extension)); }
        catch (InvalidDataException) { return (raw, null); }
    }

    private void RefreshPreview()
    {
        var result = Selected;
        C<StackPanel>("Preview").IsVisible = result is not null;
        C<ListBox>("Points").ItemsSource = null;
        if (result is null) { UpdateApply(); return; }

        C<CheckBox>("Snap").IsVisible = _osu && _snapped is not null;
        C<TextBlock>("Summary").Text = _osu
            ? _text.Format("BpmImport.OsuSummary", result.Points.Count, result.SnappedCount, result.MaximumSnapMilliseconds)
            : _text.Format("BpmImport.MalodySummary", result.Points.Count);
        C<TextBlock>("SourceAudio").Text = result.AudioFileName is { Length: > 0 } audio
            ? _text.Format("BpmImport.SourceAudio", audio) : "";
        C<TextBlock>("SourceAudio").IsVisible = result.AudioFileName is { Length: > 0 };
        C<TextBlock>("Offset").Text = result.AudioOffsetSec is { } offset
            ? _text.Format("BpmImport.Offset", ChartOffsetUnits.FormatMilliseconds(offset, signed: true))
            : _text.Format("BpmImport.ManualOffset", ChartOffsetUnits.FormatMilliseconds(_currentOffset, signed: true));
        C<Grid>("ManualOffsetPanel").IsVisible = result.NeedsManualOffset;
        C<TextBlock>("Warning").Text = string.Join(Environment.NewLine, new[]
        {
            result.HasNonFourMeter ? _text["BpmImport.Meter"] : null,
            _osu && _snapped is null ? _text["BpmImport.SnapUnavailable"] : null,
        }.OfType<string>());
        C<ListBox>("Points").ItemsSource = result.Points.Select(p => new BpmImportPreviewRow(
            ExactBarTimeText.Format(p.Time), p.Bpm.ToString("G", CultureInfo.InvariantCulture),
            p.SourceMilliseconds?.ToString("G", CultureInfo.InvariantCulture) ?? "—",
            p.SourceMilliseconds is null ? "—" : p.SnapMilliseconds.ToString("+0.######;-0.######;0", CultureInfo.InvariantCulture))).ToArray();
        UpdateApply();
    }

    private bool TryOffset(out double offset)
    {
        if (Selected?.AudioOffsetSec is { } value) { offset = value; return true; }
        return ChartOffsetUnits.TryParseMilliseconds(C<TextBox>("ManualOffset").Text, out offset);
    }

    private void UpdateApply() => C<Button>("ApplyButton").IsEnabled = !_busy && Selected is not null && TryOffset(out _);
    private void SetBusy(bool busy)
    {
        _busy = busy;
        C<Button>("BrowseButton").IsEnabled = !busy;
        UpdateApply();
    }

    private void Error(string key, Exception error) => C<TextBlock>("Feedback").Text = _text.Format(key, error.Message);
    private void SnapChanged(object? sender, RoutedEventArgs e) { if (_ready) RefreshPreview(); }
    private void OffsetChanged(object? sender, TextChangedEventArgs e) { if (_ready) UpdateApply(); }
    private void KeepOffsetClicked(object? sender, RoutedEventArgs e) =>
        C<TextBox>("ManualOffset").Text = ChartOffsetUnits.FormatMilliseconds(_currentOffset);
    private void CancelClicked(object? sender, RoutedEventArgs e) => _close();
    private void ApplyClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _lifetime.IsCancellationRequested || Selected is not { } result || !TryOffset(out var offset)) return;
        try
        {
            _apply(result.Points.Select(p => new V2BpmEvent(p.Time, p.Bpm)).ToArray(), offset);
            _close();
        }
        catch (Exception error) { Error("BpmImport.ApplyError", error); }
    }
}
