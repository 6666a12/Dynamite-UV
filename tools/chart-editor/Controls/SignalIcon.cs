using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DynamiteUniverse.ChartEditor.Controls;

public enum SignalIconKind
{
    Select,
    Tap,
    Drag,
    Hold,
    ExTap,
    Mixer,
    Mine,
    BarLine,
    Scroll,
    Bpm,
    TrackLeft,
    TrackCenter,
    TrackRight,
    NavigateBack,
    NavigateForward,
    NavigateHome,
    NavigateMain,
    NavigateProject,
    NavigateEvents,
    NavigateValidation,
    NavigatePublish,
    Undo,
    Redo,
    Open,
    Save,
    Language,
    Info,
    Delete,
    Apply,
    Play,
    Speed,
    Offset,
    Grid,
    Snap,
}

/// <summary>Clean-room vector icon set rendered from geometry and the current foreground.</summary>
public sealed class SignalIcon : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<SignalIcon, IBrush?>(nameof(Foreground), inherits: true);
    public static readonly StyledProperty<SignalIconKind> KindProperty =
        AvaloniaProperty.Register<SignalIcon, SignalIconKind>(nameof(Kind));
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<SignalIcon, double>(nameof(StrokeThickness), 1.8);

    static SignalIcon()
    {
        AffectsRender<SignalIcon>(KindProperty, ForegroundProperty, StrokeThicknessProperty);
    }

    public SignalIcon()
    {
        Width = 20;
        Height = 20;
        MinWidth = 12;
        MinHeight = 12;
        IsHitTestVisible = false;
    }

    public SignalIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var size = Math.Min(Bounds.Width, Bounds.Height);
        var origin = new Point((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        var scale = size / 24.0;
        var brush = Foreground ?? Brushes.White;
        var pen = new Pen(brush, Math.Max(.5, StrokeThickness * scale), lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);
        Point P(double x, double y) => new(origin.X + x * scale, origin.Y + y * scale);
        Rect R(double x, double y, double width, double height) =>
            new(origin.X + x * scale, origin.Y + y * scale, width * scale, height * scale);
        void Line(double x1, double y1, double x2, double y2) => context.DrawLine(pen, P(x1, y1), P(x2, y2));
        void Poly(bool closed, params (double X, double Y)[] points)
        {
            var geometry = new StreamGeometry();
            using (var target = geometry.Open())
            {
                target.BeginFigure(P(points[0].X, points[0].Y), false);
                for (var index = 1; index < points.Length; index++)
                    target.LineTo(P(points[index].X, points[index].Y));
                target.EndFigure(closed);
            }
            context.DrawGeometry(null, pen, geometry);
        }
        void FillPoly(params (double X, double Y)[] points)
        {
            var geometry = new StreamGeometry();
            using (var target = geometry.Open())
            {
                target.BeginFigure(P(points[0].X, points[0].Y), true);
                for (var index = 1; index < points.Length; index++)
                    target.LineTo(P(points[index].X, points[index].Y));
                target.EndFigure(true);
            }
            context.DrawGeometry(brush, null, geometry);
        }
        void Box(double x, double y, double width, double height, double radius = 0) =>
            context.DrawRectangle(null, pen, R(x, y, width, height), radius * scale, radius * scale);
        void FillBox(double x, double y, double width, double height, double radius = 0) =>
            context.DrawRectangle(brush, null, R(x, y, width, height), radius * scale, radius * scale);
        void Circle(double x, double y, double radius, bool fill = false) =>
            context.DrawEllipse(fill ? brush : null, fill ? null : pen, P(x, y), radius * scale, radius * scale);

        switch (Kind)
        {
            case SignalIconKind.Select:
                FillPoly((5, 3), (18.5, 12), (12.2, 13.2), (9.5, 20.5));
                break;
            case SignalIconKind.Tap:
                FillBox(4, 9, 16, 6, 1.5);
                Line(7, 6, 17, 6);
                break;
            case SignalIconKind.Drag:
                FillBox(4, 9, 16, 6, 1.5);
                Poly(false, (7, 6), (4.5, 3.5), (2, 6));
                Poly(false, (17, 18), (19.5, 20.5), (22, 18));
                break;
            case SignalIconKind.ExTap:
                FillBox(4, 9, 16, 6, 1.5);
                Line(8, 4, 16, 20);
                Line(16, 4, 8, 20);
                break;
            case SignalIconKind.Hold:
                FillBox(4, 3, 16, 5, 1.5);
                FillBox(4, 16, 16, 5, 1.5);
                Line(7, 8, 7, 16);
                Line(17, 8, 17, 16);
                break;
            case SignalIconKind.Mixer:
                Line(5, 4, 5, 20);
                Line(12, 4, 12, 20);
                Line(19, 4, 19, 20);
                FillBox(3, 7, 4, 5, 1);
                FillBox(10, 13, 4, 5, 1);
                FillBox(17, 5, 4, 5, 1);
                break;
            case SignalIconKind.Mine:
                Circle(12, 12, 5.5);
                for (var index = 0; index < 8; index++)
                {
                    var angle = Math.PI * index / 4;
                    Line(12 + Math.Cos(angle) * 7.5, 12 + Math.Sin(angle) * 7.5,
                        12 + Math.Cos(angle) * 10, 12 + Math.Sin(angle) * 10);
                }
                Line(10, 10, 14, 14);
                Line(14, 10, 10, 14);
                break;
            case SignalIconKind.BarLine:
                Line(3, 12, 21, 12);
                Line(5, 8, 5, 16);
                Line(19, 8, 19, 16);
                break;
            case SignalIconKind.Scroll:
                Poly(false, (4, 15), (8, 10), (12, 14), (17, 7));
                Poly(false, (14, 7), (17, 7), (17, 10));
                break;
            case SignalIconKind.Bpm:
                Circle(12, 13, 8);
                Line(12, 13, 17, 9);
                Line(12, 3, 12, 5);
                Line(8.5, 3, 15.5, 3);
                break;
            case SignalIconKind.TrackLeft:
                Poly(false, (18, 3), (8, 12), (18, 21));
                Line(5, 4, 5, 20);
                break;
            case SignalIconKind.TrackCenter:
                Line(8, 3, 8, 21);
                Line(16, 3, 16, 21);
                Line(8, 12, 16, 12);
                break;
            case SignalIconKind.TrackRight:
                Poly(false, (6, 3), (16, 12), (6, 21));
                Line(19, 4, 19, 20);
                break;
            case SignalIconKind.NavigateBack:
                Poly(false, (14.5, 4.5), (7, 12), (14.5, 19.5));
                break;
            case SignalIconKind.NavigateForward:
                Poly(false, (9.5, 4.5), (17, 12), (9.5, 19.5));
                break;
            case SignalIconKind.NavigateHome:
                Poly(false, (3.5, 11), (12, 4), (20.5, 11));
                Box(6, 10, 12, 10, 1);
                Line(10, 20, 10, 14);
                Line(14, 14, 14, 20);
                break;
            case SignalIconKind.NavigateMain:
                Circle(12, 12, 2, true);
                Line(12, 3, 12, 7);
                Line(12, 17, 12, 21);
                Line(3, 12, 7, 12);
                Line(17, 12, 21, 12);
                break;
            case SignalIconKind.NavigateProject:
                Poly(true, (4, 7), (10, 7), (12, 9), (20, 9), (20, 19), (4, 19));
                Line(4, 7, 4, 5);
                Line(4, 5, 10, 5);
                break;
            case SignalIconKind.NavigateEvents:
                Line(5, 6, 19, 6);
                Line(5, 12, 19, 12);
                Line(5, 18, 19, 18);
                Circle(9, 6, 1.5, true);
                Circle(15, 12, 1.5, true);
                Circle(11, 18, 1.5, true);
                break;
            case SignalIconKind.NavigateValidation:
                Circle(12, 12, 9);
                Poly(false, (7, 12), (10.5, 15.5), (17.5, 8));
                break;
            case SignalIconKind.NavigatePublish:
                Box(4, 8, 12, 12, 1);
                Line(10, 14, 20, 4);
                Poly(false, (14, 4), (20, 4), (20, 10));
                break;
            case SignalIconKind.Undo:
                Poly(false, (9, 5), (4, 10), (9, 15));
                Poly(false, (4, 10), (7, 8.5), (11, 8), (15, 9), (18, 12), (19, 16), (18, 19));
                break;
            case SignalIconKind.Redo:
                Poly(false, (15, 5), (20, 10), (15, 15));
                Poly(false, (20, 10), (17, 8.5), (13, 8), (9, 9), (6, 12), (5, 16), (6, 19));
                break;
            case SignalIconKind.Open:
                Poly(true, (3, 8), (10, 8), (12, 10), (21, 10), (18, 19), (3, 19));
                Line(3, 8, 3, 5);
                Line(3, 5, 10, 5);
                break;
            case SignalIconKind.Save:
                Box(4, 3, 16, 18, 1.5);
                Box(7, 3, 9, 6, .5);
                Box(7, 14, 10, 7, .5);
                break;
            case SignalIconKind.Language:
                Circle(12, 12, 9);
                context.DrawEllipse(null, pen, P(12, 12), 4.5 * scale, 9 * scale);
                Line(3, 12, 21, 12);
                Poly(false, (5, 8), (8.5, 6.5), (12, 6), (15.5, 6.5), (19, 8));
                Poly(false, (5, 16), (8.5, 17.5), (12, 18), (15.5, 17.5), (19, 16));
                break;
            case SignalIconKind.Info:
                Circle(12, 12, 9);
                Circle(12, 7, 1, true);
                Line(12, 11, 12, 17);
                break;
            case SignalIconKind.Delete:
                Box(6, 7, 12, 14, 1);
                Line(4, 7, 20, 7);
                Line(9, 4, 15, 4);
                Line(10, 10, 10, 18);
                Line(14, 10, 14, 18);
                break;
            case SignalIconKind.Apply:
                Circle(12, 12, 9);
                Poly(false, (7, 12), (10.5, 15.5), (17.5, 8));
                break;
            case SignalIconKind.Play:
                FillPoly((7, 4), (20, 12), (7, 20));
                break;
            case SignalIconKind.Speed:
                Poly(false, (4, 16), (5, 11), (8, 7), (12, 5), (16, 7), (19, 11), (20, 16));
                Line(12, 14, 17, 9);
                Circle(12, 14, 1.5, true);
                break;
            case SignalIconKind.Offset:
                Line(4, 7, 20, 7);
                Line(4, 17, 20, 17);
                Line(9, 4, 9, 10);
                Line(15, 14, 15, 20);
                break;
            case SignalIconKind.Grid:
                Box(4, 4, 16, 16, 1);
                Line(9.33, 4, 9.33, 20);
                Line(14.67, 4, 14.67, 20);
                Line(4, 9.33, 20, 9.33);
                Line(4, 14.67, 20, 14.67);
                break;
            case SignalIconKind.Snap:
                Poly(false, (5, 4), (5, 14), (8, 19), (12, 20), (16, 19), (19, 14), (19, 4));
                Line(5, 8, 9, 8);
                Line(15, 8, 19, 8);
                break;
        }
    }
}
