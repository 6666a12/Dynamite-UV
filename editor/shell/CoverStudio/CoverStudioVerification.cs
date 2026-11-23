using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamiteUniverse.ChartEditor.Core;
using SkiaSharp;

namespace DynamiteUniverse.Editor.Shell;

internal static class CoverStudioVerification
{
    private static readonly SKColor[] Palette = [new(49, 72, 87), new(59, 83, 98), new(36, 52, 66), new(41, 62, 80)];

    public static string CreateSource(string root)
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.png");
        using (var sourceImage = new SKBitmap(320, 240))
        using (var context = new SKCanvas(sourceImage))
        using (var paint = new SKPaint())
        {
            for (var i = 0; i < 4; i++)
            {
                paint.Color = Palette[i];
                context.DrawRect(i % 2 * 160, i / 2 * 120, 160, 120, paint);
            }
            using var output = File.Create(source);
            sourceImage.Encode(output, SKEncodedImageFormat.Png, 100);
        }
        return source;
    }

    public static async Task Run(MainWindow owner, EditorDocument document, string root, Action<string, bool> check)
    {
        var before = document.ChangeVersion;
        var layout = CoverLayout.FromDocument(document);
        check("Cover imports current chart metadata and one badge per difficulty",
            layout.Title == document.Title && layout.Artist == document.Artist && layout.Badges.Length == document.Charts.Count + 1 &&
            layout.Badges.Any(b => b.Id == document.SelectedChartId));
        CheckGeometry(check);
        using (var bitmap = CoverFiles.Load(Path.Combine(root, "source.png")))
        {
            var state = layout with { Crop = new Rect(4, 4, 120, 120) };
            using var square = CoverFiles.Capture(state, bitmap, CoverCanvasMode.CropPreview);
            using var wide = CoverFiles.Capture(state, bitmap, CoverCanvasMode.WideImage);
            using var overlay = CoverFiles.Capture(state, null, CoverCanvasMode.Overlay);
            check("Square and wide PNG use independent source framing and expected dimensions",
                square.Width == 120 && square.Height == 120 && square.GetPixel(40, 40) == Palette[0] &&
                square.GetPixel(115, 118) == Palette[2] && wide.Width == 1920 && wide.Height == 1080 &&
                wide.GetPixel(50, 200) == Palette[0] && wide.GetPixel(50, 700) == Palette[2]);
            check("Transparent PNG keeps center background clear and exports both edge bars",
                overlay.Width == 1920 && overlay.Height == 1080 && overlay.GetPixel(60, 500).Alpha == 0 &&
                overlay.GetPixel(960, 0).Alpha > 200 && overlay.GetPixel(960, 1079).Alpha > 240);
            var output = Path.Combine(root, "overlay.png");
            await CoverFiles.ExportAsync(output, null, overlay, CancellationToken.None);
            var overlayBytes = File.ReadAllBytes(output);
            var refused = false;
            try { CoverFiles.Publish(output, null, stream => stream.WriteByte(0)); }
            catch (CoverDestinationException) { refused = true; }
            using var encoded = SKBitmap.Decode(overlayBytes);
            check("Cover export never replaces an existing file and PNG retains alpha", refused &&
                File.ReadAllBytes(output).SequenceEqual(overlayBytes) && encoded.GetPixel(60, 500).Alpha == 0);
            CheckFailedPublish(root, check);
        }
        var tools = owner.GetVisualDescendants().OfType<ToolsView>().Single();
        var entry = tools.FindControl<Button>("CoverStudioButton")!;
        entry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, entry));
        Dispatcher.UIThread.RunJobs();
        var window = owner.OwnedWindows.OfType<CoverStudioWindow>().SingleOrDefault();
        check("Tools extension opens a separate owned cover window", window?.IsVisible == true);
        if (window is null) return;
        try
        {
            var squareExport = window.FindControl<Button>("ExportSquare")!;
            for (var attempt = 0; !squareExport.IsEnabled && attempt < 100; attempt++) await Task.Delay(10);
            check("Cover window automatically loads active package artwork", squareExport.IsEnabled &&
                window.FindControl<TextBlock>("SourceName")!.Text?.Contains("320 × 240", StringComparison.Ordinal) == true);
            if (!squareExport.IsEnabled) return;
            await CheckDestinations(window, document.PackageDirectory!, root, check);
            var first = window.FindControl<TextBlock>("CropReadout")!;
            var crop = (CoverCanvas)window.FindControl<Border>("CropHost")!.Child!;
            var initialCrop = first.Text;
            Drag(window, crop, new(160, 120), new(181, 131));
            check("Real pointer drag moves the source-pixel crop", first.Text != initialCrop);
            var beforeWide = first.Text;
            window.FindControl<TabControl>("Modes")!.SelectedIndex = 1;
            var wideHost = window.FindControl<Border>("WideHost")!;
            var wide = (CoverCanvas)wideHost.Child!;
            var rows = window.FindControl<StackPanel>("BadgeRows")!.Children.OfType<Expander>().ToArray();
            var footer = window.FindControl<Expander>("FooterSection")!;
            var editor = window.FindControl<StackPanel>("BadgeProperties")!;
            rows[0].IsExpanded = true;
            footer.IsExpanded = true;
            rows[^1].IsExpanded = true;
            check("Cover inspector opens one badge inline while the footer stays independent",
                rows.Count(row => row.IsExpanded) == 1 && ReferenceEquals(rows[^1].Content, editor) &&
                rows[0].Content is null && footer.IsExpanded);
            rows[^1].IsExpanded = false;
            check("Collapsing a badge detaches its properties", rows.All(row => !row.IsExpanded && row.Content is null));
            window.UpdateLayout();
            var toolbar = new[] { "FramingToggle", "SnapToggle", "TopToggle", "Recenter" }.Select(name =>
            {
                var control = window.FindControl<Control>(name)!;
                return control.TranslatePoint(new Point(0, control.Bounds.Height / 2), window)!.Value.Y;
            }).ToArray();
            check("Cover toolbar aligns its four controls on one vertical center", toolbar.Max() - toolbar.Min() < 1);
            check("Cover letterbox space uses the window background", wideHost.Background is null && wideHost.BorderThickness == default);
            var initial = layout.Badges.First(b => !b.Icon).Center;
            window.FindControl<CheckBox>("SnapToggle")!.IsChecked = false;
            Drag(window, wide, initial, initial + new Vector(68, -37));
            var x = window.FindControl<NumericUpDown>("BadgeX")!;
            check("Wide canvas drag selects and moves one badge", x.Value is { } v && Math.Abs((double)v - initial.X - 68) < 5);
            Click(window.FindControl<Button>("UndoCover")!);
            check("Undo restores the badge without undoing the square crop", x.Value == (decimal)initial.X && first.Text == beforeWide);
            if (Environment.GetEnvironmentVariable("DYNA_COVER_PREVIEW_PNG") is { Length: > 0 } previewPath)
            {
                var snap = window.FindControl<CheckBox>("SnapToggle")!;
                snap.IsChecked = true;
                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                if (frame is not null)
                {
                    using var preview = new FileStream(previewPath, FileMode.CreateNew);
                    frame.Save(preview, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                snap.IsChecked = false;
            }
            var top = window.FindControl<CheckBox>("TopToggle")!;
            Drag(window, wide, initial, initial + new Vector(55, -42), () => top.IsChecked = false);
            check("A second edit commits the current drag without losing the toggle or adding preview history",
                top.IsChecked == false && x.Value is { } moved && Math.Abs((double)moved - initial.X - 55) < 5);
            Click(window.FindControl<Button>("UndoCover")!);
            check("Undo toggle keeps the committed drag", top.IsChecked == true && x.Value is { } kept && Math.Abs((double)kept - initial.X - 55) < 5);
            Click(window.FindControl<Button>("UndoCover")!);
            check("Second undo restores the pre-drag position", x.Value == (decimal)initial.X);

            var frameToggle = window.FindControl<ToggleButton>("FramingToggle")!;
            frameToggle.IsChecked = true;
            window.FindControl<Slider>("ZoomSlider")!.Value = 130;
            var sample = new Point(1020, 300);
            var beforePan = PreviewPixel(window, wide, sample);
            Drag(window, wide, CoverLayout.Pivot, CoverLayout.Pivot + new Vector(115, -85));
            frameToggle.IsChecked = false;
            var afterPan = PreviewPixel(window, wide, sample);
            var widePath = Path.Combine(root, "panned.png");
            await window.WriteCoverAsync(widePath, CoverCanvasMode.WideImage);
            using var panned = SKBitmap.Decode(widePath);
            check("Framing drag shifts source pixels in both preview and exported PNG",
                beforePan == Palette[1] && afterPan == Palette[0] && panned.GetPixel(1020, 300) == afterPan);
            check("Background mode leaves badge and square crop unchanged", x.Value == (decimal)initial.X && first.Text == beforeWide &&
                window.FindControl<NumericUpDown>("ZoomValue")!.Value == 130);
            await CheckTransparentSource(window, wide, root, check);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        check("Cover operations leave the open document unchanged", document.ChangeVersion == before);
    }

    private static void CheckFailedPublish(string root, Action<string, bool> check)
    {
        var output = Path.Combine(root, "interrupted.png");
        var failed = false;
        try
        {
            CoverFiles.Publish(output, null, stream => { stream.WriteByte(1); throw new IOException("Injected write failure."); });
        }
        catch (IOException) { failed = true; }
        check("A failed cover export leaves no final or staged file", failed && !File.Exists(output) &&
            !Directory.EnumerateFiles(root, ".uv-cover-*.tmp").Any());
        using var cancel = new CancellationTokenSource();
        try { CoverFiles.Publish(output, null, stream => { stream.WriteByte(1); cancel.Cancel(); }, cancel.Token); }
        catch (OperationCanceledException) { }
        check("Cancelling before publication leaves no final or staged file", !File.Exists(output) &&
            !Directory.EnumerateFiles(root, ".uv-cover-*.tmp").Any());
        CoverFiles.Publish(output, null, stream => stream.WriteByte(2));
        check("Failed export can be retried with the same name", File.ReadAllBytes(output).SequenceEqual(new byte[] { 2 }));
        var race = Path.Combine(root, "existing-at-publish.png");
        try { CoverFiles.Publish(race, null, stream => { stream.WriteByte(1); File.WriteAllBytes(race, [2]); }); }
        catch (IOException) { }
        check("Cover export preserves a destination created during encoding", File.ReadAllBytes(race).SequenceEqual(new byte[] { 2 }) &&
            !Directory.EnumerateFiles(root, ".uv-cover-*.tmp").Any());
    }

    private static async Task CheckDestinations(CoverStudioWindow window, string package, string root, Action<string, bool> check)
    {
        var direct = Path.Combine(package, "no-export.png");
        async Task<bool> Reject(string path, string reason)
        {
            try { await window.WriteCoverAsync(path, CoverCanvasMode.Overlay); }
            catch (CoverDestinationException ex) { return ex.Message == reason; }
            return false;
        }
        check("Cover window rejects a direct destination inside the package", await Reject(direct, "Cover.OutsidePackage") && !File.Exists(direct));
        var alias = Path.Combine(root, "pack-alias");
        try { Directory.CreateSymbolicLink(alias, package); }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is IOException or UnauthorizedAccessException)
        {
            // Junctions don't require Windows Developer Mode or symlink privileges.
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{alias}\" \"{package}\"") { CreateNoWindow = true });
            process!.WaitForExit();
            if (process.ExitCode != 0) throw new IOException("Failed to create export verification junction.");
        }
        try
        {
            check("Cover window rejects a directory alias into the package",
                await Reject(Path.Combine(alias, "no-export.png"), "Cover.LinkedDirectory") && !File.Exists(direct));
        }
        finally { Directory.Delete(alias, recursive: false); }
    }

    private static async Task CheckTransparentSource(CoverStudioWindow window, CoverCanvas wide, string root, Action<string, bool> check)
    {
        var source = Path.Combine(root, "transparent-source.png");
        using (var art = new SKBitmap(8, 8))
        {
            art.Erase(SKColors.Transparent);
            using var output = File.Create(source);
            art.Encode(output, SKEncodedImageFormat.Png, 100);
        }
        await window.LoadSourceAsync(source);
        var path = Path.Combine(root, "transparent-composed.png");
        await window.WriteCoverAsync(path, CoverCanvasMode.WideImage);
        using var png = SKBitmap.Decode(path);
        check("Composed PNG uses the same backdrop as a transparent source preview",
            PreviewPixel(window, wide, new Point(250, 250)) == png.GetPixel(250, 250) && png.GetPixel(250, 250).Alpha == 255);
    }

    private static SKColor PreviewPixel(Window window, CoverCanvas canvas, Point logical)
    {
        window.UpdateLayout();
        var at = InWindow(window, canvas, logical);
        using var frame = window.CaptureRenderedFrame() ?? throw new IOException("Missing cover preview frame.");
        using var data = new MemoryStream();
        frame.Save(data, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        using var bitmap = SKBitmap.Decode(data.ToArray());
        return bitmap.GetPixel((int)at.X, (int)at.Y);
    }

    private static void CheckGeometry(Action<string, bool> check)
    {
        var position = CoverSnap.Align(new(966, 455), [], 11);
        check("Template axes snap independently with two guides", position.Position == CoverLayout.Pivot && position.Guides.Length == 2);
        var other = new CoverBadge("ref", "HARD", "12", Colors.Orange, false, false, true, new(400, 400));
        var edge = CoverSnap.Align(new(508, 505), [other], 12);
        check("Actual diagonal edges snap, not the diamond bounding box",
            edge.Guides.Any(g => g.Normal.X != 0 && g.Normal.Y != 0) && Math.Abs(edge.Position.X + edge.Position.Y - 1010) < 1e-9);
        var centers = CoverSnap.Align(new(407, 257), [other], 10);
        var hidden = CoverSnap.Align(new(407, 257), [other with { Visible = false }], 10);
        check("Neighbor centers snap and hidden badges do not attract", centers.Position.X == 400 && hidden.Guides.Length == 0);
        var p = CoverLayout.WideImage(new Size(800, 1200), 1.5, new(100000, -100000));
        check("Wide image is never stretched or dragged beyond its coverage", p.Right >= 1920 && p.Bottom == 1080 && p.Left == 0 &&
            Math.Abs(p.Width / p.Height - 2d / 3) < 1e-9);
    }

    private static Point InWindow(Window window, CoverCanvas canvas, Point p)
    {
        var center = new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
        var around = canvas.ToLogical(center);
        return canvas.TranslatePoint(center + new Vector(p.X - around.X, p.Y - around.Y) * canvas.Scale, window)!.Value;
    }

    private static void Drag(Window window, CoverCanvas canvas, Point from, Point to, Action? during = null)
    {
        window.UpdateLayout();
        var start = InWindow(window, canvas, from); var end = InWindow(window, canvas, to);
        window.MouseMove(start); window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end);
        during?.Invoke();
        if (during is not null) window.MouseMove(end + new Vector(80, 25));
        window.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Click(Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, button));
        Dispatcher.UIThread.RunJobs();
    }
}
