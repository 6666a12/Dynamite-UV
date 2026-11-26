using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace DynamiteUniverse.Editor.Shell;

public sealed partial class CoverStudioWindow : Window
{
    private enum GestureKind { CropMove, CropSize, Frame, Badge }
    private sealed record Gesture(IPointer Pointer, CoverCanvas Canvas, GestureKind Kind, Point Origin,
        CoverLayout Before, string? BadgeId = null);
    private readonly CoverHistory _history;
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private readonly CoverCanvas _crop = new() { Focusable = true }, _thumb = new(), _wide = new() { Focusable = true };
    private readonly string? _packageDirectory;
    private Bitmap? _image;
    private string? _source;
    private string? _selected;
    private sealed record BadgeRow(Expander Section, TextBlock Marker, TextBlock Label);
    private readonly Dictionary<string, BadgeRow> _badgeRows = [];
    private Expander? _activeBadgeRow;
    private bool _refreshing = true, _closed, _busy;
    private Gesture? _gesture;
    private CoverLayout? _beforeSlider;
    private readonly CancellationTokenSource _lifetime = new();
    private CoverGuide[] _guides = [];
    private CoverLayout State => _history.Current;
    private bool Framing => FramingToggle.IsChecked == true;
    private bool IsWide => Modes.SelectedIndex == 1;
    private Size ImageSize => _image is not null ? CoverCanvas.ImagePixels(_image) : new Size(1, 1);
    private CoverBadge? Selected => State.Badges.FirstOrDefault(b => b.Id == _selected);

    internal CoverStudioWindow(CoverLayout initial, string? packageDirectory)
    {
        _history = new(initial);
        _packageDirectory = packageDirectory;
        InitializeComponent();
        Title = _text["Cover.Title"];
        ProjectTitle.Text = initial.Title;
        CropHost.Child = _crop;
        CropPreviewHost.Child = _thumb;
        WideHost.Child = _wide;
        BadgeEditorParking.Child = null;
        BuildBadgeRows();
        foreach (var canvas in new[] { _crop, _wide })
        {
            canvas.PointerPressed += BeginDrag;
            canvas.PointerMoved += MoveDrag;
            canvas.PointerReleased += EndDrag;
            canvas.PointerCaptureLost += (_, _) => StopDrag(commit: false);
            canvas.KeyDown += CanvasKeyDown;
        }
        ZoomSlider.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!_refreshing && !_busy && _image is not null && e.GetCurrentPoint(ZoomSlider).Properties.IsLeftButtonPressed)
            {
                StopDrag(commit: true);
                CommitZoomSlider();
                _beforeSlider = State;
            }
        }, RoutingStrategies.Tunnel);
        ZoomSlider.AddHandler(PointerReleasedEvent, (_, _) => CommitZoomSlider(), RoutingStrategies.Tunnel, handledEventsToo: true);
        ZoomSlider.AddHandler(PointerCaptureLostEvent, (_, _) => CommitZoomSlider(), handledEventsToo: true);
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _image?.Dispose();
            _image = null;
        };
        ResetEditorFields();
        SetStatus("Cover.PickHint");
    }

    internal async Task LoadSourceAsync(string path)
    {
        if (_busy || _closed) return;
        FinishPreviewEdits();
        _busy = true; Refresh();
        try
        {
            var bitmap = await Task.Run(() => CoverFiles.Load(path));
            if (_closed) { bitmap.Dispose(); return; }
            var previous = _image;
            _image = bitmap;
            _source = path;
            // Each source has its own pixel coordinates. Do not undo into another file's crop.
            var next = State with { Crop = CoverLayout.DefaultCrop(ImageSize), Framing = default, Zoom = 1 };
            _history.Reset(next);
            ResetEditorFields();
            previous?.Dispose();
            CoverStatus.Text = null;
        }
        catch (Exception ex) { if (!_closed) SetStatus("Cover.LoadFailed", ex.Message); }
        finally { _busy = false; if (!_closed) Refresh(); }
    }

    internal void ReportSourceError(Exception ex) => SetStatus("Cover.LoadFailed", ex.Message);
    private void SetStatus(string key, params object?[] args) => CoverStatus.Text = _text.Format(key, args);

    private void ResetEditorFields()
    {
        _refreshing = true;
        TrackText.Text = State.Title;
        ArtistText.Text = State.Artist;
        NoterText.Text = State.Noter;
        _refreshing = false;
        Refresh(list: true);
    }

    private void BuildBadgeRows()
    {
        foreach (var badge in State.Badges)
        {
            var marker = new TextBlock { Width = 15, FontSize = 10, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            var header = new Grid { ColumnDefinitions = new("Auto,*"), Children = { marker, label } };
            Grid.SetColumn(label, 1);
            var section = new Expander { Header = header, Classes = { "cover-section" } };
            section.PropertyChanged += (_, change) =>
            {
                if (_refreshing || change.Property != Expander.IsExpandedProperty) return;
                if (section.IsExpanded) _selected = badge.Id;
                else if (_selected == badge.Id) _selected = null;
                Refresh();
            };
            _badgeRows.Add(badge.Id, new(section, marker, label));
            BadgeRows.Children.Add(section);
        }
    }

    private void Refresh(bool list = false)
    {
        _refreshing = true;
        var selected = Selected;
        if (list)
            foreach (var badge in State.Badges)
            {
                var row = _badgeRows[badge.Id];
                row.Marker.Text = badge.Visible ? "◆" : "◇";
                row.Marker.Foreground = new SolidColorBrush(badge.Color, badge.Visible ? 1 : .55);
                row.Label.Text = badge.Icon ? badge.Label : $"{badge.Label} {badge.Level} · {badge.Id}";
                ToolTip.SetTip(row.Section, row.Label.Text);
            }
        var active = selected is null ? null : _badgeRows[selected.Id].Section;
        if (_activeBadgeRow != active)
        {
            if (_activeBadgeRow is { } previous)
            {
                previous.IsExpanded = false;
                previous.Content = null;
            }
            _activeBadgeRow = active;
            if (active is not null)
            {
                active.Content = BadgeProperties;
                active.IsExpanded = true;
            }
        }
        BadgeVisible.IsChecked = selected?.Visible;
        BadgeX.Value = selected is null ? null : (decimal)selected.Center.X;
        BadgeY.Value = selected is null ? null : (decimal)selected.Center.Y;
        CustomProperties.IsVisible = selected?.Custom == true;
        if (list || !CustomHex.IsFocused)
            CustomHex.Text = selected is null ? "" : $"#{selected.Color.R:X2}{selected.Color.G:X2}{selected.Color.B:X2}";
        TopToggle.IsChecked = State.TopBar;
        ZoomSlider.Value = State.Zoom * 100;
        ZoomValue.Value = (decimal)(State.Zoom * 100);
        FramingOptions.IsVisible = Framing;
        BadgeOptions.IsVisible = !Framing;
        FramingToggle.IsEnabled = _image is not null;
        SnapToggle.IsEnabled = !Framing;
        Recenter.IsEnabled = !Framing && State.Badges.Count(b => !b.Icon && b.Visible) <= 6;
        WideHelp.Text = _text[Framing ? "Cover.FrameHelp" : "Cover.DragHelp"];
        var crop = State.Crop;
        SourceName.Text = _image is null ? _text["Cover.NoSource"] : $"{Path.GetFileName(_source)} · {ImageSize.Width} × {ImageSize.Height}";
        CropReadout.Text = _image is null ? "—" : $"X {crop.X:0}   Y {crop.Y:0}   {crop.Width:0} × {crop.Height:0} px";
        ResetCrop.IsEnabled = ExportSquare.IsEnabled = ExportWide.IsEnabled = !_busy && _image is not null;
        ExportOverlay.IsEnabled = !_busy;
        ExportSquare.IsVisible = !IsWide;
        ExportWide.IsVisible = ExportOverlay.IsVisible = IsWide;
        UndoCover.IsEnabled = _history.CanUndo && _gesture is null;
        RedoCover.IsEnabled = _history.CanRedo && _gesture is null;
        _crop.Update(State, _image, CoverCanvasMode.Crop);
        _thumb.Update(State, _image, CoverCanvasMode.CropPreview);
        _wide.Update(State, _image, Framing ? CoverCanvasMode.Framing : CoverCanvasMode.Layout, _selected, _guides);
        _refreshing = false;
    }

    private void FinishPreviewEdits()
    {
        StopDrag(commit: true);
        CommitZoomSlider();
    }

    private void Change(CoverLayout next, bool list = false)
    {
        // The command was calculated from the current preview; commit that preview before the command.
        FinishPreviewEdits();
        _history.Change(next);
        Refresh(list);
    }
    private void UndoClicked(object? sender, RoutedEventArgs e) { FinishPreviewEdits(); _history.Undo(); ResetEditorFields(); }
    private void RedoClicked(object? sender, RoutedEventArgs e) { FinishPreviewEdits(); _history.Redo(); ResetEditorFields(); }
    private void ModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || e.Source != Modes) return;
        StopDrag(commit: false);
        Refresh();
    }
    private void PreviewSettingsChanged(object? sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        StopDrag(commit: false);
        Refresh();
    }
    private void ResetCropClicked(object? sender, RoutedEventArgs e) => Change(State with { Crop = CoverLayout.DefaultCrop(ImageSize) });
    private void ResetFrameClicked(object? sender, RoutedEventArgs e) => Change(State with { Zoom = 1, Framing = default });
    private void RecenterClicked(object? sender, RoutedEventArgs e) => Change(State.Recenter());
    private void TopChanged(object? sender, RoutedEventArgs e)
    { if (!_refreshing) Change(State with { TopBar = TopToggle.IsChecked == true }); }
    private void ZoomChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (ZoomValue?.Value is { } value) UpdateZoom((double)value);
    }
    private void ZoomSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) => UpdateZoom(e.NewValue);
    private void UpdateZoom(double percent)
    {
        if (_refreshing || _image is null) return;
        var next = State with { Zoom = Math.Clamp(percent / 100, 1, 4) };
        next = next with { Framing = ClampFraming(next.Zoom, next.Framing) };
        if (_beforeSlider is null) Change(next);
        else { _history.Preview(next); Refresh(); }
    }
    private void CommitZoomSlider()
    {
        if (_beforeSlider is not { } before) return;
        _beforeSlider = null;
        _history.Commit(before);
        if (!_closed) Refresh();
    }
    private void VisibilityChanged(object? sender, RoutedEventArgs e)
    {
        if (_refreshing || Selected is not { } badge) return;
        var visible = BadgeVisible.IsChecked == true;
        Change(State.ChangeBadge(badge.Id, b => b with { Visible = visible }), list: true);
    }
    private void BadgePositionChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_refreshing || Selected is not { } b || BadgeX.Value is not { } x || BadgeY.Value is not { } y) return;
        Change(State.ChangeBadge(b.Id, badge => badge with { Center = CoverLayout.ClampBadge(new((double)x, (double)y)) }));
    }
    private void ApplyHexClicked(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { Custom: true } b) return;
        if (CustomHex.Text is not { Length: 7 } hex || !hex.StartsWith('#') || !Color.TryParse(hex, out var color))
        { SetStatus("Cover.HexHint"); return; }
        Change(State.ChangeBadge(b.Id, badge => badge with { Color = color }), list: true);
        CoverStatus.Text = null;
    }
    private void CopyChanged(object? sender, RoutedEventArgs e) => CommitCopy();
    private void CommitCopy()
    {
        if (_refreshing || _closed) return;
        var next = State with { Title = TrackText.Text ?? "", Artist = ArtistText.Text ?? "", Noter = NoterText.Text ?? "" };
        if (next != State) Change(next);
    }

    private Point ClampFraming(double zoom, Point offset)
    {
        var filled = CoverLayout.WideImage(ImageSize, zoom, offset);
        return new(filled.X - (CoverLayout.Width - filled.Width) / 2, filled.Y - (CoverLayout.Height - filled.Height) / 2);
    }

    private void BeginDrag(object? sender, PointerPressedEventArgs e)
    {
        if (_busy || _gesture is not null || sender is not CoverCanvas canvas || canvas.Scale <= 0 ||
            !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed || !canvas.ContainsImagePoint(e.GetPosition(canvas))) return;
        CommitZoomSlider();
        CommitCopy();
        var p = canvas.ToLogical(e.GetPosition(canvas));
        if (canvas == _crop)
        {
            if (_image is null) return;
            var corner = new Point(State.Crop.Right, State.Crop.Bottom);
            var resize = new Vector(p.X - corner.X, p.Y - corner.Y).Length <= Math.Min(14 / canvas.Scale, State.Crop.Width * .45);
            if (!resize && !State.Crop.Contains(p)) return;
            _gesture = new(e.Pointer, canvas, resize ? GestureKind.CropSize : GestureKind.CropMove, p, State);
        }
        else if (Framing && _image is not null) _gesture = new(e.Pointer, canvas, GestureKind.Frame, p, State);
        else
        {
            var badge = State.Badges.LastOrDefault(b => b.Visible &&
                Math.Abs(b.Center.X - p.X) + Math.Abs(b.Center.Y - p.Y) <= CoverLayout.Radius);
            _selected = badge?.Id;
            if (badge is null) { Refresh(); return; }
            _gesture = new(e.Pointer, canvas, GestureKind.Badge, p, State, badge.Id);
        }
        canvas.Focus();
        e.Pointer.Capture(canvas);
        Refresh();
        e.Handled = true;
    }

    private void MoveDrag(object? sender, PointerEventArgs e)
    {
        if (_gesture is not { } g || g.Pointer != e.Pointer || g.Canvas.Scale <= 0) return;
        var delta = g.Canvas.ToLogical(e.GetPosition(g.Canvas)) - g.Origin;
        var before = g.Before;
        _guides = [];
        switch (g.Kind)
        {
            case GestureKind.CropMove:
                _history.Preview(before with { Crop = CoverLayout.ClampCrop(ImageSize, before.Crop.Position + delta, before.Crop.Width) });
                break;
            case GestureKind.CropSize:
                var extent = Math.Min(ImageSize.Width - before.Crop.X, ImageSize.Height - before.Crop.Y);
                var side = Math.Min(extent, before.Crop.Width + (delta.X + delta.Y) / 2);
                _history.Preview(before with { Crop = CoverLayout.ClampCrop(ImageSize, before.Crop.Position, side) });
                break;
            case GestureKind.Frame:
                var framing = ClampFraming(before.Zoom, before.Framing + delta);
                _history.Preview(before with { Framing = framing });
                break;
            case GestureKind.Badge:
                var raw = before.Badges.First(b => b.Id == g.BadgeId).Center + delta;
                var useSnap = SnapToggle.IsChecked == true && !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
                var result = useSnap ? CoverSnap.Align(raw, before.Badges.Where(b => b.Id != g.BadgeId),
                    Math.Min(24, 7 / g.Canvas.Scale)) : new CoverSnapResult(CoverLayout.ClampBadge(raw), []);
                _guides = result.Guides;
                _history.Preview(before.ChangeBadge(g.BadgeId!, b => b with { Center = result.Position }));
                break;
        }
        Refresh();
        e.Handled = true;
    }

    private void EndDrag(object? sender, PointerReleasedEventArgs e)
    {
        if (_gesture?.Pointer != e.Pointer) return;
        StopDrag(commit: true);
        e.Handled = true;
    }

    private void StopDrag(bool commit)
    {
        if (_gesture is not { } g) return;
        _gesture = null;
        _guides = [];
        if (commit) _history.Commit(g.Before);
        else _history.Preview(g.Before);
        g.Pointer.Capture(null);
        Refresh();
    }

    private void CanvasKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _gesture is not null) { StopDrag(commit: false); e.Handled = true; return; }
        if (_gesture is not null || _busy) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && (e.Key is Key.Z or Key.Y))
        {
            if (e.Key == Key.Y || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) RedoClicked(null, e);
            else UndoClicked(null, e);
            e.Handled = true; return;
        }
        var delta = e.Key switch { Key.Left => new Vector(-1, 0), Key.Right => new Vector(1, 0),
            Key.Up => new Vector(0, -1), Key.Down => new Vector(0, 1), _ => default };
        if (delta == default) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) delta *= 10;
        if (sender == _crop && _image is not null)
            Change(State with { Crop = CoverLayout.ClampCrop(ImageSize, State.Crop.Position + delta, State.Crop.Width) });
        else if (sender == _wide && Framing && _image is not null)
            Change(State with { Framing = ClampFraming(State.Zoom, State.Framing + delta) });
        else if (sender == _wide && Selected is { Visible: true } badge)
            Change(State.ChangeBadge(badge.Id, b => b with { Center = CoverLayout.ClampBadge(b.Center + delta) }));
        else return;
        e.Handled = true;
    }

    private async void BrowseClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _text["Cover.Open"], AllowMultiple = false,
                FileTypeFilter = [new(_text["Cover.Images"]) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }],
            });
            using var selected = files.FirstOrDefault();
            if (selected?.TryGetLocalPath() is { } path) await LoadSourceAsync(path);
        }
        catch (Exception ex) { if (!_closed) ReportSourceError(ex); }
    }

    private async void ExportSquareClicked(object? sender, RoutedEventArgs e) => await Export(CoverCanvasMode.CropPreview, "cover-square.png");
    private async void ExportOverlayClicked(object? sender, RoutedEventArgs e) => await Export(CoverCanvasMode.Overlay, "cover-overlay.png");
    private async void ExportWideClicked(object? sender, RoutedEventArgs e) => await Export(CoverCanvasMode.WideImage, "cover-video.png");
    private async Task Export(CoverCanvasMode mode, string name)
    {
        if (_closed || _busy || (mode != CoverCanvasMode.Overlay && _image is null)) return;
        FinishPreviewEdits();
        CommitCopy();
        _busy = true; Refresh();
        try
        {
            using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = _text["Cover.ExportTitle"], SuggestedFileName = name, DefaultExtension = "png",
                FileTypeChoices = [new("PNG") { Patterns = ["*.png"] }], ShowOverwritePrompt = false,
            });
            if (_closed || file?.TryGetLocalPath() is not { } path) return;
            await WriteCoverAsync(path, mode);
            if (!_closed) SetStatus("Cover.Exported", Path.GetFileName(path));
        }
        catch (CoverDestinationException ex) { if (!_closed) SetStatus(ex.Message); }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex) { if (!_closed) SetStatus("Cover.ExportFailed", ex.Message); }
        finally { _busy = false; if (!_closed) Refresh(); }
    }

    internal async Task WriteCoverAsync(string path, CoverCanvasMode mode)
    {
        var token = _lifetime.Token;
        // Avoid allocating a large image if the destination is already invalid. Recheck before publication.
        path = await Task.Run(() => CoverFiles.ValidateDestination(path, _packageDirectory), token);
        token.ThrowIfCancellationRequested();
        using var pixels = CoverFiles.Capture(State, _image, mode);
        await CoverFiles.ExportAsync(path, _packageDirectory, pixels, token);
    }
}
