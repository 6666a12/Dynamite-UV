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
    public static async Task Run(MainWindow owner, EditorDocument document, string root, Action<string, bool> check)
    {
        Directory.CreateDirectory(root);
        var before = document.ChangeVersion;
        var layout = CoverLayout.FromDocument(document);
        check("Cover imports current chart metadata and one badge per difficulty",
            layout.Title == document.Title && layout.Artist == document.Artist && layout.Badges.Length == document.Charts.Count + 1 &&
            layout.Badges.Any(b => b.Id == document.SelectedChartId));
        CheckGeometry(check);
        var source = Path.Combine(root, "source.png");
        SKColor[] colors = [new(49, 72, 87), new(59, 83, 98), new(36, 52, 66), new(41, 62, 80)];
        using (var sourceImage = new SKBitmap(320, 240))
        using (var context = new SKCanvas(sourceImage))
        using (var paint = new SKPaint())
        {
            for (var i = 0; i < 4; i++)
            {
                paint.Color = colors[i];
                context.DrawRect(i % 2 * 160, i / 2 * 120, 160, 120, paint);
            }
            using var output = File.Create(source);
            sourceImage.Encode(output, SKEncodedImageFormat.Png, 100);
        }
        using (var bitmap = CoverFiles.Load(source))
        {
            var state = layout with { Crop = new Rect(4, 4, 120, 120) };
            using var square = SKBitmap.Decode(CoverFiles.Encode(state, bitmap, CoverCanvasMode.CropPreview));
            using var wide = SKBitmap.Decode(CoverFiles.Encode(state, bitmap, CoverCanvasMode.WideImage));
            var overlayBytes = CoverFiles.Encode(state, null, CoverCanvasMode.Overlay);
            using var overlay = SKBitmap.Decode(overlayBytes);
            check("Square and wide PNG use independent source framing and expected dimensions",
                square.Width == 120 && square.Height == 120 && square.GetPixel(40, 40) == colors[0] &&
                square.GetPixel(115, 118) == colors[2] && wide.Width == 1920 && wide.Height == 1080 &&
                wide.GetPixel(50, 200) == colors[0] && wide.GetPixel(50, 700) == colors[2]);
            check("Transparent PNG keeps center background clear and exports both edge bars",
                overlay.Width == 1920 && overlay.Height == 1080 && overlay.GetPixel(60, 500).Alpha == 0 &&
                overlay.GetPixel(960, 0).Alpha > 200 && overlay.GetPixel(960, 1079).Alpha > 240);
            var output = Path.Combine(root, "overlay.png");
            await CoverFiles.SaveNewAsync(output, overlayBytes);
            var refused = false;
            try { await CoverFiles.SaveNewAsync(output, [0]); }
            catch (IOException) { refused = true; }
            check("Cover export never replaces an existing file", refused && File.ReadAllBytes(output).SequenceEqual(overlayBytes));
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
            await window.LoadSourceAsync(source);
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
                frame?.Save(previewPath, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                snap.IsChecked = false;
            }
            window.FindControl<ToggleButton>("FramingToggle")!.IsChecked = true;
            window.FindControl<Slider>("ZoomSlider")!.Value = 130;
            Drag(window, wide, CoverLayout.Pivot, CoverLayout.Pivot + new Vector(115, -85));
            check("Background mode leaves badge and square crop unchanged", x.Value == (decimal)initial.X && first.Text == beforeWide &&
                window.FindControl<NumericUpDown>("ZoomValue")!.Value == 130);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        check("Cover operations leave the open document unchanged", document.ChangeVersion == before);
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

    private static void Drag(Window window, CoverCanvas canvas, Point from, Point to)
    {
        window.UpdateLayout();
        var center = new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
        var around = canvas.ToLogical(center);
        Point InWindow(Point p) => canvas.TranslatePoint(center + new Vector(p.X - around.X, p.Y - around.Y) * canvas.Scale, window)!.Value;
        var start = InWindow(from); var end = InWindow(to);
        window.MouseMove(start); window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end); window.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Click(Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, button));
        Dispatcher.UIThread.RunJobs();
    }
}
