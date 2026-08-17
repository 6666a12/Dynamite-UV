using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DuxCommunity.ChartEditor.Controls;

/// <summary>Quiet procedural signal field used by the editor's front-door pages.</summary>
public sealed class SignalBackdrop : Control
{
    private static readonly SolidColorBrush Background = new(Color.Parse("#060A13"));
    private static readonly SolidColorBrush CyanWash = new(Color.Parse("#0B2230"), 0.42);
    private static readonly SolidColorBrush PinkWash = new(Color.Parse("#28111F"), 0.28);
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.Parse("#5A6C8F"), 0.10), 1);
    private static readonly Pen CyanPen = new(new SolidColorBrush(Color.Parse("#35E0FF"), 0.18), 1.5);
    private static readonly Pen PinkPen = new(new SolidColorBrush(Color.Parse("#FF4D91"), 0.16), 1.5);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        context.FillRectangle(Background, Bounds);

        context.FillRectangle(CyanWash, new Rect(width * 0.54, 0, width * 0.46, height));
        context.FillRectangle(PinkWash, new Rect(0, height * 0.72, width * 0.44, height * 0.28));

        var horizon = height * 0.34;
        for (var index = -8; index <= 8; index++)
        {
            var target = width * 0.5 + index * width * 0.12;
            context.DrawLine(GridPen, new Point(width * 0.57, horizon), new Point(target, height));
        }
        for (var row = 0; row < 8; row++)
        {
            var progress = row / 7.0;
            var y = horizon + (height - horizon) * progress * progress;
            context.DrawLine(GridPen, new Point(0, y), new Point(width, y));
        }

        context.DrawLine(CyanPen, new Point(width * 0.57, 54), new Point(width * 0.57, height - 54));
        context.DrawLine(PinkPen, new Point(42, height * 0.78), new Point(width * 0.42, height * 0.78));
        context.FillRectangle(new SolidColorBrush(Color.Parse("#35E0FF"), 0.58),
            new Rect(width * 0.57 - 2, horizon - 18, 4, 36));
    }
}
