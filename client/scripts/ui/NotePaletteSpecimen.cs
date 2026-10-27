using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Ui;

/// <summary>Palette preview adapter: real NoteView/material and hit effects, no judging or global palette writes.</summary>
public partial class NotePaletteSpecimen : Node2D
{
    private NoteView _head = null!;
    private NoteView _tail = null!;
    private Polygon2D _body = null!;
    private Line2D _outline = null!;
    private GameplayHitBloom? _bloom;
    private GameplayHitParticles? _particles;
    private NoteType _type;
    private Color _color;
    private float _width = 272f;
    private float _tailWidth = 240f;
    private float _gap = 172f;
    private float _brightness = 1f;
    private bool _showPath;
    private bool _initialized;
    private double _hitRemaining;
    private bool _residual;
    private bool _selected;

    public override void _Ready()
    {
        // Keep the path above the page background; a negative absolute Z would hide it.
        ZIndex = 1;
        _body = new Polygon2D();
        _outline = new Line2D { Width = 3f, Closed = true };
        AddChild(_body);
        AddChild(_outline);
        _head = NoteView.Create(Model(NoteType.Tap), new Vector2(_width, 24), Colors.White);
        _tail = NoteView.Create(Model(NoteType.HoldNode), new Vector2(_tailWidth, 26), Colors.White);
        AddChild(_head);
        AddChild(_tail);
        _tail.Visible = false;
        _initialized = true;
        SetProcess(false);
    }

    public void Configure(NoteType type, Color color, float width, float brightness,
        bool showPath = false, float tailWidth = 240f, float gap = 172f, bool selected = false)
    {
        StopHit();
        _type = type;
        _color = color;
        _width = width;
        _tailWidth = tailWidth;
        _gap = gap;
        _brightness = brightness;
        _showPath = showPath && type == NoteType.HoldHead;
        _selected = selected;
        if (!_initialized) return;
        RestoreNotes();
        QueueRedraw();
    }

    private void RestoreNotes()
    {
        _head.Reuse(Model(_type), new Vector2(_width, _type == NoteType.HoldHead ? 26 : 24),
            _color, brightness: _brightness);
        _head.Position = Vector2.Zero;
        _tail.Visible = _body.Visible = _outline.Visible = _showPath;
        if (!_showPath) return;
        _tail.Reuse(Model(NoteType.HoldNode), new Vector2(_tailWidth, 26), _color, brightness: _brightness);
        _tail.Position = new Vector2(0, -_gap);
        var points = new Vector2[]
        {
            new(-_tailWidth * .421f, -_gap + 13), new(_tailWidth * .421f, -_gap + 13),
            new(_width * .421f, -13), new(-_width * .421f, -13),
        };
        _body.Polygon = points;
        _body.Color = new Color(_color, .28f);
        _outline.Points = points;
        _outline.DefaultColor = new Color(_color, .9f);
    }

    public void PlayHit(NotePalette palette, bool burstFollowsNote, GameSettings settings)
    {
        if (!_initialized || !IsVisibleInTree()) return;
        StopHit();
        RestoreNotes();
        var kind = NoteVisualSpec.HitEffect(_type);
        var accent = NoteVisualSpec.EffectAccent(_type, _type == NoteType.Mine ? JudgeGrade.Miss : JudgeGrade.Perfect,
            palette, burstFollowsNote);
        _hitRemaining = .85;
        if (_type is NoteType.HoldHead or NoteType.MixerHead)
            _head.SetContactLight();
        else if (_type != NoteType.Mine && settings.HitResidualSeconds > 0)
        {
            _head.BeginResidual(settings.HitResidualSeconds);
            _residual = true;
        }
        else if (_type != NoteType.Mine)
            _head.Visible = false;

        if (settings.HitEffectLevel != HitEffectLevel.Off)
        {
            if (_bloom is null)
            {
                _bloom = new GameplayHitBloom { ZIndex = 3 };
                AddChild(_bloom);
            }
            _bloom.Intensity = settings.FlashIntensityScale;
            _bloom.ResetFor(Vector2.Zero, 0, accent, kind, _width, 1f);
            _bloom.SetProcess(true);
            if (_type != NoteType.Mine)
            {
                _particles ??= GameplayHitParticles.CreatePooled(this);
                _particles.ResetFor(Vector2.Zero, 0, accent, kind, 1f,
                    settings.HitEffectLevel == HitEffectLevel.Reduced ? UiMotionMode.Reduced : UiMotionMode.Full);
            }
        }
        SetProcess(true);
    }

    public void StopHit()
    {
        var wasPlaying = _hitRemaining > 0;
        _hitRemaining = 0;
        _residual = false;
        if (_bloom is not null)
        {
            _bloom.ResetForPool();
            _bloom.SetProcess(false);
        }
        _particles?.ResetForPool();
        SetProcess(false);
        if (wasPlaying && _initialized) RestoreNotes();
    }

    public override void _Process(double delta)
    {
        _hitRemaining -= delta;
        if (_residual && _head.TickTtl(delta))
        {
            _residual = false;
            _head.Visible = false;
        }
        if (_bloom is { IsExpired: true })
        {
            _bloom.ResetForPool();
            _bloom.SetProcess(false);
        }
        if (_particles is { IsExpired: true }) _particles.ResetForPool();
        if (_hitRemaining > 0) return;
        StopHit();
        RestoreNotes();
    }

    public override void _Draw()
    {
        if (!_selected) return;
        var half = _width / 2;
        var color = new Color("A6BFB6");
        DrawPolyline([new(-half - 8, -21), new(-half - 18, -21), new(-half - 18, 21), new(-half - 8, 21)], color, 1.5f, true);
        DrawPolyline([new(half + 8, -21), new(half + 18, -21), new(half + 18, 21), new(half + 8, 21)], color, 1.5f, true);
    }

    private static Note Model(NoteType type) => new()
    {
        Id = 0, SubNoteId = -1, Type = type, Track = Track.Center,
        BarTime = 0, Position = .5, Width = 1, Second = 0, BakedSecond = 0,
    };
}
