using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.ChartEditor.Editor;
using DynamiteUniverse.ChartEditor.Settings;

namespace DynamiteUniverse.ChartEditor.Controls;

public sealed record EditorPlacementRequest(
    EditorTool Tool,
    EditorTrack Track,
    double Second,
    double Bar,
    double Center,
    double Width,
    double? TailSecond = null,
    double? TailBar = null,
    double? TailCenter = null,
    double? TailWidth = null);

public sealed class EditorTimeChangedEventArgs(double second) : EventArgs
{
    public double Second { get; } = second;
}

public sealed class ChartCanvas : Control
{
    public const double DesignWidth = 1920;
    public const double DesignHeight = 1080;

    private const double CenterLineY = 861;
    private const double MixerBarY = 632;
    private const double MixerBarLeft = 675;
    private const double MixerBarLength = 569;
    private const double CenterX0 = 280;
    private const double PositionUnit = 273.2;
    private const double LeftLineX = 184;
    private const double RightLineX = 1736;
    private const double SideDistanceScale = 0.75;
    private const double SideY0 = 840;
    private const double SidePositionUnit = 115;
    private const double SideNoteLengthUnit = 102;
    private const double SideBarLengthUnit = 190;
    private const double BaseFallSpeedPixelsPerSecond = 1026;
    private const double NoteVisualScale = 0.95;
    private const double CenterTravel = 790;
    private const double SideLead = 691 / SideDistanceScale;

    private static readonly Color Background = Color.Parse("#070B17");
    private static readonly Color BackgroundRaised = Color.Parse("#101936");
    private static readonly Color Grid = Color.Parse("#263B67");
    private static readonly Color Cyan = Color.Parse("#35E0FF");
    private static readonly Color Pink = Color.Parse("#FF4D91");
    private static readonly Color Green = Color.Parse("#53E38D");
    private static readonly Color Amber = Color.Parse("#FFBD59");
    private static readonly Color Red = Color.Parse("#FF7185");
    private static readonly Color Text = Color.Parse("#E8EDFF");
    private static readonly Color Dim = Color.Parse("#8490B7");
    private static readonly Typeface UiTypeface = new("Inter");

    private readonly List<HitTarget> _hitTargets = [];
    private readonly DispatcherTimer _animationTimer;
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private Point? _pointer;
    private Point? _gestureStart;
    private PlacementDraft? _holdHeadDraft;
    private string? _pathInsertOwnerId;
    private bool _scrubbing;
    private EditorSelectionInfo? _selection;
    private string? _selectedId;
    private string? _committedPreview;
    private Point? _commitPulsePoint;
    private double _commitPulseStart;
    private EditorChartDocument? _document;
    private double _currentSecond;
    private double _playerSpeed = 1;
    private EditorTrack _activeTrack = EditorTrack.Center;
    private EditorTool _activeTool = EditorTool.Select;

    public ChartCanvas()
    {
        Width = DesignWidth;
        Height = DesignHeight;
        ClipToBounds = true;
        Focusable = true;
        _animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50),
            DispatcherPriority.Background, (_, _) =>
            {
                if (MotionMode == EditorMotionMode.Full || _committedPreview is not null)
                    InvalidateVisual();
            });
        _animationTimer.Start();
        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += OnKeyDown;
    }

    public event EventHandler<EditorSelectionInfo?>? SelectionChanged;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<EditorPlacementRequest>? PlacementRequested;
    public event EventHandler<EditorTimeChangedEventArgs>? TimeChanged;

    public EditorChartDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            CurrentSecond = 0;
            ClearSelection(raiseEvent: false);
            InvalidateVisual();
        }
    }

    public void SetSelection(EditorSelectionInfo? selection)
    {
        _selection = selection;
        _selectedId = selection?.Id;
        InvalidateVisual();
    }

    public double CurrentSecond
    {
        get => _currentSecond;
        set
        {
            var duration = Document?.DurationSec ?? 0;
            _currentSecond = Math.Clamp(value, 0, duration);
            InvalidateVisual();
        }
    }

    public double PlayerSpeed
    {
        get => _playerSpeed;
        set
        {
            _playerSpeed = Math.Clamp(value, 0.25, 4);
            InvalidateVisual();
        }
    }

    public EditorTrack ActiveTrack
    {
        get => _activeTrack;
        set
        {
            _activeTrack = value;
            _pointer = null;
            InvalidateVisual();
        }
    }

    public EditorTool ActiveTool
    {
        get => _activeTool;
        set
        {
            _activeTool = value;
            _committedPreview = null;
            _gestureStart = null;
            _holdHeadDraft = null;
            InvalidateVisual();
        }
    }

    public int GridDivisor { get; set; } = 16;
    public bool SnapEnabled { get; set; } = true;
    public EditorMotionMode MotionMode { get; set; } = EditorMotionMode.Full;
    public string SongTitle { get; set; } = "NO CHART LOADED";
    public string DifficultyText { get; set; } = "—";
    public double AudioOffsetSec { get; set; }
    public string? PathInsertOwnerId
    {
        get => _pathInsertOwnerId;
        set
        {
            _pathInsertOwnerId = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _hitTargets.Clear();

        context.FillRectangle(new SolidColorBrush(Background), Bounds);
        DrawBackdrop(context);
        DrawActiveTrackField(context);
        DrawGrid(context);
        DrawJudgeGeometry(context);

        if (Document is not null)
        {
            DrawPathBodies(context, Document);
            DrawNotes(context, Document);
            DrawBpmEvents(context, Document);
            DrawScrollEvents(context, Document);
        }

        DrawToolPreview(context);
        DrawCommitPulse(context);
    }

    private void DrawBackdrop(DrawingContext context)
    {
        var depthPen = new Pen(new SolidColorBrush(Cyan, 0.045), 2);
        for (var x = 420; x <= 1500; x += 180)
            context.DrawLine(depthPen, new Point(960, 85), new Point(x, CenterLineY));
        for (var y = 180; y < CenterLineY; y += 130)
            context.DrawLine(new Pen(new SolidColorBrush(Text, 0.025), 1),
                new Point(184, y), new Point(1736, y));

        context.DrawRectangle(new SolidColorBrush(Pink, 0.08),
            new Pen(new SolidColorBrush(Pink, 0.42), 2),
            new Rect(MixerBarLeft, MixerBarY - 12, MixerBarLength, 24), 4, 4);
        context.FillRectangle(new SolidColorBrush(Cyan, 0.38),
            new Rect(956, MixerBarY - 16, 8, 32));
    }

    private void DrawActiveTrackField(DrawingContext context)
    {
        var color = ActiveTool switch
        {
            EditorTool.Bpm => Pink,
            EditorTool.Scroll => Green,
            _ => Cyan,
        };
        var fill = new SolidColorBrush(color, 0.035);
        var locator = new Pen(new SolidColorBrush(color, 0.34), 2);
        switch (ActiveTool is EditorTool.Bpm or EditorTool.Scroll ? EditorTrack.Events : ActiveTrack)
        {
            case EditorTrack.Center:
            case EditorTrack.Events:
                context.FillRectangle(fill, new Rect(CenterX0, 76, PositionUnit * 5, CenterLineY - 76));
                context.DrawLine(locator, new Point(CenterX0, 76), new Point(CenterX0, CenterLineY));
                break;
            case EditorTrack.Left:
                context.FillRectangle(fill, new Rect(LeftLineX, 260, 776, 600));
                context.DrawLine(locator, new Point(LeftLineX, 260), new Point(LeftLineX, CenterLineY));
                break;
            case EditorTrack.Right:
                context.FillRectangle(fill, new Rect(960, 260, RightLineX - 960, 600));
                context.DrawLine(locator, new Point(RightLineX, 260), new Point(RightLineX, CenterLineY));
                break;
        }
    }

    private void DrawGrid(DrawingContext context)
    {
        if (Document is null)
            return;

        var currentBar = Document.SecondsToBar(CurrentSecond);
        var divisor = Math.Max(1, GridDivisor);
        var firstTick = (long)Math.Floor(currentBar * divisor) - 4;
        for (var index = firstTick; index < firstTick + divisor * 9; index++)
        {
            if (index < 0)
                continue;
            var bar = index / (double)divisor;
            var remaining = VisualDistance(bar);
            var major = index % divisor == 0;
            var pen = new Pen(new SolidColorBrush(major ? Cyan : Grid, major ? 0.28 : 0.20),
                major ? 2 : 1);

            var centerY = CenterLineY - remaining;
            if (centerY is >= 76 and <= CenterLineY)
                context.DrawLine(pen, new Point(CenterX0, centerY),
                    new Point(CenterX0 + PositionUnit * 5, centerY));

            var leftX = LeftLineX + remaining * SideDistanceScale;
            var rightX = RightLineX - remaining * SideDistanceScale;
            if (leftX is >= LeftLineX and <= 960)
                context.DrawLine(pen, new Point(leftX, 260), new Point(leftX, 860));
            if (rightX is >= 960 and <= RightLineX)
                context.DrawLine(pen, new Point(rightX, 260), new Point(rightX, 860));
        }
    }

    private void DrawJudgeGeometry(DrawingContext context)
    {
        var glowPen = new Pen(new SolidColorBrush(Cyan, 0.18), 8);
        context.DrawLine(glowPen, new Point(60, CenterLineY), new Point(1860, CenterLineY));
        context.DrawLine(glowPen, new Point(LeftLineX, 70), new Point(LeftLineX, CenterLineY));
        context.DrawLine(glowPen, new Point(RightLineX, 70), new Point(RightLineX, CenterLineY));

        var linePen = new Pen(new SolidColorBrush(Text, 0.76), 3);
        context.DrawLine(linePen, new Point(60, CenterLineY), new Point(1860, CenterLineY));
        context.DrawLine(linePen, new Point(LeftLineX, 70), new Point(LeftLineX, CenterLineY));
        context.DrawLine(linePen, new Point(RightLineX, 70), new Point(RightLineX, CenterLineY));
    }

    private void DrawPathBodies(DrawingContext context, EditorChartDocument document)
    {
        foreach (var note in document.Notes.Where(note => note.IsPath))
        {
            var isMixer = note.Type == V2NoteType.Mixer;
            var color = isMixer ? Pink : Amber;
            var points = note.Path;
            for (var index = 0; index + 1 < points.Count; index++)
            {
                var first = PointFor(note.Track, points[index]);
                var second = PointFor(note.Track, points[index + 1]);
                if (!SegmentMayBeVisible(first, second))
                    continue;

                var firstHalf = isMixer ? 5 : BodyHalfWidth(note.Track, points[index].Width);
                var secondHalf = isMixer ? 5 : BodyHalfWidth(note.Track, points[index + 1].Width);
                var geometry = RibbonGeometry(note.Track, first, second, firstHalf, secondHalf);
                context.DrawGeometry(new SolidColorBrush(color, isMixer ? 0.34 : 0.22),
                    new Pen(new SolidColorBrush(color, isMixer ? 0.68 : 0.82), isMixer ? 2 : 3),
                    geometry);
            }
        }
    }

    private void DrawNotes(DrawingContext context, EditorChartDocument document)
    {
        foreach (var note in document.Notes)
        {
            var point = PointFor(note.Track, note.Path[0]);
            var remaining = VisualDistance(note.Bar);
            var lead = note.Track == EditorTrack.Center ? CenterTravel : SideLead;
            if (remaining < -70 || remaining > lead + 100)
                continue;

            var rect = NoteRect(note.Track, note.Type, note.Center, note.Width, point);
            var color = TypeColor(note.Type);
            var selected = string.Equals(note.Id, _selectedId, StringComparison.Ordinal);
            var pulse = selected && MotionMode != EditorMotionMode.Off
                ? 0.5 + 0.5 * Math.Sin(_animationClock.Elapsed.TotalSeconds * 4.2)
                : 0;
            DrawNoteSurface(context, rect, note.Track, color, selected,
                note.Type == V2NoteType.BarLine ? 0.42 : 0.92, pulse);
            DrawTypeDecoration(context, rect, note.Track, note.Type, color);

            if (note.IsPath)
            {
                DrawPathTerminal(context, note, color, selected);
                DrawPathNodes(context, note, color);
            }

            _hitTargets.Add(new HitTarget(rect.Inflate(9), new EditorSelectionInfo(
                EditorObjectKind.Note, note.Id, note.Track,
                $"{note.Second:0.000}s · bar {note.ExactTime}",
                note.Center.ToString("0.###", CultureInfo.InvariantCulture),
                note.Width.ToString("0.###", CultureInfo.InvariantCulture),
                note.IsPath
                    ? $"{note.Type} · path to {note.EndSecond:0.000}s · {note.Path.Count} evaluated samples"
                    : note.Type.ToString())));
        }
    }

    private void DrawNoteSurface(DrawingContext context, Rect rect, EditorTrack track,
        Color color, bool selected, double opacity, double pulse = 0)
    {
        var geometry = CutNoteGeometry(rect, track, 7);
        var start = track == EditorTrack.Center ? rect.TopLeft : rect.TopLeft;
        var end = track == EditorTrack.Center ? rect.BottomLeft : rect.TopRight;
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(start, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(end, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Lift(color, 0.38), 0),
                new GradientStop(color, 0.54),
                new GradientStop(Lift(color, -0.34), 1),
            },
            Opacity = opacity,
        };
        context.DrawGeometry(gradient,
            new Pen(new SolidColorBrush(selected ? Text : Lift(color, 0.24), 1),
                selected ? 4 + pulse * 2 : 2),
            geometry);
        if (selected && pulse > 0)
        {
            var glowRect = rect.Inflate(5 + pulse * 5);
            context.DrawGeometry(null,
                new Pen(new SolidColorBrush(color, 0.14 + pulse * 0.18), 3),
                CutNoteGeometry(glowRect, track, 9));
        }
        var highlight = track == EditorTrack.Center
            ? new Pen(new SolidColorBrush(Text, selected ? 0.72 : 0.30), 2)
            : new Pen(new SolidColorBrush(Text, selected ? 0.60 : 0.24), 2);
        if (track == EditorTrack.Center)
            context.DrawLine(highlight, new Point(rect.Left + 8, rect.Top + 4),
                new Point(rect.Right - 8, rect.Top + 4));
        else
            context.DrawLine(highlight, new Point(rect.Left + 4, rect.Top + 8),
                new Point(rect.Left + 4, rect.Bottom - 8));
    }

    private static void DrawTypeDecoration(DrawingContext context, Rect rect, EditorTrack track,
        V2NoteType type, Color color)
    {
        var pen = new Pen(new SolidColorBrush(Text, 0.72), 2);
        if (type == V2NoteType.ExTap)
        {
            context.DrawLine(pen, rect.TopLeft + new Vector(6, 4), rect.BottomRight - new Vector(6, 4));
            context.DrawLine(pen, rect.TopRight + new Vector(-6, 4), rect.BottomLeft + new Vector(6, -4));
        }
        else if (type == V2NoteType.Mine)
        {
            var center = rect.Center;
            var radius = Math.Min(rect.Width, rect.Height) * 0.22;
            context.DrawEllipse(null, pen, center, radius, radius);
            context.DrawLine(pen, center + new Vector(-radius, 0), center + new Vector(radius, 0));
            context.DrawLine(pen, center + new Vector(0, -radius), center + new Vector(0, radius));
        }
        else if (type == V2NoteType.Drag)
        {
            if (track == EditorTrack.Center)
                context.DrawLine(new Pen(new SolidColorBrush(color, 0.9), 3),
                    new Point(rect.Left + 8, rect.Center.Y), new Point(rect.Right - 8, rect.Center.Y));
            else
                context.DrawLine(new Pen(new SolidColorBrush(color, 0.9), 3),
                    new Point(rect.Center.X, rect.Top + 8), new Point(rect.Center.X, rect.Bottom - 8));
        }
    }

    private void DrawPathTerminal(DrawingContext context, EditorNoteModel note, Color color, bool selected)
    {
        var terminal = note.Path[^1];
        var remaining = VisualDistance(terminal.Bar);
        var lead = note.Track == EditorTrack.Center ? CenterTravel : SideLead;
        if (remaining < -70 || remaining > lead + 100)
            return;
        var point = PointFor(note.Track, terminal);
        var rect = NoteRect(note.Track, note.Type, terminal.Center, terminal.Width, point);
        DrawNoteSurface(context, rect, note.Track, color, selected, 0.58);
    }

    private void DrawPathNodes(DrawingContext context, EditorNoteModel note, Color color)
    {
        foreach (var node in note.PathNodes)
        {
            var remaining = VisualDistance(node.Bar);
            var lead = note.Track == EditorTrack.Center ? CenterTravel : SideLead;
            if (remaining < -70 || remaining > lead + 100)
                continue;
            var sample = new EditorPathPoint(node.Bar, node.Second, node.Center, node.Width);
            var point = PointFor(note.Track, sample);
            var selected = string.Equals(node.Id, _selectedId, StringComparison.Ordinal);
            var radius = node.IsTerminal ? 9 : 7;
            var fill = node.Judge == true ? color : BackgroundRaised;
            context.DrawEllipse(new SolidColorBrush(fill, node.Judge == false ? 0.94 : 0.82),
                new Pen(new SolidColorBrush(selected ? Text : color, selected ? 4 : 2), selected ? 3 : 2),
                point, radius, radius);
            if (node.Judge == false)
                context.DrawLine(new Pen(new SolidColorBrush(color, 0.8), 2),
                    point + new Vector(-5, 0), point + new Vector(5, 0));
            var target = new Rect(point.X - 14, point.Y - 14, 28, 28);
            _hitTargets.Add(new HitTarget(target, new EditorSelectionInfo(
                EditorObjectKind.PathNode, node.Id, note.Track,
                $"{node.Second:0.000}s · bar {node.ExactTime}",
                node.Center.ToString("0.###", CultureInfo.InvariantCulture),
                node.Width.ToString("0.###", CultureInfo.InvariantCulture),
                $"{note.Type} · {(node.IsTerminal ? "terminal" : "control")} · {node.CurveToNext?.ToString() ?? "tail"}",
                note.Id)));
        }
    }

    private static Geometry CutNoteGeometry(Rect rect, EditorTrack track, double cut)
    {
        var geometry = new StreamGeometry();
        using var stream = geometry.Open();
        if (track == EditorTrack.Center)
        {
            stream.BeginFigure(new Point(rect.Left + cut, rect.Top), true);
            stream.LineTo(new Point(rect.Right - cut, rect.Top));
            stream.LineTo(new Point(rect.Right, rect.Top + cut));
            stream.LineTo(new Point(rect.Right - cut, rect.Bottom));
            stream.LineTo(new Point(rect.Left + cut, rect.Bottom));
            stream.LineTo(new Point(rect.Left, rect.Bottom - cut));
        }
        else
        {
            stream.BeginFigure(new Point(rect.Left + cut, rect.Top), true);
            stream.LineTo(new Point(rect.Right, rect.Top + cut));
            stream.LineTo(new Point(rect.Right, rect.Bottom - cut));
            stream.LineTo(new Point(rect.Left + cut, rect.Bottom));
            stream.LineTo(new Point(rect.Left, rect.Bottom - cut));
            stream.LineTo(new Point(rect.Left, rect.Top + cut));
        }
        stream.EndFigure(true);
        return geometry;
    }

    private static Color Lift(Color color, double amount)
    {
        static byte Channel(byte value, double delta) => (byte)Math.Clamp(
            Math.Round(delta >= 0 ? value + (255 - value) * delta : value * (1 + delta)), 0, 255);
        return Color.FromArgb(color.A, Channel(color.R, amount), Channel(color.G, amount),
            Channel(color.B, amount));
    }

    private void DrawBpmEvents(DrawingContext context, EditorChartDocument document)
    {
        foreach (var bpm in document.Bpms)
        {
            var remaining = VisualDistance(bpm.Bar);
            var y = CenterLineY - remaining;
            if (y is < 74 or > CenterLineY + 30)
                continue;

            var selected = string.Equals(bpm.Id, _selectedId, StringComparison.Ordinal);
            var pen = new Pen(new SolidColorBrush(Pink, selected ? 1 : 0.62), selected ? 4 : 2);
            context.DrawLine(pen, new Point(CenterX0, y), new Point(1060, y));
            var marker = new Rect(300, y - 18, 190, 36);
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#291326"), 0.96), pen,
                marker, 3, 3);
            DrawLabel(context, $"BPM  {bpm.Bpm:0.##}", new Point(318, y - 10), 15, Text);
            _hitTargets.Add(new HitTarget(marker.Inflate(7), new EditorSelectionInfo(
                EditorObjectKind.Bpm, bpm.Id, EditorTrack.Events, $"{bpm.Second:0.000}s · bar {bpm.ExactTime}",
                "—", "—", $"BPM {bpm.Bpm:0.###}")));
        }
    }

    private void DrawScrollEvents(DrawingContext context, EditorChartDocument document)
    {
        foreach (var scroll in document.Scrolls)
        {
            var remaining = VisualDistance(scroll.Bar);
            var y = CenterLineY - remaining;
            if (y is < 74 or > CenterLineY + 30)
                continue;
            var selected = string.Equals(scroll.Id, _selectedId, StringComparison.Ordinal);
            var marker = new Rect(1120, y - 16, 230, 32);
            var pen = new Pen(new SolidColorBrush(Green, selected ? 1 : 0.66), selected ? 4 : 2);
            context.DrawLine(pen, new Point(1060, y), new Point(1400, y));
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#10281E"), 0.94), pen,
                marker, 3, 3);
            DrawLabel(context, $"SCROLL  {scroll.Value:0.##}", new Point(1138, y - 9), 14, Text);
            _hitTargets.Add(new HitTarget(marker.Inflate(7), new EditorSelectionInfo(
                EditorObjectKind.Scroll, scroll.Id, EditorTrack.Events,
                $"{scroll.Second:0.000}s · bar {scroll.ExactTime}",
                "—", "—", $"Scroll {scroll.Value:0.###} · {scroll.CurveToNext?.ToString() ?? "tail"}")));
        }
    }

    private void DrawCommitPulse(DrawingContext context)
    {
        if (_committedPreview is null || MotionMode == EditorMotionMode.Off)
            return;
        var age = _animationClock.Elapsed.TotalSeconds - _commitPulseStart;
        if (age >= 0.45)
        {
            _committedPreview = null;
            return;
        }
        var progress = Math.Clamp(age / 0.45, 0, 1);
        var radius = 18 + progress * 52;
        var alpha = (1 - progress) * 0.7;
        var center = _commitPulsePoint ?? new Point(960, CenterLineY);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Cyan, alpha), 4), center, radius, radius);
    }

    private void BeginCommitPulse(Point point, string message)
    {
        _committedPreview = message;
        _commitPulsePoint = point;
        _commitPulseStart = _animationClock.Elapsed.TotalSeconds;
    }

    private void DrawToolPreview(DrawingContext context)
    {
        if (_pointer is not { } pointer || Document is null || ActiveTool == EditorTool.Select)
            return;

        var track = ActiveTool is EditorTool.Bpm or EditorTool.Scroll ? EditorTrack.Events : ActiveTrack;
        if (!InsideTrack(pointer, track))
            return;

        var draft = DraftFor(_gestureStart ?? pointer, pointer, track);
        var color = ActiveTool switch
        {
            EditorTool.Drag => Green,
            EditorTool.Hold => Amber,
            EditorTool.Mixer => Pink,
            EditorTool.Mine => Red,
            EditorTool.BarLine => Dim,
            EditorTool.Scroll => Green,
            EditorTool.Bpm => Pink,
            _ => Cyan,
        };

        if (ActiveTool is EditorTool.Bpm or EditorTool.Scroll)
        {
            var y = CenterLineY - VisualDistance(draft.Bar);
            context.DrawLine(new Pen(new SolidColorBrush(color, 0.72), 2, dashStyle: DashStyle.Dash),
                new Point(CenterX0, y), new Point(1400, y));
            DrawTag(context, $"{(ActiveTool == EditorTool.Bpm ? "BPM" : "SCROLL")} · BAR {FormatBar(draft.Bar)}",
                new Point(300, Math.Clamp(y - 48, 82, 815)), color);
            return;
        }

        DrawCrosshair(context, draft, track, color);
        if (ActiveTool == EditorTool.PathNode)
        {
            DrawTag(context, $"PATH NODE · BAR {FormatBar(draft.Bar)}",
                new Point(Math.Clamp(draft.Rect.Right + 12, 260, 1420),
                    Math.Clamp(draft.Rect.Top, 82, 800)), color);
            return;
        }
        DrawDraftNote(context, draft, track, color);
        if (ActiveTool is EditorTool.Hold or EditorTool.Mixer && _holdHeadDraft is { } head)
        {
            var headPoint = PointFor(track, head.Sample);
            var tailPoint = PointFor(track, draft.Sample);
            var body = RibbonGeometry(track, headPoint, tailPoint,
                BodyHalfWidth(track, head.Width), BodyHalfWidth(track, draft.Width));
            context.DrawGeometry(new SolidColorBrush(color, 0.14),
                new Pen(new SolidColorBrush(color, 0.72), 3, dashStyle: DashStyle.Dash), body);
            DrawDraftNote(context, head, track, color);
        }

        var action = ActiveTool is EditorTool.Hold or EditorTool.Mixer && _holdHeadDraft is not null
            ? $"{ActiveTool.ToString().ToUpperInvariant()} · SET TAIL"
            : ActiveTool.ToString().ToUpperInvariant();
        DrawTag(context,
            $"{action} · {track.ToString().ToUpperInvariant()}\nBAR {FormatBar(draft.Bar)}   POS {draft.Center:0.###}   WIDTH {draft.Width:0.###}",
            new Point(Math.Clamp(draft.Rect.Right + 12, 260, 1420),
                Math.Clamp(draft.Rect.Top, 82, 800)), color);
    }

    private void DrawDraftNote(DrawingContext context, PlacementDraft draft, EditorTrack track,
        Color color)
    {
        context.DrawRectangle(new SolidColorBrush(color, 0.18),
            new Pen(new SolidColorBrush(color, 0.92), 3, dashStyle: DashStyle.Dash),
            draft.Rect, 4, 4);
    }

    private void DrawCrosshair(DrawingContext context, PlacementDraft draft, EditorTrack track,
        Color color)
    {
        var pen = new Pen(new SolidColorBrush(color, 0.26), 1, dashStyle: DashStyle.Dash);
        var point = PointFor(track, draft.Sample);
        if (track == EditorTrack.Center)
        {
            context.DrawLine(pen, new Point(CenterX0, point.Y),
                new Point(CenterX0 + PositionUnit * 5, point.Y));
            context.DrawLine(pen, new Point(point.X, 76), new Point(point.X, CenterLineY));
        }
        else
        {
            context.DrawLine(pen, new Point(point.X, 260), new Point(point.X, 860));
            context.DrawLine(pen, new Point(LeftLineX, point.Y), new Point(RightLineX, point.Y));
        }
    }

    private PlacementDraft DraftFor(Point start, Point current, EditorTrack track)
    {
        var (bar, startCenter) = PreviewCoordinates(start, track);
        var (_, currentCenter) = PreviewCoordinates(current, track);
        bar = SnapPreviewBar(bar);
        startCenter = Math.Round(startCenter * 8) / 8;
        currentCenter = Math.Round(currentCenter * 8) / 8;
        var dragPixels = Math.Abs(track == EditorTrack.Center
            ? current.X - start.X
            : current.Y - start.Y);
        var width = dragPixels < 8 ? 0.75 : Math.Max(0.125, Math.Abs(currentCenter - startCenter));
        var center = dragPixels < 8 ? startCenter : (startCenter + currentCenter) * 0.5;
        var second = Document!.BarToSeconds(Math.Max(0, bar));
        var sample = new EditorPathPoint(bar, second, center, width);
        var point = PointFor(track, sample);
        var type = ActiveTool switch
        {
            EditorTool.Drag => V2NoteType.Drag,
            EditorTool.ExTap => V2NoteType.ExTap,
            EditorTool.Hold => V2NoteType.Hold,
            EditorTool.Mixer => V2NoteType.Mixer,
            EditorTool.Mine => V2NoteType.Mine,
            EditorTool.BarLine => V2NoteType.BarLine,
            _ => V2NoteType.Tap,
        };
        var rect = NoteRect(track, type, center, width, point);
        return new PlacementDraft(bar, second, center, width, sample, rect);
    }

    private static string FormatBar(double bar)
    {
        var whole = Math.Floor(bar);
        var fraction = bar - whole;
        return fraction <= 0.000001 ? $"{whole:0}" : $"{bar:0.####}";
    }

    private static string FormatTime(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}.{span.Milliseconds:000}";
    }

    private double VisualDistance(double bar)
    {
        if (Document is null)
            return 0;
        var noteSecond = Document.BarToSeconds(Math.Max(0, bar));
        var currentBar = Document.SecondsToBar(CurrentSecond);
        var scroll = Document.ScrollSpeedAtBar(Math.Max(0, currentBar));
        return VisualScrollMath.DistancePixels(noteSecond, CurrentSecond,
            BaseFallSpeedPixelsPerSecond, scroll, PlayerSpeed);
    }

    private Point PointFor(EditorTrack track, EditorPathPoint sample)
    {
        var remaining = VisualDistance(sample.Bar);
        return track switch
        {
            EditorTrack.Center => new Point(CenterX0 + sample.Center * PositionUnit,
                CenterLineY - remaining),
            EditorTrack.Left => new Point(LeftLineX + remaining * SideDistanceScale,
                SideY0 - sample.Center * SidePositionUnit),
            _ => new Point(RightLineX - remaining * SideDistanceScale,
                SideY0 - sample.Center * SidePositionUnit),
        };
    }

    private static Rect NoteRect(EditorTrack track, V2NoteType type,
        double center, double width, Point point)
    {
        if (track == EditorTrack.Center)
        {
            var axis = Math.Max(12, width * PositionUnit * NoteVisualScale);
            var height = type switch
            {
                V2NoteType.Hold => 26,
                V2NoteType.BarLine => 6,
                _ => 24,
            };
            return new Rect(point.X - axis / 2, point.Y - height / 2, axis, height);
        }

        var lengthUnit = type == V2NoteType.BarLine ? SideBarLengthUnit : SideNoteLengthUnit;
        var length = Math.Max(12, width * lengthUnit * NoteVisualScale);
        var thickness = type == V2NoteType.Hold ? 17 : 20;
        return new Rect(point.X - thickness / 2, point.Y - length / 2, thickness, length);
    }

    private static double BodyHalfWidth(EditorTrack track, double width) =>
        track == EditorTrack.Center
            ? Math.Max(4, width * PositionUnit * 0.4)
            : Math.Max(6, width * SideNoteLengthUnit * 0.5 * NoteVisualScale);

    private static Geometry RibbonGeometry(EditorTrack track, Point first, Point second,
        double firstHalf, double secondHalf)
    {
        var geometry = new StreamGeometry();
        using var stream = geometry.Open();
        if (track == EditorTrack.Center)
        {
            stream.BeginFigure(new Point(first.X - firstHalf, first.Y), true);
            stream.LineTo(new Point(first.X + firstHalf, first.Y));
            stream.LineTo(new Point(second.X + secondHalf, second.Y));
            stream.LineTo(new Point(second.X - secondHalf, second.Y));
        }
        else
        {
            stream.BeginFigure(new Point(first.X, first.Y - firstHalf), true);
            stream.LineTo(new Point(second.X, second.Y - secondHalf));
            stream.LineTo(new Point(second.X, second.Y + secondHalf));
            stream.LineTo(new Point(first.X, first.Y + firstHalf));
        }
        stream.EndFigure(true);
        return geometry;
    }

    private static bool SegmentMayBeVisible(Point first, Point second)
    {
        var bounds = new Rect(-100, -150, DesignWidth + 200, DesignHeight + 300);
        return bounds.Contains(first) || bounds.Contains(second) ||
               (Math.Min(first.X, second.X) <= DesignWidth && Math.Max(first.X, second.X) >= 0 &&
                Math.Min(first.Y, second.Y) <= DesignHeight && Math.Max(first.Y, second.Y) >= 0);
    }

    private static Color TypeColor(V2NoteType type) => type switch
    {
        V2NoteType.Drag => Green,
        V2NoteType.Hold => Amber,
        V2NoteType.Mixer => Pink,
        V2NoteType.Mine => Red,
        V2NoteType.BarLine => Dim,
        _ => Cyan,
    };

    private static bool InsideTrack(Point point, EditorTrack track) => track switch
    {
        EditorTrack.Center or EditorTrack.Events =>
            point.X is >= CenterX0 and <= CenterX0 + PositionUnit * 5 &&
            point.Y is >= 76 and <= CenterLineY,
        EditorTrack.Left => point.X is >= LeftLineX and <= 960 && point.Y is >= 260 and <= 860,
        EditorTrack.Right => point.X is >= 960 and <= RightLineX && point.Y is >= 260 and <= 860,
        _ => false,
    };

    private double SnapPreviewBar(double bar)
    {
        var minimum = Document!.SecondsToBar(CurrentSecond);
        if (!SnapEnabled)
            return Math.Max(minimum, bar);
        var divisor = Math.Max(1, GridDivisor);
        return Math.Max(minimum, Math.Round(bar * divisor) / divisor);
    }

    private (double Bar, double Center) PreviewCoordinates(Point point, EditorTrack track)
    {
        var currentBar = Document!.SecondsToBar(CurrentSecond);
        var speed = Math.Max(0.0001, VisualScrollMath.SpeedPixelsPerSecond(
            BaseFallSpeedPixelsPerSecond,
            Document.ScrollSpeedAtBar(Math.Max(0, currentBar)), PlayerSpeed));
        var deltaSeconds = track switch
        {
            EditorTrack.Center or EditorTrack.Events => (CenterLineY - point.Y) / speed,
            EditorTrack.Left => (point.X - LeftLineX) / SideDistanceScale / speed,
            _ => (RightLineX - point.X) / SideDistanceScale / speed,
        };
        var bar = Document.SecondsToBar(Math.Max(0, CurrentSecond + deltaSeconds));
        return track switch
        {
            EditorTrack.Center or EditorTrack.Events =>
                (bar, (point.X - CenterX0) / PositionUnit),
            EditorTrack.Left =>
                (bar, (SideY0 - point.Y) / SidePositionUnit),
            _ =>
                (bar, (SideY0 - point.Y) / SidePositionUnit),
        };
    }

    private static void DrawLabel(DrawingContext context, string text, Point origin,
        double size, Color color)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, UiTypeface, size, new SolidColorBrush(color));
        context.DrawText(formatted, origin);
    }

    private static void DrawTag(DrawingContext context, string text, Point origin, Color color)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, UiTypeface, 14, new SolidColorBrush(color));
        var box = new Rect(origin.X - 10, origin.Y - 7, formatted.Width + 20, formatted.Height + 14);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#050B1B"), 0.94),
            new Pen(new SolidColorBrush(color, 0.8), 2), box, 3, 3);
        context.DrawText(formatted, origin);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        _pointer = eventArgs.GetPosition(this);
        if (_scrubbing)
            SeekFromScrubber(_pointer.Value);
        InvalidateVisual();
    }

    private void OnPointerExited(object? sender, PointerEventArgs eventArgs)
    {
        if (_gestureStart is null && !_scrubbing)
            _pointer = null;
        InvalidateVisual();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        Focus();
        var point = eventArgs.GetPosition(this);
        var properties = eventArgs.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            CancelDraft("Placement cancelled");
            eventArgs.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed)
            return;
        if (point.Y <= 18 && Document is not null)
        {
            _scrubbing = true;
            SeekFromScrubber(point);
            eventArgs.Pointer.Capture(this);
            eventArgs.Handled = true;
            return;
        }

        if (ActiveTool == EditorTool.Select)
        {
            SelectAt(point);
            return;
        }
        if (Document is null)
            return;
        var track = ActiveTool is EditorTool.Bpm or EditorTool.Scroll ? EditorTrack.Events : ActiveTrack;
        if (!InsideTrack(point, track))
            return;
        if (ActiveTool is EditorTool.Bpm or EditorTool.Scroll)
        {
            var draft = DraftFor(point, point, track);
            PlacementRequested?.Invoke(this, new EditorPlacementRequest(
                ActiveTool, ActiveTrack, draft.Second, draft.Bar, draft.Center, draft.Width));
            eventArgs.Handled = true;
            return;
        }
        if (ActiveTool == EditorTool.PathNode)
        {
            if (string.IsNullOrWhiteSpace(PathInsertOwnerId))
            {
                StatusChanged?.Invoke(this, "Select a Hold or Mixer before placing a path node.");
                return;
            }
            var draft = DraftFor(point, point, ActiveTrack);
            PlacementRequested?.Invoke(this, new EditorPlacementRequest(
                ActiveTool, ActiveTrack, draft.Second, draft.Bar, draft.Center, draft.Width));
            eventArgs.Handled = true;
            return;
        }

        _gestureStart = point;
        _pointer = point;
        eventArgs.Pointer.Capture(this);
        eventArgs.Handled = true;
        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (_scrubbing)
        {
            _scrubbing = false;
            eventArgs.Pointer.Capture(null);
            eventArgs.Handled = true;
            return;
        }
        if (_gestureStart is not { } start || Document is null)
            return;

        var end = eventArgs.GetPosition(this);
        var track = ActiveTrack;
        var draft = DraftFor(start, end, track);
        _gestureStart = null;
        eventArgs.Pointer.Capture(null);
        eventArgs.Handled = true;

        if (ActiveTool is EditorTool.Hold or EditorTool.Mixer)
        {
            if (_holdHeadDraft is null)
            {
                _holdHeadDraft = draft;
                _committedPreview = $"{ActiveTool.ToString().ToUpperInvariant()} · SET TAIL";
                StatusChanged?.Invoke(this, $"{ActiveTool} head set. Move through time and place the tail; Esc cancels.");
                InvalidateVisual();
                return;
            }

            var head = _holdHeadDraft;
            if (draft.Bar <= head.Bar)
            {
                StatusChanged?.Invoke(this, $"{ActiveTool} tail must be later than the head.");
                InvalidateVisual();
                return;
            }
            _holdHeadDraft = null;
            PlacementRequested?.Invoke(this, new EditorPlacementRequest(
                ActiveTool, ActiveTrack, head.Second, head.Bar, head.Center, head.Width,
                draft.Second, draft.Bar, draft.Center, draft.Width));
            BeginCommitPulse(PointFor(track, draft.Sample), $"{ActiveTool} placed");
            StatusChanged?.Invoke(this, $"{ActiveTool} placed");
            InvalidateVisual();
            return;
        }

        PlacementRequested?.Invoke(this, new EditorPlacementRequest(
            ActiveTool, ActiveTrack, draft.Second, draft.Bar, draft.Center, draft.Width));
        BeginCommitPulse(PointFor(track, draft.Sample), $"{ActiveTool.ToString().ToUpperInvariant()} placed");
        StatusChanged?.Invoke(this,
            $"{ActiveTool} placed · center {draft.Center:0.###} · width {draft.Width:0.###} · bar {draft.Bar:0.####}");
        InvalidateVisual();
    }

    private void SelectAt(Point point)
    {
        var target = _hitTargets.LastOrDefault(candidate => candidate.Bounds.Contains(point));
        if (target is null)
        {
            ClearSelection();
            StatusChanged?.Invoke(this, "Selection cleared");
        }
        else
        {
            _selection = target.Selection;
            _selectedId = target.Selection.Id;
            SelectionChanged?.Invoke(this, _selection);
            StatusChanged?.Invoke(this, $"Selected {target.Selection.Kind.ToString().ToLowerInvariant()} {target.Selection.Id}");
        }
        InvalidateVisual();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
    {
        if (Document is null)
            return;
        var currentBar = Document.SecondsToBar(CurrentSecond);
        var step = (eventArgs.KeyModifiers & KeyModifiers.Shift) != 0
            ? 1.0
            : 1.0 / Math.Max(1, GridDivisor);
        var nextBar = Math.Max(0, currentBar - Math.Sign(eventArgs.Delta.Y) * step);
        SetTimeFromInput(Document.BarToSeconds(nextBar));
        StatusChanged?.Invoke(this, $"BAR {FormatBar(nextBar)}");
        eventArgs.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape || (_gestureStart is null && _holdHeadDraft is null))
            return;
        CancelDraft("Placement cancelled");
        eventArgs.Handled = true;
    }

    private void SeekFromScrubber(Point point)
    {
        if (Document is null)
            return;
        SetTimeFromInput(Document.DurationSec * Math.Clamp(point.X / DesignWidth, 0, 1));
        StatusChanged?.Invoke(this, $"Time {FormatTime(CurrentSecond)}");
    }

    private void SetTimeFromInput(double second)
    {
        CurrentSecond = second;
        TimeChanged?.Invoke(this, new EditorTimeChangedEventArgs(CurrentSecond));
    }

    private void CancelDraft(string status)
    {
        _gestureStart = null;
        _holdHeadDraft = null;
        _scrubbing = false;
        _committedPreview = null;
        StatusChanged?.Invoke(this, status);
        InvalidateVisual();
    }

    private void ClearSelection(bool raiseEvent = true)
    {
        _selection = null;
        _selectedId = null;
        if (raiseEvent)
            SelectionChanged?.Invoke(this, null);
    }

    private sealed record PlacementDraft(
        double Bar,
        double Second,
        double Center,
        double Width,
        EditorPathPoint Sample,
        Rect Rect);

    private sealed record HitTarget(Rect Bounds, EditorSelectionInfo Selection);
}
