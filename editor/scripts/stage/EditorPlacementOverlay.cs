using System.Globalization;
using Godot;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

/// <summary>当前放置手势的幽灵读数与轨道投影，由 EditorStagePreview 直接显示。</summary>
public sealed record EditorPlacementVisual(int Type, int Track, ExactBarTime Time, ExactBarTime TailTime,
    double Center, double Width, bool TailStage, bool Snapped)
{
    public double Bar => Time.ToDouble();
    public double TailBar => TailTime.ToDouble();
    public string HeadTimeText => TimeText(Time, Snapped);
    public string TailTimeText => TimeText(TailTime, Snapped);
    private static string TimeText(ExactBarTime time, bool snapped) => snapped
        ? ExactBarTimeText.Format(time)
        : time.ToDouble().ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>
/// Note placement ghost + 吸附辅助 overlay。由主代理加到 EditorStagePreview 的 _stageRoot，
/// Position=(0,40)：内部坐标直接是 1920x1080 设计空间，不经 DPI/stageScale/pan 换算。
/// 真实 Note ghost 用 NoteView.Create 绘制（Modulate alpha 0.6），辅助线/读数走 Draw 回调。
/// </summary>
public partial class EditorPlacementOverlay : Control
{
    private const float GhostAlpha = 0.6f;
    private const float GuideAlpha = 0.5f;
    private const float AlignTolerance = 0.000001f;
    private const float BehindLineAllowancePx = 100f;
    private const int LabelFontSize = 22;

    private static readonly Color ProjectionMagenta = new(1f, 0.25f, 0.85f, GuideAlpha);
    private static readonly Color BackingColor = new(0.02f, 0.02f, 0.05f, 0.72f);
    private static readonly Color AlignSamePosition = new(0.25f, 0.55f, 1f);
    private static readonly Color AlignSameWidth = new(1f, 0.3f, 0.3f);
    private static readonly Color AlignBoth = new(1f, 0.3f, 0.85f);

    private readonly Node2D _ghostRoot;
    private readonly Control _guideLayer;

    private EditorPlacementVisual? _placement;
    private V2RuntimeChartMetadata? _metadata;
    private double _currentTime;
    private double _playerSpeed = 1.0;
    private IReadOnlyList<Note> _existingNotes = Array.Empty<Note>();

    public EditorPlacementOverlay()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _ghostRoot = new Node2D { Name = "PlacementGhost", ZIndex = 4 };
        AddChild(_ghostRoot);
        _guideLayer = new Control
        {
            Name = "PlacementGuides",
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 10,
            Size = new Vector2(GameplayStageGeometry.DesignWidth, GameplayStageGeometry.DesignHeight),
        };
        AddChild(_guideLayer);
        _guideLayer.Draw += DrawGuides;
    }

    public void SetPlacement(EditorPlacementVisual? value, V2RuntimeChartMetadata? metadata,
        double currentTime, double playerSpeed, IReadOnlyList<Note> existingNotes)
    {
        _placement = value;
        _metadata = metadata;
        _currentTime = currentTime;
        _playerSpeed = playerSpeed;
        _existingNotes = existingNotes ?? Array.Empty<Note>();
        if (value is null || metadata is null)
        {
            Clear();
            return;
        }
        RebuildGhosts(value, metadata);
        _guideLayer.QueueRedraw();
    }

    public void Clear()
    {
        _placement = null;
        foreach (var child in _ghostRoot.GetChildren()) child.QueueFree();
        _guideLayer.QueueRedraw();
    }

    private double RemainingForBar(Track track, double bar, V2RuntimeChartMetadata metadata) =>
        RemainingFor(track, metadata.BpmTimeline.RuntimeSecondsAt(bar), bar, metadata);

    private double RemainingFor(Track track, double noteSecond, double noteBar,
        V2RuntimeChartMetadata metadata, double noteSpeedMultiplier = 1d) =>
        VisualScrollMath.DistancePixels(noteSecond, _currentTime, GameplayStageGeometry.BaseFallSpeedPx,
            metadata.ScrollMap.RuntimeSpeedAt(noteBar), _playerSpeed, noteSpeedMultiplier);

    private static Track TrackOf(int track) =>
        EditorStagePlacement.ToRuntimeTrack((EditorTrack)track);

    private static bool IsLongType(V2NoteType type) => type is V2NoteType.Hold or V2NoteType.Mixer;

    private static NoteType HeadTypeOf(V2NoteType type) => type switch
    {
        V2NoteType.Drag => NoteType.Drag,
        V2NoteType.ExTap => NoteType.ExTap,
        V2NoteType.Hold => NoteType.HoldHead,
        V2NoteType.Mixer => NoteType.MixerHead,
        V2NoteType.Mine => NoteType.Mine,
        V2NoteType.BarLine => NoteType.BarLine,
        _ => NoteType.Tap,
    };

    private static NoteType TailTypeOf(V2NoteType type) => type == V2NoteType.Hold
        ? NoteType.HoldNode
        : NoteType.MixerNode;

    private Note BuildGhostNote(NoteType type, Track track, double bar, double center, double width,
        V2RuntimeChartMetadata metadata, int subNoteId)
    {
        var second = metadata.BpmTimeline.RuntimeSecondsAt(bar);
        return new Note
        {
            Id = -1,
            SubNoteId = subNoteId,
            Type = type,
            Track = track,
            BarTime = bar,
            Position = center - width * .5,
            Width = width,
            Second = second,
            BakedSecond = second,
        };
    }

    private void RebuildGhosts(EditorPlacementVisual value, V2RuntimeChartMetadata metadata)
    {
        foreach (var child in _ghostRoot.GetChildren()) child.QueueFree();
        var track = TrackOf(value.Track);
        var v2Type = (V2NoteType)Math.Clamp(value.Type, 0, (int)V2NoteType.BarLine);
        var longType = IsLongType(v2Type);
        var head = BuildGhostNote(HeadTypeOf(v2Type), track, value.Bar, value.Center, value.Width,
            metadata, longType ? -2 : -1);
        var headRemaining = RemainingFor(track, head.Second, value.Bar, metadata);
        var travel = GameplayStageGeometry.PreviewTravelPx(track);
        var tailRemaining = double.NegativeInfinity;
        Note? tail = null;
        if (longType)
        {
            tail = BuildGhostNote(TailTypeOf(v2Type), track, value.TailBar, value.Center, value.Width,
                metadata, -1);
            tailRemaining = RemainingFor(track, tail.Second, value.TailBar, metadata);
        }
        var headVisible = headRemaining >= -BehindLineAllowancePx && headRemaining <= travel + 150f;
        var tailVisible = !longType || tailRemaining >= -BehindLineAllowancePx;
        if (!headVisible || !tailVisible)
        {
            _ghostRoot.Visible = false;
            return;
        }
        _ghostRoot.Visible = true;
        var color = NoteVisualSpec.BaseColor(v2Type);
        var headView = NoteView.Create(head, GameplayVisualMapper.SizeFor(head), color);
        headView.SetDepthAlpha(GhostAlpha);
        headView.Position = GameplayVisualMapper.PositionAt(head, (float)headRemaining);
        _ghostRoot.AddChild(headView);
        if (tail is null) return;
        var tailView = NoteView.Create(tail, GameplayVisualMapper.SizeFor(tail), color);
        tailView.SetDepthAlpha(GhostAlpha);
        tailView.Position = GameplayVisualMapper.PositionAt(tail, (float)tailRemaining);
        _ghostRoot.AddChild(tailView);

        var a = GameplayVisualMapper.PositionAt(head, (float)headRemaining);
        var b = GameplayVisualMapper.PositionAt(tail, (float)tailRemaining);
        var wa = GameplayVisualMapper.LinkHalfFor(head);
        var wb = GameplayVisualMapper.LinkHalfFor(tail);
        if (!GameplayVisualMapper.ClipToJudgeLine(track, ref a, ref b, ref wa, ref wb))
            return;
        var body = new Polygon2D
        {
            Color = GameplayVisualMapper.LinkColorFor(head),
            Polygon = GameplayVisualMapper.LinkPolygon(track, a, b, wa, wb),
            Modulate = new Color(1f, 1f, 1f, GhostAlpha),
            ZIndex = -1,
        };
        _ghostRoot.AddChild(body);
        if (v2Type == V2NoteType.Hold)
        {
            _ghostRoot.AddChild(new Line2D
            {
                Width = 3f,
                DefaultColor = new Color(NoteVisualSpec.Hold, .9f * GhostAlpha),
                Points = GameplayVisualMapper.LinkFrame(track, a, b, wa, wb),
                ZIndex = -1,
            });
        }
    }

    private void DrawGuides()
    {
        if (_placement is not { } value || _metadata is not { } metadata) return;
        if (!_ghostRoot.Visible) return;
        var track = TrackOf(value.Track);
        var v2Type = (V2NoteType)Math.Clamp(value.Type, 0, (int)V2NoteType.BarLine);
        var risks = EditorSpatialAnalysis.Point((EditorTrack)value.Track, v2Type, value.Center, value.Width);
        if (risks != EditorSpatialRisk.None)
            DrawLabel(new Vector2(600, 90), $"[!] {risks}", new Color("ffb454"), true);
        // Recommended authoring limits, not input/clipping boundaries.
        _guideLayer.DrawLine(new Vector2(GameplayStageGeometry.CenterX0, 100),
            new Vector2(GameplayStageGeometry.CenterX0, GameplayStageGeometry.CenterLineY), new Color(1, 1, 1, .15f));
        _guideLayer.DrawLine(new Vector2(GameplayStageGeometry.CenterToStageX(5), 100),
            new Vector2(GameplayStageGeometry.CenterToStageX(5), GameplayStageGeometry.CenterLineY), new Color(1, 1, 1, .15f));
        var longType = IsLongType(v2Type);
        var baseColor = NoteVisualSpec.BaseColor(v2Type);
        var guideColor = new Color(baseColor, GuideAlpha);
        var headRemaining = (float)RemainingForBar(track, value.Bar, metadata);
        var headPoint = GameplayVisualMapper.PositionAt(
            BuildGhostNote(HeadTypeOf(v2Type), track, value.Bar, value.Center, value.Width,
                metadata, -1), headRemaining);
        var tailRemaining = float.NaN;
        Vector2 tailPoint = default;
        if (longType)
        {
            tailRemaining = (float)RemainingForBar(track, value.TailBar, metadata);
            tailPoint = GameplayVisualMapper.PositionAt(
                BuildGhostNote(TailTypeOf(v2Type), track, value.TailBar, value.Center, value.Width,
                    metadata, -1), tailRemaining);
        }

        // 其他两轨同 Bar 的品红投影线（仅在前方可视范围画）。
        foreach (var other in Enum.GetValues<Shared.Chart.Track>())
        {
            if (other == track) continue;
            var otherRemaining = (float)RemainingForBar(other, value.Bar, metadata);
            if (otherRemaining < -BehindLineAllowancePx ||
                otherRemaining > GameplayStageGeometry.PreviewTravelPx(other) + 150f)
                continue;
            DrawTrackTimeLine(other, otherRemaining, ProjectionMagenta, 2f);
        }

        // 当前轨目标时间线 + 位置导引线（颜色按 Note 类型）。
        DrawTrackTimeLine(track, headRemaining, guideColor, 2.5f);
        DrawPositionGuide(track, value.Center, headPoint, guideColor);

        // Hold/Mixer 尾阶段突出尾点。
        if (longType && !float.IsNaN(tailRemaining))
        {
            var accent = value.TailStage ? Colors.White : new Color(baseColor.Lightened(0.4f), .85f);
            _guideLayer.DrawCircle(tailPoint, value.TailStage ? 11f : 8f, new Color(baseColor, .9f));
            _guideLayer.DrawArc(tailPoint, value.TailStage ? 14f : 11f, 0f, Mathf.Tau, 24, accent, 2.5f, true);
        }

        DrawAlignmentSquares(value, track, metadata);

        // 读数：头（bar + center + width），Hold/Mixer 尾阶段尾读数放大、头读数减淡防重叠。
        var headText = $"{value.HeadTimeText}  c{EditorPlacementGuide.ValueText(value.Center)}"
            + $" w{EditorPlacementGuide.ValueText(value.Width)}";
        var headColor = longType && value.TailStage
            ? new Color(Colors.White, .55f)
            : baseColor.Lightened(0.45f);
        DrawLabel(HeadLabelAnchor(track, headPoint, longType), headText, headColor,
            emphasized: !(longType && value.TailStage));
        if (longType)
        {
            var tailText = value.TailTimeText;
            DrawLabel(TailLabelAnchor(track, tailPoint, longType), tailText,
                baseColor.Lightened(0.6f), emphasized: value.TailStage);
        }
    }

    private void DrawTrackTimeLine(Track track, float remaining, Color color, float width)
    {
        switch (track)
        {
            case Shared.Chart.Track.Center:
                var y = GameplayStageGeometry.CenterLineY - remaining;
                _guideLayer.DrawLine(new Vector2(GameplayStageGeometry.CenterX0, y),
                    new Vector2(GameplayStageGeometry.CenterX0 + 5f * GameplayStageGeometry.CenterUnitPx, y),
                    color, width, true);
                break;
            default:
                var sideX = track == Shared.Chart.Track.Left
                    ? GameplayStageGeometry.LeftLineX + remaining * GameplayStageGeometry.SideDistanceScale
                    : GameplayStageGeometry.RightLineX - remaining * GameplayStageGeometry.SideDistanceScale;
                _guideLayer.DrawLine(new Vector2(sideX, GameplayStageGeometry.SideY0 - 5f * GameplayStageGeometry.SideUnitPx),
                    new Vector2(sideX, GameplayStageGeometry.SideY0), color, width, true);
                break;
        }
    }

    private void DrawPositionGuide(Track track, double center, Vector2 notePoint, Color color)
    {
        const float overshoot = 48f;
        switch (track)
        {
            case Shared.Chart.Track.Center:
                var x = GameplayStageGeometry.CenterX0 + (float)center * GameplayStageGeometry.CenterUnitPx;
                _guideLayer.DrawLine(new Vector2(x, GameplayStageGeometry.CenterLineY),
                    new Vector2(x, notePoint.Y - overshoot), color, 2f, true);
                break;
            case Shared.Chart.Track.Left:
                var leftY = GameplayStageGeometry.SideY0 - (float)center * GameplayStageGeometry.SideUnitPx;
                _guideLayer.DrawLine(new Vector2(GameplayStageGeometry.LeftLineX, leftY),
                    new Vector2(notePoint.X + overshoot * GameplayStageGeometry.SideDistanceScale, leftY),
                    color, 2f, true);
                break;
            default:
                var rightY = GameplayStageGeometry.SideY0 - (float)center * GameplayStageGeometry.SideUnitPx;
                _guideLayer.DrawLine(new Vector2(GameplayStageGeometry.RightLineX, rightY),
                    new Vector2(notePoint.X - overshoot * GameplayStageGeometry.SideDistanceScale, rightY),
                    color, 2f, true);
                break;
        }
    }

    private void DrawAlignmentSquares(EditorPlacementVisual value, Track track,
        V2RuntimeChartMetadata metadata)
    {
        var travel = GameplayStageGeometry.PreviewTravelPx(track);
        foreach (var note in _existingNotes)
        {
            if (note.Track != track) continue;
            // Existing notes project with their own scroll multiplier, like the stage renderer;
            // only the new placement ghost keeps unit speed.
            var remaining = RemainingFor(track, note.Second, note.BarTime, metadata,
                note.V2Metadata?.ScrollSpeedMultiplier ?? 1d);
            if (remaining < -50f || remaining > travel + 80f) continue;
            var relation = EditorPlacementGuide.Classify(value.Center, value.Width,
                note.Position + note.Width * .5, note.Width, AlignTolerance);
            var color = relation switch
            {
                EditorAlignRelation.SamePosition => AlignSamePosition,
                EditorAlignRelation.SameWidth => AlignSameWidth,
                EditorAlignRelation.Both => AlignBoth,
                _ => (Color?)null,
            };
            if (color is not { } square) continue;
            var point = GameplayVisualMapper.PositionAt(note, (float)remaining);
            var rect = new Rect2(point - new Vector2(7f, 7f), new Vector2(14f, 14f));
            _guideLayer.DrawRect(rect.Grow(2f), new Color(0f, 0f, 0f, .7f));
            _guideLayer.DrawRect(rect, square);
        }
    }

    private static Vector2 HeadLabelAnchor(Track track, Vector2 point, bool longType)
    {
        // Hold/Mixer 头读数在尾阶段会减淡并移向判定线一侧，给尾读数让位。
        var inward = longType ? 26f : 12f;
        return track switch
        {
            Shared.Chart.Track.Center => point + new Vector2(30f, -inward - 30f),
            Shared.Chart.Track.Left => point + new Vector2(0f, 30f),
            _ => point + new Vector2(0f, 30f),
        };
    }

    private static Vector2 TailLabelAnchor(Track track, Vector2 point, bool longType) =>
        track == Shared.Chart.Track.Center
            ? point + new Vector2(30f, longType ? 8f : -38f)
            : point + new Vector2(0f, -64f);

    private void DrawLabel(Vector2 topLeft, string text, Color color, bool emphasized)
    {
        var font = ThemeDB.FallbackFont;
        var textSize = font.GetStringSize(text, fontSize: LabelFontSize);
        var rect = new Rect2(topLeft, textSize + new Vector2(14f, 10f));
        _guideLayer.DrawRect(rect, BackingColor);
        _guideLayer.DrawRect(rect, new Color(1f, 1f, 1f, emphasized ? .32f : .16f), false, 1.5f);
        var baseline = new Vector2(rect.Position.X + 7f, rect.Position.Y + 5f + font.GetAscent(LabelFontSize));
        _guideLayer.DrawString(font, baseline + new Vector2(2f, 2f), text, fontSize: LabelFontSize,
            modulate: new Color(0f, 0f, 0f, .9f));
        _guideLayer.DrawString(font, baseline, text, fontSize: LabelFontSize, modulate: color);
    }
}
