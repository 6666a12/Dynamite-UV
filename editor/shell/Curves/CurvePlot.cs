using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Draw the shared evaluator's actual polyline, including any predicted overshoot.</summary>
public sealed class CurvePlot : Control
{
    private static readonly Pen GridLine = new(Brush.Parse("#53626E"), 1);
    private static readonly Pen Normal = new(Brush.Parse("#6ADBD9"), 1.8);
    private static readonly Pen Outside = new(Brush.Parse("#F399A6"), 1.8);
    internal CustomCurve? Curve { get; private set; }
    internal void Show(CustomCurve? curve) { Curve = curve; InvalidateVisual(); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var w = Bounds.Width - 6; var h = Bounds.Height - 6;
        if (w <= 0 || h <= 0 || Curve is not { } curve) return;
        var low = Math.Min(0, curve.Minimum); var high = Math.Max(1, curve.Maximum);
        var scale = Math.Max(Math.Abs(low), high); // also works if high-low overflows
        double Y(double p) => 3 + (high / scale - p / scale) / (high / scale - low / scale) * h;
        Point At(double t, double p) => new(3 + t * w, Y(p));
        foreach (var y in new[] { 0d, 1d }) context.DrawLine(GridLine, At(0, y), At(1, y));
        var a = curve.Knots[0]; var pa = curve.Evaluate(a);
        for (var i = 1; i < curve.Knots.Count; i++)
        {
            var b = curve.Knots[i]; var pb = curve.Evaluate(b);
            var prev = At(a, pa); var prevValue = pa;
            void DrawTo(Point end, double value)
            {
                var outside = Math.Min(prevValue, value) < 0 || Math.Max(prevValue, value) > 1;
                context.DrawLine(outside ? Outside : Normal, prev, end); prev = end; prevValue = value;
            }
            void Cross(double threshold)
            {
                if (!(Math.Min(pa, pb) < threshold && Math.Max(pa, pb) > threshold)) return;
                var s = Math.Max(1, Math.Max(Math.Abs(pa), Math.Abs(pb)));
                var u = (threshold/s - pa/s) / (pb/s - pa/s);
                DrawTo(At(a + (b-a)*u, threshold), threshold);
            }
            if (pa < pb) { Cross(0); Cross(1); } else { Cross(1); Cross(0); }
            DrawTo(At(b, pb), pb); a = b; pa = pb;
        }
    }
}
