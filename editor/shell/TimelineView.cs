using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Two scales in chart bars: whole-recording onset density above, selected-side rhythm below.</summary>
public sealed class TimelineView : Control
{
    private const double Inset = 8;
    private static IBrush Ink(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static readonly IBrush BackgroundInk = Ink("#090F1E"), OverviewInk = Ink("#111C2E"),
        DensityInk = Ink("#69879C"), Dim = Ink("#9AAFC0"), HeadInk = Ink("#E3FAFF"), RangeInk = Ink("#24364C");
    private static readonly IPen GridBar = new Pen(Ink("#34435D"), 1), GridBeat = new Pen(Ink("#1C293C"), 1),
        RangeBorder = new Pen(Ink("#77C8D9"), 1), Playhead = new Pen(HeadInk, 1.3), JudgeInk = new Pen(HeadInk, 1.4);
    private static readonly IBrush[] NoteInk = [Ink("#40C9FF"), Ink("#FFC66A"), Ink("#B5EBFF"), Ink("#56EEA5"), Ink("#FF75BB"), Ink("#FF6977")];
    private static readonly IPen[] MarkPens = NoteInk.Select(b => (IPen)new Pen(b, 1.8)).ToArray();
    private static readonly IPen[] BandPens = NoteInk.Select(b => (IPen)new Pen(b, 3.2)).ToArray();
    private readonly Dictionary<EditorTrack, TimelineTrackLayout> _tracks = [];
    private readonly Dictionary<double, FormattedText> _labels = [];
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private (string? Text, FormattedText? Label) _trackCaption;
    private readonly List<(IPen Pen, StreamGeometry Geometry)> _detail = [];
    private (double Start, double Scale, double Height, EditorTrack Track)? _detailKey;
    private (double Width, double Height, double Maximum)? _overviewKey;
    private StreamGeometry? _overview;
    private TimelineRhythmData? _rhythm;
    private EditorTrack _track = EditorTrack.Center;
    private double _bar, _maximumBar = 32;
    private IPointer? _pointer;
    private bool _overviewDrag;
    private TimelineBarWindow? _frozen;
    private double? _lastSeek;

    public event EventHandler<double>? SeekRequested;
    public event Action<double>? WheelSteps;
    public double CurrentBar
    {
        get => _bar;
        set { if (double.IsFinite(value) && value != _bar) { _bar = value; InvalidateVisual(); } }
    }
    public double MaximumBar
    {
        get => _maximumBar;
        set
        {
            var max = double.IsFinite(value) && value > 0 ? value : 1;
            if (max == _maximumBar) return;
            _maximumBar = max; InvalidateVisual();
        }
    }
    internal TimelineRhythmData? Rhythm
    {
        get => _rhythm;
        set
        {
            if (ReferenceEquals(value, _rhythm)) return;
            EndDrag(); _rhythm = value; _tracks.Clear(); _detail.Clear(); _detailKey = null; _overviewKey = null;
            IndexCurrentTrack(); InvalidateVisual();
        }
    }
    internal EditorTrack Track
    {
        get => _track;
        set { if (value != _track) { _track = value; IndexCurrentTrack(); InvalidateVisual(); } }
    }
    private void IndexCurrentTrack()
    {
        if (_rhythm is not null && !_tracks.ContainsKey(Track)) _tracks.Add(Track, new(_rhythm.ForTrack(Track)));
    }
    internal TimelineBarWindow DetailWindow => _frozen ?? TimelineBarWindow.Follow(_bar, _maximumBar);
    internal double OverviewHeight => Math.Min(23, Bounds.Height * .23);
    private double Left => Math.Min(Inset, Bounds.Width * .10);
    private double WidthInside => Math.Max(1, Bounds.Width - 2 * Left);
    private double Fraction(double x) => Math.Clamp((x - Left) / WidthInside, 0, 1);
    internal double BarAtPoint(Point p)
    {
        var overview = _pointer != null ? _overviewDrag : p.Y < OverviewHeight;
        return (overview ? new TimelineBarWindow(0, _maximumBar) : DetailWindow).AtFraction(Fraction(p.X));
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        if (rect.Width < 2 || rect.Height < 2) return;
        using var clip = context.PushClip(rect);
        context.FillRectangle(BackgroundInk, rect);
        var window = DetailWindow;
        DrawOverview(context, window);
        if (window.End <= window.Start) return;
        var width = WidthInside; var top = OverviewHeight + 2; var height = Bounds.Height - top - 1;
        if (height < 3) return;
        using var inside = context.PushClip(new Rect(Left, top, width, height));
        // For a moving window the intended span is exactly eight. Do not change the cache
        // key every tick because (start + 8) - start differs by a floating-point ulp.
        var span = window.Start == 0 ? window.End : 8d;
        var scale = width / span;
        if (!double.IsFinite(scale)) return;
        DrawGrid(context, window, top, height, scale);
        // A note at the current bar shares the playhead's exact X. Paint the thin cursor
        // behind its glyph, so it cannot whiten a Tap or a Hold head in a vertical stack.
        var x = Left + window.FractionAt(_bar) * width;
        context.DrawLine(Playhead, new Point(x, top), new Point(x, top + height));
        context.FillRectangle(HeadInk, new Rect(x - 2.5, top, 5, 2));
        if (_tracks.TryGetValue(Track, out var layout) && height > 15)
        {
            var anchor = Math.Floor(window.Start);
            var key = (anchor, scale, height, Track);
            if (_detailKey != key)
            {
                // Cache the visible slice plus its fractional scroll margin. Between whole-bar
                // boundaries the drawing is reused, even while transport ticks keep arriving.
                BuildDetail(layout, anchor, anchor + span + 1, scale, height);
                _detailKey = key;
            }
            using var translate = context.PushTransform(Matrix.CreateTranslation(Left + (anchor - window.Start) * scale, top));
            foreach (var (pen, geometry) in _detail) context.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawOverview(DrawingContext context, TimelineBarWindow window)
    {
        var w = WidthInside; var h = OverviewHeight; var left = Left;
        context.FillRectangle(OverviewInk, new Rect(left, 0, w, h));
        var x1 = left + w * (window.Start / _maximumBar); var x2 = left + w * (window.End / _maximumBar);
        var range = new Rect(x1, 1, Math.Max(1, x2 - x1), Math.Max(0, h - 2));
        context.FillRectangle(RangeInk, range);
        var key = (w, h, _maximumBar);
        if (_overviewKey != key)
        {
            _overview = null;
            if (_rhythm is { AllHits.Count: > 0 })
            {
                var n = (int)Math.Clamp(Math.Ceiling(w / 3), 1, 768);
                var counts = TimelineTrackLayout.Density(_rhythm.AllHits, _maximumBar, n);
                var peak = Math.Log(1 + (double)counts.Max());
                if (peak > 0 && h > 4)
                {
                    _overview = new StreamGeometry(); using var g = _overview.Open();
                    for (var i = 0; i < n; i++)
                    {
                        if (counts[i] == 0) continue;
                        var a = w * i / n; var b = w * (i + 1) / n;
                        var y = h - 2 - Math.Max(1, (h - 4) * Math.Log(1 + (double)counts[i]) / peak);
                        g.BeginFigure(new Point(a, h - 2), true); g.LineTo(new Point(a, y));
                        g.LineTo(new Point(Math.Max(a + .65, b - .8), y)); g.LineTo(new Point(Math.Max(a + .65, b - .8), h - 2)); g.EndFigure(true);
                    }
                }
            }
            _overviewKey = key;
        }
        if (_overview != null)
        {
            using var translate = context.PushTransform(Matrix.CreateTranslation(left, 0));
            context.DrawGeometry(DensityInk, null, _overview);
        }
        context.DrawRectangle(null, RangeBorder, range);
        var x = left + w * Math.Clamp(_bar / _maximumBar, 0, 1);
        context.DrawLine(Playhead, new Point(x, 1), new Point(x, Math.Max(1, h - 1)));
    }

    private void DrawGrid(DrawingContext context, TimelineBarWindow window, double top, double height, double scale)
    {
        var first = Math.Floor(window.Start);
        var lastLabel = double.NegativeInfinity;
        var text = _text[Track switch { EditorTrack.Left => "Context.Left", EditorTrack.Right => "Context.Right", _ => "Context.Center" }];
        if (_trackCaption.Text != text)
            _trackCaption = (text, new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, ShellTypography.OfficialTypeface, 10, HeadInk));
        var caption = _trackCaption.Label!;
        var showCaption = height >= 24 && WidthInside >= caption.Width + 70;
        var labelRight = Left + WidthInside - (showCaption ? caption.Width + 8 : 0);
        // Fixed small loop, even for extreme chart positions where adding 1/4 may round away.
        for (var q = 0; q <= 36; q++)
        {
            var bar = first + q / 4d;
            if (bar < window.Start || bar > window.End) continue;
            var x = Left + (bar - window.Start) * scale; var whole = q % 4 == 0;
            context.DrawLine(whole ? GridBar : GridBeat, new Point(x, whole ? top : top + Math.Min(14, height / 3)), new Point(x, top + height));
            if (!whole || height < 22 || x < lastLabel + 4) continue;
            if (!_labels.TryGetValue(bar, out var label))
            {
                if (_labels.Count > 64) _labels.Clear();
                _labels.Add(bar, label = new FormattedText(bar.ToString("0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, ShellTypography.OfficialTypeface, 10, Dim));
            }
            if (x + 3 + label.Width > labelRight) continue;
            context.DrawText(label, new Point(x + 3, top)); lastLabel = x + 3 + label.Width;
        }
        if (showCaption)
        {
            context.FillRectangle(BackgroundInk, new Rect(labelRight + 1, top, caption.Width + 7, caption.Height));
            context.DrawText(caption, new Point(labelRight + 6, top));
        }
    }

    private void BuildDetail(TimelineTrackLayout layout, double start, double end, double scale, double height)
    {
        _detail.Clear();
        var contentTop = Math.Min(16, height * .28) + 4;
        var contentHeight = Math.Max(0, height - contentTop - 6);
        var minGap = 1.5 / scale;
        // Each series has bounded horizontal resolution. Dense marks coalesce on the same
        // lane/ink, never by shifting time. Genuine crossing sustains still span the viewport.
        foreach (var lane in layout.Lanes)
        {
            var y = contentTop + contentHeight * lane.RelativeY; var type = (int)lane.Type;
            var spans = lane.Bodies;
            var i = TimelineTrackLayout.FirstBody(spans, start);
            if (i < spans.Count && spans[i].Start <= end)
            {
                var path = new StreamGeometry(); using var g = path.Open();
                while (i < spans.Count && spans[i].Start <= end)
                {
                    var a = Math.Max(start, spans[i].Start);
                    var next = i + 1;
                    if (next < spans.Count && spans[next].End < a + minGap)
                        next = TimelineTrackLayout.FirstBody(spans, a + minGap, next);
                    Line(g, (a - start) * scale, y, (Math.Min(end, spans[next - 1].End) - start) * scale, y);
                    i = next;
                }
                _detail.Add((BandPens[type], path));
            }
            Marks(lane.Heads, MarkPens[type], (g, x) =>
            {
                switch (lane.Type)
                {
                    case V2NoteType.Drag: Line(g, x - 2.1, y + 3, x + 2.1, y - 3); break;
                    case V2NoteType.ExTap:
                        g.BeginFigure(new Point(x, y - 3.6), false); g.LineTo(new Point(x + 2.2, y));
                        g.LineTo(new Point(x, y + 3.6)); g.LineTo(new Point(x - 2.2, y)); g.EndFigure(true); break;
                    case V2NoteType.Mine:
                        Line(g, x - 2.5, y - 2.5, x + 2.5, y + 2.5); Line(g, x - 2.5, y + 2.5, x + 2.5, y - 2.5); break;
                    default: Line(g, x, y - 3.4, x, y + 3.4); break;
                }
            });
            Marks(lane.Tails, MarkPens[type], (g, x) => Line(g, x, y - 2.8, x, y + 2.8));
            Marks(lane.Judgements, JudgeInk, (g, x) => Line(g, x, y - 3.3, x, y + 3.3));
        }
        void Marks(List<double> bars, IPen pen, Action<StreamGeometryContext, double> emit)
        {
            var i = TimelineTrackLayout.LowerBound(bars, start);
            if (i == bars.Count || bars[i] > end) return;
            var path = new StreamGeometry(); using var g = path.Open();
            while (i < bars.Count && bars[i] <= end)
            {
                var bar = bars[i++]; emit(g, (bar - start) * scale);
                if (i < bars.Count && bars[i] < bar + minGap) i = TimelineTrackLayout.LowerBound(bars, bar + minGap, i);
            }
            _detail.Add((pen, path));
        }
    }

    private static void Line(StreamGeometryContext g, double x1, double y1, double x2, double y2)
    { g.BeginFigure(new Point(x1, y1), false); g.LineTo(new Point(x2, y2)); g.EndFigure(false); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_pointer != null || e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        var p = e.GetPosition(this);
        _overviewDrag = p.Y < OverviewHeight;
        if (!_overviewDrag) _frozen = DetailWindow; // Capture scale BEFORE seek listeners change CurrentBar.
        _pointer = e.Pointer; _lastSeek = null; e.Pointer.Capture(this);
        Seek(p); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer != _pointer) return;
        Seek(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer != _pointer || e.InitialPressMouseButton != MouseButton.Left) return;
        Seek(e.GetPosition(this)); EndDrag(); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (e.Pointer == _pointer) EndDrag();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); _text.LanguageChanged += InvalidateVisual; }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _text.LanguageChanged -= InvalidateVisual; EndDrag(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (WheelSteps is not { } wheel || e.Delta.Y == 0 || !double.IsFinite(e.Delta.Y)) return;
        wheel(-e.Delta.Y); e.Handled = true;
    }
    private void Seek(Point p)
    {
        var bar = BarAtPoint(p);
        if (_lastSeek == bar) return;
        _lastSeek = bar; SeekRequested?.Invoke(this, bar);
    }
    private void EndDrag()
    {
        var old = _pointer; _pointer = null; _frozen = null; _lastSeek = null;
        if (old?.Captured == this) old.Capture(null);
        if (old != null) InvalidateVisual();
    }
}
