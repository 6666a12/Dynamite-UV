using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace DynamiteUniverse.Editor.Shell;

public sealed class TimelineView : Control
{
    private double _bar;
    private double _maximumBar = 32;
    private IReadOnlyList<double> _noteBars = Array.Empty<double>();
    private bool _scrubbing;

    public event EventHandler<double>? SeekRequested;

    public double CurrentBar
    {
        get => _bar;
        set { _bar = value; InvalidateVisual(); }
    }

    public double MaximumBar
    {
        get => _maximumBar;
        set { _maximumBar = Math.Max(1, value); InvalidateVisual(); }
    }

    public IReadOnlyList<double> NoteBars
    {
        get => _noteBars;
        set { _noteBars = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#090F1E")), rect);
        var line = new SolidColorBrush(Color.Parse("#2A3560"));
        var dim = new SolidColorBrush(Color.Parse("#7C88B0"));
        var cyan = new SolidColorBrush(Color.Parse("#35E0FF"));
        var pink = new SolidColorBrush(Color.Parse("#FF4D8F"));
        var left = 8d;
        var width = Math.Max(1, Bounds.Width - 16);
        for (var tick = 0; tick <= 32; tick++)
        {
            var x = left + width * tick / 32;
            context.DrawLine(new Pen(line, tick % 4 == 0 ? 1 : .5), new Point(x, 12), new Point(x, Bounds.Height - 7));
            if (tick % 4 == 0)
                context.DrawText(Label($"{_maximumBar * tick / 32:00}", dim, 10), new Point(x + 3, 2));
        }
        foreach (var noteBar in _noteBars)
        {
            var x = left + width * Math.Clamp(noteBar / _maximumBar, 0, 1);
            context.DrawLine(new Pen(cyan, 2), new Point(x, Bounds.Height - 22), new Point(x, Bounds.Height - 8));
        }
        var playhead = left + width * Math.Clamp(_bar / _maximumBar, 0, 1);
        context.DrawLine(new Pen(cyan, 1.5), new Point(playhead, 4), new Point(playhead, Bounds.Height - 4));
        var triangle = new StreamGeometry();
        using (var path = triangle.Open())
        {
            path.BeginFigure(new Point(playhead - 5, 3), true);
            path.LineTo(new Point(playhead + 5, 3));
            path.LineTo(new Point(playhead, 10));
            path.EndFigure(true);
        }
        context.DrawGeometry(cyan, null, triangle);
        context.DrawLine(new Pen(pink, 1), new Point(left, Bounds.Height - 2), new Point(left + width, Bounds.Height - 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _scrubbing = true;
        e.Pointer.Capture(this);
        Seek(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_scrubbing) Seek(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _scrubbing = false;
        e.Pointer.Capture(null);
    }

    private void Seek(double x) => SeekRequested?.Invoke(this,
        Math.Clamp((x - 8) / Math.Max(1, Bounds.Width - 16), 0, 1) * _maximumBar);

    private static FormattedText Label(string text, IBrush brush, double size) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
}
