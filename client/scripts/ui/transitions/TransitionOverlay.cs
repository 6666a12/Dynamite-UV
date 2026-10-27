using Godot;

namespace DynamiteUniverse.Ui;

public partial class TransitionOverlay : Control
{
    private const float EdgeWidth = 150f;
    private const float EdgeCut = 34f;
    private const float ScanWidth = 9f;

    private static readonly Color FieldColor = new("070b16");

    private float _coverage;
    private float _waitingScan;
    private UiMotionProfile _profile = UiMotionProfile.For(UiMotionMode.Full);
    private TransitionKind _kind;
    private Label _label = null!;
    private Label _detail = null!;
    private TrackHandoffPresentation _handoff = null!;

    public const float DischargeSeconds = TrackHandoffPresentation.DischargeSeconds;

    public float Coverage
    {
        get => _coverage;
        set
        {
            _coverage = Mathf.Clamp(value, 0f, 1f);
            UpdateContentVisibility();
            QueueRedraw();
            if (IsInstanceValid(_handoff))
                _handoff.Coverage = _coverage;
        }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        SetAnchorsPreset(LayoutPreset.TopLeft);
        BuildStandardCopy();
        _handoff = new TrackHandoffPresentation
        {
            Name = "TrackHandoffPresentation",
            Size = UiLayout.DesignSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_handoff);
    }

    public void Configure(TransitionKind kind, string? label, string? detail,
        UiMotionProfile profile, TrackRelayPresentation? relay = null)
    {
        _kind = kind;
        _profile = profile;
        _waitingScan = 0f;
        _label.Text = string.IsNullOrWhiteSpace(label) ? DefaultLabel(kind) : label;
        _detail.Text = string.IsNullOrWhiteSpace(detail) ? DefaultDetail(kind) : detail;
        _handoff.Configure(kind, label, detail, profile, relay);
        _handoff.Coverage = _coverage;
        UpdateContentVisibility();
        QueueRedraw();
    }

    public void MarkReady() => _handoff.MarkReady();
    public void MarkFailed() => _handoff.MarkFailed();
    public void BeginHandoffReveal(bool discharge) => _handoff.BeginReveal(discharge);
    public void AdvanceHandoff(double delta) => _handoff.Advance(delta);
    public void RefreshHandoffLayers() => _handoff.RefreshLayers();
    public bool IsDischargeRunning => _handoff.IsDischargeRunning;
    public void ClearPresentation() => _handoff.Clear();

    public void AdvanceWaitingScan(double delta)
    {
        if (!_profile.AllowLoop)
            return;
        var loop = Math.Max(0.01, _profile.LoopDuration);
        _waitingScan = Mathf.PosMod(_waitingScan + (float)(delta / loop), 1f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_kind is TransitionKind.Gameplay or TransitionKind.Restart || _coverage <= 0f)
            return;
        var direction = _kind == TransitionKind.Back ? -1f : 1f;
        DrawBackdrop(direction);
        if (_profile.AllowDirectionalMotion)
        {
            DrawSignalSlices(direction);
            DrawCutEdge(direction);
            DrawScan(direction);
        }
        DrawSignalMarks(direction, 1f);
    }

    private void BuildStandardCopy()
    {
        _label = new Label
        {
            Position = new Vector2(112, 872), Size = new Vector2(900, 46),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontOverride("font", UiFonts.TechBold);
        _label.AddThemeFontSizeOverride("font_size", 28);
        _label.AddThemeColorOverride("font_color", UiFonts.Text);
        AddChild(_label);
        _detail = new Label
        {
            Position = new Vector2(114, 926), Size = new Vector2(1080, 34),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _detail.AddThemeFontOverride("font", UiFonts.Tech);
        _detail.AddThemeFontSizeOverride("font_size", 16);
        _detail.AddThemeColorOverride("font_color", UiFonts.Dim);
        AddChild(_detail);
    }

    private void DrawBackdrop(float direction)
    {
        if (!_profile.AllowDirectionalMotion)
        {
            DrawRect(new Rect2(Vector2.Zero, UiLayout.DesignSize),
                new Color(0.027f, 0.043f, 0.086f, _coverage));
            return;
        }
        var width = UiLayout.DesignWidth * _coverage;
        var rect = direction > 0f
            ? new Rect2(0, 0, width, UiLayout.DesignHeight)
            : new Rect2(UiLayout.DesignWidth - width, 0, width, UiLayout.DesignHeight);
        DrawRect(rect, FieldColor);
        if (_coverage < 0.12f)
            return;
        var line = new Color(UiFonts.Line, 0.28f);
        for (var x = rect.Position.X - UiLayout.DesignHeight; x < rect.End.X; x += 170f)
            DrawLine(new Vector2(x, UiLayout.DesignHeight), new Vector2(x + 380, 0), line, 2f);
    }

    private void DrawSignalSlices(float direction)
    {
        if (_coverage >= 0.999f)
            return;
        var edge = direction > 0f
            ? UiLayout.DesignWidth * _coverage
            : UiLayout.DesignWidth * (1f - _coverage);
        var shade = new Color("101a2f");
        var offsets = new[] { -78f, 0f, 66f };
        for (var i = 0; i < offsets.Length; i++)
        {
            var y = i * UiLayout.DesignHeight / 3f;
            var h = UiLayout.DesignHeight / 3f + 2f;
            var sliceEdge = edge + offsets[i] * direction;
            var rect = direction > 0f
                ? new Rect2(Mathf.Max(0f, sliceEdge - 116f), y, 116f, h)
                : new Rect2(sliceEdge, y, 116f, h);
            DrawRect(rect, new Color(shade, 0.64f));
        }
    }

    private void DrawCutEdge(float direction)
    {
        if (_coverage >= 0.999f)
            return;
        var edge = UiLayout.DesignWidth * _coverage;
        Vector2[] points;
        if (direction > 0f)
        {
            points = [new(edge - EdgeWidth, 0), new(edge, 0),
                new(edge, UiLayout.DesignHeight - EdgeCut), new(edge - EdgeCut, UiLayout.DesignHeight),
                new(edge - EdgeWidth, UiLayout.DesignHeight)];
        }
        else
        {
            edge = UiLayout.DesignWidth - edge;
            points = [new(edge + EdgeWidth, 0), new(edge, 0), new(edge, EdgeCut),
                new(edge + EdgeCut, UiLayout.DesignHeight), new(edge + EdgeWidth, UiLayout.DesignHeight)];
        }
        DrawColoredPolygon(points, new Color("101a2f"));
    }

    private void DrawScan(float direction)
    {
        float x;
        if (_coverage >= 0.999f && _profile.AllowLoop)
            x = Mathf.Lerp(100f, UiLayout.DesignWidth - 100f, _waitingScan);
        else
            x = direction > 0f ? UiLayout.DesignWidth * _coverage : UiLayout.DesignWidth * (1f - _coverage);
        DrawRect(new Rect2(x - ScanWidth * 2f, 0, ScanWidth * 4f, UiLayout.DesignHeight),
            new Color(UiFonts.Cyan, 0.08f));
        DrawRect(new Rect2(x - ScanWidth * 0.5f, 0, ScanWidth, UiLayout.DesignHeight),
            new Color(UiFonts.Cyan, 0.88f));
        DrawLine(new Vector2(x - 26f, 0), new Vector2(x + 8f, 0), Colors.White, 3f);
        DrawLine(new Vector2(x - 8f, UiLayout.DesignHeight), new Vector2(x + 26f, UiLayout.DesignHeight),
            Colors.White, 3f);
    }

    private void DrawSignalMarks(float direction, float alpha)
    {
        if (alpha <= 0.001f)
            return;
        var accentX = direction > 0f ? 112f : UiLayout.DesignWidth - 404f;
        DrawRect(new Rect2(accentX, 842, 288, 3), new Color(UiFonts.Cyan, alpha));
        DrawRect(new Rect2(accentX, 976, 124, 2), new Color(UiFonts.Cyan, 0.48f * alpha));
        for (var i = 0; i < 3; i++)
            DrawRect(new Rect2(accentX + 142 + i * 24, 972, 12, 6),
                new Color(UiFonts.Cyan, (0.38f + i * 0.18f) * alpha));
    }

    private void UpdateContentVisibility()
    {
        if (_label is null || _detail is null)
            return;
        if (_kind is TransitionKind.Gameplay or TransitionKind.Restart)
        {
            _label.Visible = false;
            _detail.Visible = false;
            return;
        }
        var show = _coverage >= 0.82f;
        _label.Visible = show;
        _detail.Visible = show;
    }

    private static string DefaultLabel(TransitionKind kind) => kind switch
    {
        TransitionKind.Back => "SIGNAL RETURN",
        TransitionKind.Gameplay => "TRACK RELAY",
        TransitionKind.Restart => "SIGNAL RELOCK",
        _ => "SIGNAL LOCK",
    };

    private static string DefaultDetail(TransitionKind kind) => kind switch
    {
        TransitionKind.Gameplay => "SYNCING PLAYFIELD",
        TransitionKind.Restart => "REARMING CURRENT TRACK",
        TransitionKind.Back => "RESTORING PREVIOUS CHANNEL",
        _ => "SWITCHING CHANNEL",
    };
}
