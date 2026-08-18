using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using DynamiteUniverse.ChartEditor.Settings;

namespace DynamiteUniverse.ChartEditor.Controls;

/// <summary>Quiet procedural signal field used by the editor's front-door pages.</summary>
public sealed class SignalBackdrop : Control
{
    private static readonly SolidColorBrush Background = new(Color.Parse("#060A13"));
    private static readonly SolidColorBrush CyanWash = new(Color.Parse("#0B2230"), 0.42);
    private static readonly SolidColorBrush PinkWash = new(Color.Parse("#28111F"), 0.28);
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.Parse("#5A6C8F"), 0.10), 1);
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public SignalBackdrop()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background,
            (_, _) =>
            {
                if (EditorPreferences.Current.Motion == EditorMotionMode.Full)
                    InvalidateVisual();
            });
        _timer.Start();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        context.FillRectangle(Background, Bounds);

        var fullMotion = EditorPreferences.Current.Motion == EditorMotionMode.Full;
        var phase = fullMotion ? _clock.Elapsed.TotalSeconds : 0;
        var breathe = fullMotion ? 0.78 + 0.22 * Math.Sin(phase * 0.72) : 0.9;
        context.FillRectangle(new SolidColorBrush(CyanWash.Color, CyanWash.Opacity * breathe),
            new Rect(width * 0.54, 0, width * 0.46, height));
        context.FillRectangle(PinkWash, new Rect(0, height * 0.72, width * 0.44, height * 0.28));

        var horizon = height * 0.34;
        var gridShift = fullMotion ? (phase * 0.018) % 1 : 0;
        for (var index = -8; index <= 8; index++)
        {
            var target = width * 0.5 + (index + gridShift) * width * 0.12;
            context.DrawLine(GridPen, new Point(width * 0.57, horizon), new Point(target, height));
        }
        for (var row = 0; row < 8; row++)
        {
            var progress = ((row / 7.0) + gridShift * 0.15) % 1;
            var y = horizon + (height - horizon) * progress * progress;
            context.DrawLine(GridPen, new Point(0, y), new Point(width, y));
        }

        var scanX = fullMotion ? width * (0.18 + 0.68 * ((phase * 0.055) % 1)) : width * 0.57;
        var cyanPen = new Pen(new SolidColorBrush(Color.Parse("#35E0FF"), 0.12 + breathe * 0.10), 1.5);
        var pinkPen = new Pen(new SolidColorBrush(Color.Parse("#FF4D91"), 0.16), 1.5);
        context.DrawLine(cyanPen, new Point(scanX, 54), new Point(scanX, height - 54));
        context.DrawLine(pinkPen, new Point(42, height * 0.78), new Point(width * 0.42, height * 0.78));
        context.FillRectangle(new SolidColorBrush(Color.Parse("#35E0FF"), 0.58),
            new Rect(scanX - 2, horizon - 18, 4, 36));
    }
}
