using Godot;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Game;

/// <summary>Visual-only stage used by gameplay and the chart editor. It has no judgement or input state.</summary>
public partial class GameplayStageRenderer : Node2D
{
    private readonly Dictionary<int, NoteView> _views = new();
    private readonly List<(Note From, Note To, Polygon2D Body, Line2D? Frame)> _links = new();
    private int _curvedLinkCount;
    private IReadOnlyList<Note> _notes = Array.Empty<Note>();
    private IReadOnlySet<int> _ghostNoteIds = new HashSet<int>();
    private float _remainingScale = 1f;
    private double _previewBar;
    private double _previewTime;
    private Func<Note, double, float>? _previewDistanceProvider;
    private Node2D? _chromeRoot;
    private Node2D? _noteRoot;
    public bool ShowControlNodes { get; set; } = true;
    public bool ShowLinks { get; set; } = true;
    /// <summary>Dynamic gameplay views can live in the same visual layer as editor previews.</summary>
    public Node2D NoteLayer
    {
        get { EnsureLayers(); return _noteRoot!; }
    }

    public void SetNotes(IReadOnlyList<Note> notes, float remainingScale = 1f)
    {
        _notes = notes;
        _remainingScale = remainingScale;
        Rebuild();
        SetPreviewBar(_previewBar);
    }

    /// <summary>Marks editor-only preview notes so they use the original ghost alpha.</summary>
    public void SetGhostNoteIds(IReadOnlySet<int>? ids)
    {
        _ghostNoteIds = ids ?? new HashSet<int>();
        Rebuild();
        SetPreviewTime(_previewTime);
    }

    /// <summary>Uses the runtime visual-distance equation for previews that have an audio timeline.</summary>
    public void SetPreviewDistanceProvider(Func<Note, double, float>? provider)
    {
        _previewDistanceProvider = provider;
        UpdatePreviewPositions();
    }

    public void SetPreviewBar(double currentBar)
    {
        _previewBar = currentBar;
        UpdatePreviewPositions();
    }

    public void SetPreviewTime(double currentTime)
    {
        _previewTime = currentTime;
        UpdatePreviewPositions();
    }

    private void UpdatePreviewPositions()
    {
        foreach (var note in _notes)
            if (_views.TryGetValue(note.Id, out var view))
                view.Position = GameplayVisualMapper.PositionAt(note, Remaining(note));
        foreach (var link in _links)
            UpdateLink(link);
    }

    public int RenderedNoteCount => _views.Count;
    public int RenderedLinkCount => _links.Count;
    public int RenderedCurvedLinkCount => _curvedLinkCount;

    public void BuildCommunityStageChrome()
    {
        EnsureLayers();
        foreach (var child in _chromeRoot!.GetChildren()) child.Free();
        _chromeRoot.AddChild(new GameplayBackdrop { ZIndex = -2 });
        var lineColor = new Color(.35f, .35f, .4f);
        AddChromeLine(new Vector2(60f, GameplayStageGeometry.CenterLineY - 2f), new Vector2(1800f, 4f), lineColor);
        AddChromeLine(new Vector2(GameplayStageGeometry.LeftLineX - 2f, 70f), new Vector2(4f, GameplayStageGeometry.CenterLineY - 70f), lineColor);
        AddChromeLine(new Vector2(GameplayStageGeometry.RightLineX - 2f, 70f), new Vector2(4f, GameplayStageGeometry.CenterLineY - 70f), lineColor);
        AddChromeLine(new Vector2(956f, 618f), new Vector2(8f, 28f), new Color(.5f, .5f, 1f));
        _chromeRoot.AddChild(new ColorRect
        {
            Position = new Vector2(675f, 620f), Size = new Vector2(569f, 24f),
            Color = new Color(NoteVisualSpec.MixerBar, .15f), MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = -1,
        });
        _chromeRoot.AddChild(new ColorRect
        {
            Position = new Vector2(675f, 620f), Size = new Vector2(569f, 2f),
            Color = new Color(NoteVisualSpec.MixerBar, .68f), MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = -1,
        });
        _chromeRoot.AddChild(new ColorRect
        {
            Position = new Vector2(675f, 642f), Size = new Vector2(569f, 2f),
            Color = new Color(NoteVisualSpec.MixerBar, .68f), MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = -1,
        });
    }

    public void Rebuild()
    {
        EnsureLayers();
        foreach (var child in _noteRoot!.GetChildren()) child.Free();
        _views.Clear();
        _links.Clear();
        _curvedLinkCount = 0;
        var byId = _notes.ToDictionary(note => note.Id);
        foreach (var note in _notes)
        {
            if (!ShowControlNodes && note.Type is NoteType.HoldNode or NoteType.MixerNode) continue;
            var ghost = _ghostNoteIds.Contains(note.Id);
            var remaining = Remaining(note);
            if (ghost && !ShouldRenderGhost(note, remaining, byId)) continue;
            var view = NoteView.Create(note, GameplayVisualMapper.SizeFor(note), GameplayVisualMapper.ColorFor(note), ghost: ghost);
            view.SetDepthAlpha(ghost ? 0.6f : 1f);
            // Original Hold ghosts clamp the head to the judgement rail once it
            // has crossed the line; ordinary ghosts disappear below it.
            var drawRemaining = ghost && note.Type == NoteType.HoldHead ? Mathf.Max(0f, remaining) : remaining;
            view.Position = GameplayVisualMapper.PositionAt(note, drawRemaining);
            _noteRoot.AddChild(view);
            _views[note.Id] = view;
        }

        if (!ShowLinks) return;
        foreach (var from in _notes)
        {
            if (from.SubNoteId < 0 || !byId.TryGetValue(from.SubNoteId, out var to)) continue;
            if (from.Type is not (NoteType.HoldHead or NoteType.HoldNode or NoteType.MixerHead or NoteType.MixerNode)) continue;
            var body = new Polygon2D { Color = GameplayVisualMapper.LinkColorFor(from), ZIndex = -1 };
            var ghostLink = _ghostNoteIds.Contains(from.Id) || _ghostNoteIds.Contains(to.Id);
            body.Modulate = new Color(1f, 1f, 1f, ghostLink ? 0.6f : 1f);
            Line2D? frame = from.Type is NoteType.HoldHead or NoteType.HoldNode
                ? new Line2D { Width = 3f, DefaultColor = new Color(1f, .8f, .35f, .9f), ZIndex = -1 }
                : null;
            if (frame is not null) frame.Modulate = new Color(1f, 1f, 1f, ghostLink ? 0.6f : 1f);
            _noteRoot.AddChild(body);
            if (frame is not null) _noteRoot.AddChild(frame);
            _links.Add((from, to, body, frame));
            if (from.V2Metadata?.CurveToNext is { } pathCurve && pathCurve != V2PathCurve.Linear)
                _curvedLinkCount++;
            UpdateLink(_links[^1]);
        }
    }

    private bool ShouldRenderGhost(Note note, float remaining, IReadOnlyDictionary<int, Note> byId)
    {
        var travel = GameplayStageGeometry.PreviewTravelPx(note.Track);
        if (note.Type is NoteType.Tap or NoteType.Drag or NoteType.ExTap or NoteType.Mine or NoteType.BarLine)
            return remaining >= 0f && remaining <= travel;
        if (note.Type == NoteType.HoldHead)
        {
            if (note.SubNoteId < 0 || !byId.TryGetValue(note.SubNoteId, out var tail))
                return false;
            return remaining <= travel + 100f && Remaining(tail) >= 0f;
        }
        return remaining >= 0f && remaining <= travel;
    }

    private void EnsureLayers()
    {
        if (IsInstanceValid(_chromeRoot) && IsInstanceValid(_noteRoot)) return;
        _chromeRoot = new Node2D { Name = "CommunityStageChrome", ZIndex = -2 };
        _noteRoot = new Node2D { Name = "CommunityStageNotes", ZIndex = 1 };
        AddChild(_chromeRoot);
        AddChild(_noteRoot);
    }

    private void AddChromeLine(Vector2 position, Vector2 size, Color color) => _chromeRoot!.AddChild(new ColorRect
    {
        Position = position, Size = size, Color = color,
        MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = -1,
    });

    private float Remaining(Note note)
    {
        if (_previewDistanceProvider is not null)
            return _previewDistanceProvider(note, _previewTime);
        return GameplayStageGeometry.PreviewDistanceForBars(note.Track,
            note.BarTime - _previewBar) * _remainingScale;
    }

    private void UpdateLink((Note From, Note To, Polygon2D Body, Line2D? Frame) link)
    {
        var a = GameplayVisualMapper.PositionAt(link.From, Remaining(link.From));
        var b = GameplayVisualMapper.PositionAt(link.To, Remaining(link.To));
        var wa = GameplayVisualMapper.LinkHalfFor(link.From);
        var wb = GameplayVisualMapper.LinkHalfFor(link.To);
        var ghostLink = _ghostNoteIds.Contains(link.From.Id) || _ghostNoteIds.Contains(link.To.Id);
        // noteTemp's initial Hold preview has head and tail at the same time,
        // yet the original renderer still shows its minimum terminal box.
        if (ghostLink && link.From.Type == NoteType.HoldHead && a.DistanceSquaredTo(b) < 1f)
        {
            b = link.From.Track switch
            {
                Track.Center => a + Vector2.Up * 66f,
                Track.Left => a + Vector2.Right * 66f,
                _ => a + Vector2.Left * 66f,
            };
        }
        if (!GameplayVisualMapper.ClipToJudgeLine(link.From.Track, ref a, ref b, ref wa, ref wb))
        {
            link.Body.Visible = false;
            if (link.Frame is not null) link.Frame.Visible = false;
            return;
        }
        var curve = link.From.V2Metadata?.CurveToNext ?? V2PathCurve.Linear;
        if (curve == V2PathCurve.Linear)
        {
            link.Body.Polygon = GameplayVisualMapper.LinkPolygon(link.From.Track, a, b, wa, wb);
            if (link.Frame is not null) link.Frame.Points = GameplayVisualMapper.LinkFrame(link.From.Track, a, b, wa, wb);
        }
        else
        {
            var points = new List<Vector2>(13);
            var halfWidths = new List<float>(13);
            for (var index = 0; index <= 12; index++)
            {
                var u = index / 12d;
                var progress = curve == V2PathCurve.Smooth
                    ? u * u * (3d - 2d * u)
                    : V2Curves.Progress(curve, u);
                points.Add(a.Lerp(b, (float)progress));
                halfWidths.Add(Mathf.Lerp(wa, wb, (float)progress));
            }
            link.Body.Polygon = GameplayVisualMapper.LinkRibbon(link.From.Track, points, halfWidths);
            if (link.Frame is not null) link.Frame.Points = GameplayVisualMapper.LinkRibbonFrame(link.From.Track, points, halfWidths);
        }
        link.Body.Visible = true;
        if (link.Frame is not null)
        {
            link.Frame.Visible = true;
        }
    }
}
