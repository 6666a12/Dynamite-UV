using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Fixed-design opaque Signal Lock and cover-capable Track Handoff presentation.</summary>
public partial class TransitionOverlay : Control
{
    private const float EdgeWidth = 150f;
    private const float EdgeCut = 34f;
    private const float ScanWidth = 9f;

    private float _coverage;
    private float _waitingScan;
    private UiMotionProfile _profile = UiMotionProfile.For(UiMotionMode.Full);
    private TransitionKind _kind;
    private TrackRelayPresentation? _relay;
    private Label _label = null!;
    private Label _detail = null!;
    private Control _relayContext = null!;
    private CoverPlaceholder _coverFallback = null!;
    private TextureRect _cover = null!;
    private Label _relayHeading = null!;
    private Label _relayStatus = null!;
    private Label _relayTitle = null!;
    private Label _relayDifficulty = null!;

    public float Coverage
    {
        get => _coverage;
        set
        {
            _coverage = Mathf.Clamp(value, 0f, 1f);
            UpdateContentVisibility();
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        SetAnchorsPreset(LayoutPreset.TopLeft);

        BuildStandardCopy();
        BuildRelayContext();
    }

    public void Configure(
        TransitionKind kind,
        string? label,
        string? detail,
        UiMotionProfile profile,
        TrackRelayPresentation? relay = null)
    {
        _kind = kind;
        _profile = profile;
        _relay = relay;
        _waitingScan = 0f;
        _label.Text = string.IsNullOrWhiteSpace(label) ? DefaultLabel(kind) : label;
        _detail.Text = string.IsNullOrWhiteSpace(detail) ? DefaultDetail(kind) : detail;

        if (IsTrackHandoff)
        {
            _relayHeading.Text = kind == TransitionKind.Restart
                ? "TRACK HANDOFF · RE-SYNC"
                : "TRACK HANDOFF";
            _relayStatus.AddThemeColorOverride("font_color", UiFonts.Dim);
            _relayStatus.Text = "PLAYFIELD / LOCKING";
            _relayTitle.Text = relay?.Title ?? _label.Text;
            _relayDifficulty.Text = relay?.Difficulty ?? _detail.Text;
            _cover.Texture = relay?.Cover;
        }
        else
        {
            _cover.Texture = null;
        }

        UpdateContentVisibility();
        QueueRedraw();
    }

    public void MarkReady()
    {
        if (!IsTrackHandoff)
            return;
        _relayStatus.Text = "SYNC ESTABLISHED · READY";
        QueueRedraw();
    }

    public void MarkFailed()
    {
        if (!IsTrackHandoff)
            return;
        _relayStatus.Text = "PLAYFIELD / NO READY SIGNAL";
        _relayStatus.AddThemeColorOverride("font_color", UiFonts.Pink);
        QueueRedraw();
    }

    public void ClearPresentation()
    {
        _relay = null;
        if (IsInstanceValid(_cover))
            _cover.Texture = null;
        if (IsInstanceValid(_relayContext))
        {
            _relayContext.Visible = false;
            _relayContext.Modulate = Colors.White;
        }
    }

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
        if (_coverage <= 0f)
            return;

        var direction = _kind == TransitionKind.Back ? -1f : 1f;
        DrawBackdrop(direction);
        if (_profile.AllowDirectionalMotion)
        {
            DrawSignalSlices(direction);
            DrawCutEdge(direction);
            DrawScan(direction);
        }
        DrawSignalMarks(direction);
        if (IsTrackHandoff && _coverage >= 0.82f)
            DrawRelaySignalField();
    }

    private void BuildStandardCopy()
    {
        _label = new Label
        {
            Position = new Vector2(112, 872),
            Size = new Vector2(900, 46),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontOverride("font", UiFonts.TechBold);
        _label.AddThemeFontSizeOverride("font_size", 28);
        _label.AddThemeColorOverride("font_color", UiFonts.Text);
        AddChild(_label);

        _detail = new Label
        {
            Position = new Vector2(114, 926),
            Size = new Vector2(1080, 34),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _detail.AddThemeFontOverride("font", UiFonts.Tech);
        _detail.AddThemeFontSizeOverride("font_size", 16);
        _detail.AddThemeColorOverride("font_color", UiFonts.Dim);
        AddChild(_detail);
    }

    private void BuildRelayContext()
    {
        _relayContext = new Control
        {
            Name = "TrackHandoffContext",
            Size = UiLayout.DesignSize,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_relayContext);

        _relayHeading = MakeLabel(new Vector2(112, 94), new Vector2(920, 46), 27,
            UiFonts.Text, bold: true);
        _relayContext.AddChild(_relayHeading);

        var lockLabel = MakeLabel(new Vector2(1448, 100), new Vector2(360, 32), 16,
            UiFonts.Cyan, bold: true);
        lockLabel.Text = "CONTEXT LOCKED";
        lockLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _relayContext.AddChild(lockLabel);

        _relayContext.AddChild(new CutPanel
        {
            Position = new Vector2(112, 210),
            Size = new Vector2(540, 540),
            Cut = 22,
            Fill = new Color("0b1228"),
            Border = new Color(UiFonts.Cyan, 0.64f),
            BorderWidth = 2f,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        _coverFallback = new CoverPlaceholder
        {
            Position = new Vector2(132, 230),
            Size = new Vector2(500, 500),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _relayContext.AddChild(_coverFallback);
        _cover = new TextureRect
        {
            Position = new Vector2(132, 230),
            Size = new Vector2(500, 500),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _relayContext.AddChild(_cover);
        _relayContext.AddChild(new ColorRect
        {
            Position = new Vector2(132, 230),
            Size = new Vector2(500, 500),
            Color = new Color(0.02f, 0.04f, 0.10f, 0.18f),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var contextTag = MakeLabel(new Vector2(760, 258), new Vector2(800, 32), 16,
            UiFonts.Dim, bold: true);
        contextTag.Text = "TRACK CONTEXT / COMMITTED";
        _relayContext.AddChild(contextTag);

        _relayTitle = MakeLabel(new Vector2(756, 320), new Vector2(980, 92), 54,
            UiFonts.Text, bold: true);
        _relayContext.AddChild(_relayTitle);

        _relayDifficulty = MakeLabel(new Vector2(760, 432), new Vector2(920, 46), 25,
            UiFonts.Cyan, bold: true);
        _relayContext.AddChild(_relayDifficulty);

        _relayStatus = MakeLabel(new Vector2(760, 650), new Vector2(800, 38), 19,
            UiFonts.Dim, bold: true);
        _relayContext.AddChild(_relayStatus);
    }

    private static Label MakeLabel(Vector2 position, Vector2 size, int fontSize, Color color,
        bool bold = false)
    {
        var label = new Label
        {
            Position = position,
            Size = size,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontOverride("font", bold ? UiFonts.TechBold : UiFonts.Tech);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private bool IsTrackHandoff => _kind is TransitionKind.Gameplay or TransitionKind.Restart;

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
        DrawRect(rect, new Color("070b16"));

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
            points =
            [
                new(edge - EdgeWidth, 0), new(edge, 0),
                new(edge, UiLayout.DesignHeight - EdgeCut),
                new(edge - EdgeCut, UiLayout.DesignHeight),
                new(edge - EdgeWidth, UiLayout.DesignHeight),
            ];
        }
        else
        {
            edge = UiLayout.DesignWidth - edge;
            points =
            [
                new(edge + EdgeWidth, 0), new(edge, 0), new(edge, EdgeCut),
                new(edge + EdgeCut, UiLayout.DesignHeight),
                new(edge + EdgeWidth, UiLayout.DesignHeight),
            ];
        }
        DrawColoredPolygon(points, new Color("101a2f"));
    }

    private void DrawScan(float direction)
    {
        float x;
        if (_coverage >= 0.999f && _profile.AllowLoop)
            x = Mathf.Lerp(100f, UiLayout.DesignWidth - 100f, _waitingScan);
        else
            x = direction > 0f
                ? UiLayout.DesignWidth * _coverage
                : UiLayout.DesignWidth * (1f - _coverage);

        DrawRect(new Rect2(x - ScanWidth * 2f, 0, ScanWidth * 4f, UiLayout.DesignHeight),
            new Color(UiFonts.Cyan, 0.08f));
        DrawRect(new Rect2(x - ScanWidth * 0.5f, 0, ScanWidth, UiLayout.DesignHeight),
            new Color(UiFonts.Cyan, 0.88f));
        DrawLine(new Vector2(x - 26f, 0), new Vector2(x + 8f, 0), Colors.White, 3f);
        DrawLine(new Vector2(x - 8f, UiLayout.DesignHeight),
            new Vector2(x + 26f, UiLayout.DesignHeight), Colors.White, 3f);
    }

    private void DrawSignalMarks(float direction)
    {
        if (_coverage < 0.82f)
            return;
        var accentX = direction > 0f ? 112f : UiLayout.DesignWidth - 404f;
        DrawRect(new Rect2(accentX, 842, 288, 3), UiFonts.Cyan);
        DrawRect(new Rect2(accentX, 976, 124, 2), new Color(UiFonts.Cyan, 0.48f));
        for (var i = 0; i < 3; i++)
            DrawRect(new Rect2(accentX + 142 + i * 24, 972, 12, 6),
                new Color(UiFonts.Cyan, 0.38f + i * 0.18f));
    }

    private void DrawRelaySignalField()
    {
        var opacity = Mathf.Clamp((_coverage - 0.82f) / 0.18f, 0f, 1f);
        var line = new Color(UiFonts.Cyan, 0.22f * opacity);
        var core = new Vector2(1600, 650);
        DrawLine(new Vector2(760, 540), core, line, 2f);
        DrawLine(new Vector2(760, 585), core, line, 2f);
        DrawLine(new Vector2(760, 630), core, line, 2f);
        DrawCircle(core, 18f, new Color(UiFonts.Cyan, 0.12f * opacity));
        DrawArc(core, 32f, 0f, Mathf.Tau, 48, new Color(UiFonts.Cyan, 0.72f * opacity), 2f);
        DrawLine(new Vector2(1510, 650), new Vector2(1730, 650),
            new Color(UiFonts.Pink, 0.36f * opacity), 2f);
    }

    private void UpdateContentVisibility()
    {
        if (_label is null || _relayContext is null)
            return;

        var show = _coverage >= 0.82f;
        _label.Visible = show && !IsTrackHandoff;
        _detail.Visible = show && !IsTrackHandoff;
        _relayContext.Visible = show && IsTrackHandoff;
        if (_relayContext.Visible)
        {
            var contentAlpha = _profile.AllowDirectionalMotion
                ? Mathf.Clamp((_coverage - 0.82f) / 0.18f, 0f, 1f)
                : _coverage;
            _relayContext.Modulate = new Color(1f, 1f, 1f, contentAlpha);
        }
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
