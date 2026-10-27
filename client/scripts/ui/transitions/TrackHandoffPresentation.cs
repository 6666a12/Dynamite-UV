using Godot;

namespace DynamiteUniverse.Ui;

internal partial class TrackHandoffPresentation : Control
{
    private const float SkyCx = 960f;
    private const float SkyCy = 540f;
    private const float CoverX = 720f;
    private const float CoverY = 300f;
    private const float CoverSize = 480f;
    private const float CoverPad = 20f;
    private const float CableGap = 6f;
    private const float NodeHalf = 6f;
    private const float CableChargeOpacity = 0.25f;
    private const float CableAfterglowOpacity = 0.12f;
    private const float NodeDimOpacity = 0.4f;
    private const float NodeLitOpacity = 1f;
    private const int BandTextureWidth = 128;
    private const int BandTextureHeight = 16;
    public const float DischargeSeconds = 0.50f;
    private const float DischargeFlashPeak = 0.12f;
    private const float ShockwaveInnerStart = 0f;
    private const float ShockwaveOuterStart = 0.080f;
    private const float ShockwaveSpanSeconds = 0.400f;
    private const float NodeBurstGrowFraction = 0.40f;
    private const float CableAfterglowFallFraction = 0.70f;
    private const float ChargeCycleSeconds = 1.8f;
    private const float CableBandLength = 150f;
    private const float CableBandWidth = 9f;
    private const float CableBreathMin = 0.20f;
    private const float CableBreathMax = 0.30f;
    private const float CableTravelFraction = 0.75f;
    private const float NodeFlashSeconds = 0.15f;
    private const float NodeFlashOffset = 0.5f;

    private static readonly Vector2[] CableFrom =
        [new(0, 0), new(1920, 0), new(0, 1080), new(1920, 1080)];
    private static readonly Vector2[] CableTo =
        [new(720, 300), new(1200, 300), new(720, 780), new(1200, 780)];
    private static readonly Vector2[] CornerPads =
        [new(0, 0), new(1904, 0), new(0, 1064), new(1904, 1064)];
    private static readonly float[] CablePhaseOffsets = [0.30f, 0.38f, 0.26f, 0.34f];
    private static readonly float[] NodeRays = [30f, 20f, 26f, 18f, 29f, 21f, 25f, 19f];
    private static readonly Color FieldColor = new("070b16");
    private static readonly Color CoverFill = new("0b1228");
    private static readonly Color CoverTint = new(0.02f, 0.04f, 0.10f, 0.18f);

    private static Texture2D? _washTexture;
    private static Texture2D? _flashTexture;
    private static Texture2D? _streakTexture;

    private readonly TrackHandoffState _state = new();
    private UiMotionProfile _profile = UiMotionProfile.For(UiMotionMode.Full);
    private TransitionKind _kind;
    private float _coverage;
    private Control _relayContext = null!;
    private TextureRect _cover = null!;
    private CutPanel _coverPanel = null!;
    private ColorRect _headingPlate = null!;
    private HandoffDrawLayer _frontLayer = null!;
    private HandoffDrawLayer _fxLayer = null!;
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
            if (IsInstanceValid(_frontLayer))
                _frontLayer.QueueRedraw();
            if (IsInstanceValid(_fxLayer))
                _fxLayer.QueueRedraw();
        }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        BuildRelayContext();
    }

    public void Configure(TransitionKind kind, string? label, string? detail,
        UiMotionProfile profile, TrackRelayPresentation? relay)
    {
        _kind = kind;
        _profile = profile;
        _state.Reset();
        if (IsTrackHandoff)
        {
            var restart = kind == TransitionKind.Restart;
            _relayHeading.Text = restart ? "TRACK HANDOFF · RE-SYNC" : "TRACK HANDOFF";
            _headingPlate.Size = new Vector2(restart ? 380f : 250f, 50f);
            _relayStatus.Text = "PLAYFIELD / LOCKING";
            _relayStatus.AddThemeColorOverride("font_color", UiFonts.Dim);
            _relayTitle.Text = relay?.Title ?? (string.IsNullOrWhiteSpace(label) ? DefaultLabel(kind) : label);
            _relayDifficulty.Text = relay?.Difficulty ?? (string.IsNullOrWhiteSpace(detail) ? DefaultDetail(kind) : detail);
            _cover.Texture = relay?.Cover;
        }
        else
        {
            _cover.Texture = null;
        }

        ApplyHandoffPalette();
        UpdateContentVisibility();
        RefreshLayers();
    }

    public void MarkReady()
    {
        if (!IsTrackHandoff)
            return;
        _state.MarkReady(_profile.AllowDirectionalMotion);
        _relayStatus.Text = "SYNC ESTABLISHED · READY";
        _relayStatus.AddThemeColorOverride("font_color", UiFonts.Cyan);
        ApplyHandoffPalette();
        RefreshLayers();
    }

    public void MarkFailed()
    {
        if (!IsTrackHandoff)
            return;
        _relayStatus.Text = "PLAYFIELD / NO READY SIGNAL";
        _relayStatus.AddThemeColorOverride("font_color", UiFonts.Pink);
        QueueRedraw();
    }

    public void BeginReveal(bool discharge)
    {
        if (!IsTrackHandoff)
            return;
        _state.BeginReveal(discharge);
        ApplyHandoffPalette();
        RefreshLayers();
    }

    public void Advance(double delta)
    {
        if (!IsTrackHandoff)
            return;
        if (!_state.Advance(delta, _profile.AllowDirectionalMotion, _profile.AllowLoop,
                DischargeSeconds, ChargeCycleSeconds))
            return;
        ApplyHandoffPalette();
        RefreshLayers();
    }

    public bool IsDischargeRunning => _state.IsDischargeRunning;

    public void Clear()
    {
        _state.Reset();
        if (IsInstanceValid(_cover))
            _cover.Texture = null;
        if (IsInstanceValid(_relayContext))
        {
            _relayContext.Visible = false;
            _relayContext.Modulate = Colors.White;
        }
        RefreshLayers();
    }

    public void RefreshLayers()
    {
        QueueRedraw();
        if (IsInstanceValid(_frontLayer))
            _frontLayer.QueueRedraw();
        if (IsInstanceValid(_fxLayer))
            _fxLayer.QueueRedraw();
    }

    public override void _Draw()
    {
        if (!IsTrackHandoff)
            return;
        if (_coverage <= 0f && !IsDischargeRunning)
            return;
        DrawHandoffField(_coverage);
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
        _coverPanel = new CutPanel
        {
            Position = new Vector2(CoverX, CoverY), Size = new Vector2(CoverSize, CoverSize),
            Cut = 22f, Fill = CoverFill, Border = new Color(UiFonts.Cyan, 0.64f),
            BorderWidth = 2f, MouseFilter = MouseFilterEnum.Ignore,
        };
        _relayContext.AddChild(_coverPanel);
        var art = CoverSize - CoverPad * 2f;
        var artPosition = new Vector2(CoverX + CoverPad, CoverY + CoverPad);
        var artSize = new Vector2(art, art);
        _relayContext.AddChild(new CoverPlaceholder
        {
            Position = artPosition, Size = artSize, MouseFilter = MouseFilterEnum.Ignore,
        });
        _cover = new TextureRect
        {
            Position = artPosition, Size = artSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _relayContext.AddChild(_cover);
        _relayContext.AddChild(new ColorRect
        {
            Position = artPosition, Size = artSize, Color = CoverTint,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        _frontLayer = new HandoffDrawLayer
        {
            Size = UiLayout.DesignSize, MouseFilter = MouseFilterEnum.Ignore,
            Content = DrawHandoffFront,
        };
        _relayContext.AddChild(_frontLayer);
        _headingPlate = new ColorRect
        {
            Position = new Vector2(96, 86), Size = new Vector2(250, 50),
            Color = FieldColor, MouseFilter = MouseFilterEnum.Ignore,
        };
        _relayContext.AddChild(_headingPlate);
        _relayHeading = MakeLabel(new Vector2(112, 94), new Vector2(920, 46), 27, UiFonts.Text, true);
        _relayContext.AddChild(_relayHeading);
        _relayContext.AddChild(new ColorRect
        {
            Position = new Vector2(1662, 92), Size = new Vector2(158, 36),
            Color = FieldColor, MouseFilter = MouseFilterEnum.Ignore,
        });
        var lockLabel = MakeLabel(new Vector2(1448, 100), new Vector2(360, 32), 16, UiFonts.Cyan, true);
        lockLabel.Text = "CONTEXT LOCKED";
        lockLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _relayContext.AddChild(lockLabel);
        var contextTag = MakeLabel(new Vector2(SkyCx - 400f, 258f - 16f), new Vector2(800, 32),
            16, UiFonts.Dim, true);
        contextTag.Text = "TRACK CONTEXT / COMMITTED";
        contextTag.HorizontalAlignment = HorizontalAlignment.Center;
        _relayContext.AddChild(contextTag);
        _relayTitle = MakeLabel(new Vector2(SkyCx - 500f, 840f - 34f), new Vector2(1000, 68),
            44, UiFonts.Text, true);
        _relayTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _relayContext.AddChild(_relayTitle);
        _relayDifficulty = MakeLabel(new Vector2(SkyCx - 400f, 900f - 18f), new Vector2(800, 36),
            22, UiFonts.Cyan, true);
        _relayDifficulty.HorizontalAlignment = HorizontalAlignment.Center;
        _relayContext.AddChild(_relayDifficulty);
        _relayStatus = MakeLabel(new Vector2(SkyCx - 400f, 1010f - 16f), new Vector2(800, 32),
            16, UiFonts.Dim, true);
        _relayStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _relayContext.AddChild(_relayStatus);
        _fxLayer = new HandoffDrawLayer
        {
            Name = "TrackHandoffDischargeFx", Size = UiLayout.DesignSize,
            MouseFilter = MouseFilterEnum.Ignore, Content = DrawHandoffDischargeFx,
        };
        AddChild(_fxLayer);
    }

    private static Label MakeLabel(Vector2 position, Vector2 size, int fontSize, Color color, bool bold = false)
    {
        var label = new Label
        {
            Position = position, Size = size, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontOverride("font", bold ? UiFonts.TechBold : UiFonts.Tech);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private bool IsTrackHandoff => _kind is TransitionKind.Gameplay or TransitionKind.Restart;

    private void DrawHandoffField(float alpha)
    {
        if (alpha > 0.001f)
        {
            DrawRect(new Rect2(Vector2.Zero, UiLayout.DesignSize), new Color(FieldColor, alpha));
            DrawTextureRect(WashTexture(), new Rect2(SkyCx - 700f, SkyCy - 540f, 1400f, 1080f),
                false, new Color(UiFonts.Cyan, 0.04f * alpha));
            DrawSignalMarks(1f, alpha);
        }
        if (_state.Phase != TrackHandoffPhase.Discharge)
            return;
        DrawRect(new Rect2(0, 860, UiLayout.DesignWidth, 2f), new Color(UiFonts.Cyan, 0.25f * _state.Discharge));
        DrawShockwave();
    }

    private void DrawShockwave()
    {
        var center = new Vector2(SkyCx, SkyCy);
        var elapsed = _state.Discharge * DischargeSeconds;
        var inner = Mathf.Clamp((elapsed - ShockwaveInnerStart) / ShockwaveSpanSeconds, 0f, 1f);
        var outer = Mathf.Clamp((elapsed - ShockwaveOuterStart) / ShockwaveSpanSeconds, 0f, 1f);
        if (inner > 0f && inner < 1f)
            DrawArc(center, Mathf.Lerp(320f, 420f, UiEase.Standard(inner)), 0f, Mathf.Tau, 96,
                new Color(UiFonts.Cyan, 0.20f * (1f - inner)), 2f, true);
        if (outer > 0f && outer < 1f)
            DrawArc(center, Mathf.Lerp(420f, 520f, UiEase.Standard(outer)), 0f, Mathf.Tau, 96,
                new Color(UiFonts.Cyan, 0.08f * (1f - outer)), 1.5f, true);
    }

    private void DrawHandoffFront()
    {
        if (!IsInstanceValid(_frontLayer))
            return;
        if (_state.Phase == TrackHandoffPhase.Lock && _state.LockAmount > 0.001f)
            _frontLayer.DrawTextureRect(WashTexture(), new Rect2(CoverX, CoverY, CoverSize, CoverSize),
                false, new Color(UiFonts.Cyan, 0.08f * _state.LockAmount));
        var cable = CableOpacity();
        for (var i = 0; i < CableFrom.Length; i++)
            DrawCable(i, cable);
        foreach (var pad in CornerPads)
            _frontLayer.DrawRect(new Rect2(pad.X, pad.Y, 16f, 16f), new Color(UiFonts.Cyan, cable), false, 2f);
        for (var i = 0; i < CableTo.Length; i++)
            DrawNode(CableTo[i], NodeFlashAmount(i));
    }

    private void DrawHandoffDischargeFx()
    {
        if (!IsInstanceValid(_fxLayer) || _state.Phase != TrackHandoffPhase.Discharge)
            return;
        var flash = DischargeFlashAmount();
        if (flash > 0.001f)
            _fxLayer.DrawTextureRect(FlashTexture(), new Rect2(CoverX, CoverY, CoverSize, CoverSize),
                false, new Color(1f, 1f, 1f, flash));
        var fade = Mathf.Pow(1f - _state.Discharge, 1.2f);
        if (fade <= 0.001f)
            return;
        foreach (var node in CableTo)
        for (var i = 0; i < NodeRays.Length; i++)
        {
            var angle = i * Mathf.Pi / 4f;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var grow = Mathf.Clamp(_state.Discharge / NodeBurstGrowFraction, 0f, 1f);
            var length = NodeRays[i] * (0.4f + 0.6f * grow);
            _fxLayer.DrawLine(node, node + direction * (length * 0.5f), new Color("eafdff", 0.95f * fade), 2f, true);
            _fxLayer.DrawLine(node + direction * (length * 0.5f), node + direction * length,
                new Color("7eeaff", 0.5f * fade), 2f, true);
        }
    }

    private void DrawCable(int index, float opacity)
    {
        var from = CableFrom[index];
        var to = CableTo[index];
        var delta = to - from;
        var length = delta.Length();
        var unit = delta / length;
        var normal = new Vector2(-unit.Y, unit.X) * (CableGap * 0.5f);
        var tone = opacity;
        if (_state.Phase == TrackHandoffPhase.Charge && _profile.AllowLoop)
        {
            var breath = 0.5f + 0.5f * Mathf.Sin(Mathf.Tau * Mathf.PosMod(
                _state.ChargePhase + CablePhaseOffsets[index] + NodeFlashOffset, 1f));
            tone = Mathf.Lerp(CableBreathMin, CableBreathMax, breath);
        }
        var color = new Color(UiFonts.Cyan, tone);
        _frontLayer.DrawLine(from + normal, to + normal, color, 2f, true);
        _frontLayer.DrawLine(from - normal, to - normal, color, 2f, true);
        if (_state.Phase != TrackHandoffPhase.Charge || !_profile.AllowLoop)
            return;
        var head = CableBandHead(index);
        var headPoint = from + delta * head;
        var tailPoint = headPoint - unit * CableBandLength;
        DrawCableBand(tailPoint, headPoint, Mathf.Sin(Mathf.Pi * Mathf.Clamp(head, 0f, 1f)));
    }

    private float CableBandHead(int index)
    {
        var cycle = Mathf.PosMod(_state.ChargePhase + CablePhaseOffsets[index], 1f);
        if (cycle >= CableTravelFraction)
            return 1f;
        return UiEase.Standard(cycle / CableTravelFraction);
    }

    private float NodeFlashAmount(int index)
    {
        if (_state.Phase == TrackHandoffPhase.Lock)
            return _state.LockAmount;
        if (_state.Phase != TrackHandoffPhase.Charge || !_profile.AllowLoop)
            return 0f;
        var cycle = Mathf.PosMod(_state.ChargePhase + CablePhaseOffsets[index], 1f);
        if (cycle < CableTravelFraction)
            return 0f;
        var progress = (cycle - CableTravelFraction) / (NodeFlashSeconds / ChargeCycleSeconds);
        if (progress >= 1f)
            return 0f;
        return Mathf.Sin(Mathf.Pi * Mathf.Clamp(progress, 0f, 1f));
    }

    private void DrawCableBand(Vector2 tailPoint, Vector2 headPoint, float alpha)
    {
        if (alpha <= 0.02f)
            return;
        var delta = headPoint - tailPoint;
        var length = delta.Length();
        if (length <= 1f)
            return;
        _frontLayer.DrawSetTransform(tailPoint, Mathf.Atan2(delta.Y, delta.X),
            new Vector2(length / BandTextureWidth, CableBandWidth / BandTextureHeight));
        _frontLayer.DrawTextureRect(StreakTexture(), new Rect2(0f, 0f, BandTextureWidth, BandTextureHeight),
            false, new Color(UiFonts.Cyan, 0.85f * alpha));
        _frontLayer.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawNode(Vector2 node, float flash)
    {
        if (_state.Phase == TrackHandoffPhase.Lock && _state.LockAmount > 0.001f)
        {
            _frontLayer.DrawTextureRect(WashTexture(), new Rect2(node.X - 14f, node.Y - 14f, 28f, 28f),
                false, new Color(UiFonts.Cyan, 0.9f * _state.LockAmount));
            _frontLayer.DrawTextureRect(WashTexture(), new Rect2(node.X - 9f, node.Y - 9f, 18f, 18f),
                false, new Color(1f, 1f, 1f, 0.85f * _state.LockAmount));
        }
        else if (flash > 0.001f)
        {
            _frontLayer.DrawTextureRect(WashTexture(), new Rect2(node.X - 16f, node.Y - 16f, 32f, 32f),
                false, new Color(UiFonts.Cyan, 0.55f * flash));
            _frontLayer.DrawTextureRect(WashTexture(), new Rect2(node.X - 8f, node.Y - 8f, 16f, 16f),
                false, new Color(1f, 1f, 1f, 0.5f * flash));
        }
        Vector2[] diamond =
        [new(node.X, node.Y - NodeHalf), new(node.X + NodeHalf, node.Y),
         new(node.X, node.Y + NodeHalf), new(node.X - NodeHalf, node.Y)];
        var stroke = _state.Phase switch
        {
            TrackHandoffPhase.Discharge => NodeDimOpacity,
            TrackHandoffPhase.Lock => NodeLitOpacity,
            _ => Mathf.Lerp(NodeDimOpacity, 0.95f, flash),
        };
        _frontLayer.DrawColoredPolygon(diamond, CoverFill);
        _frontLayer.DrawPolyline(UiGeometry.Close(diamond), new Color(UiFonts.Cyan, stroke), 2f, true);
    }

    private float CableOpacity()
    {
        if (_state.Phase == TrackHandoffPhase.Discharge)
        {
            var fall = UiEase.Standard(Mathf.Clamp(_state.Discharge / CableAfterglowFallFraction, 0f, 1f));
            return Mathf.Lerp(1f, CableAfterglowOpacity, fall);
        }
        return Mathf.Lerp(CableChargeOpacity, 1f, _state.LockAmount);
    }

    private float DischargeFlashAmount()
    {
        if (_state.Discharge <= 0f)
            return 0f;
        return Mathf.Pow(1f - _state.Discharge, 1.4f) *
               Mathf.Clamp(_state.Discharge / DischargeFlashPeak, 0f, 1f);
    }

    private void ApplyHandoffPalette()
    {
        if (!IsInstanceValid(_coverPanel))
            return;
        _coverPanel.Border = _state.Phase == TrackHandoffPhase.Discharge
            ? new Color(UiFonts.Cyan, 1f).Lerp(Colors.White, Mathf.Clamp(_state.Discharge * 2f, 0f, 1f))
            : new Color(UiFonts.Cyan, Mathf.Lerp(0.64f, 1f, _state.LockAmount));
    }

    private static Texture2D WashTexture() => _washTexture ??= BuildWashTexture();
    private static Texture2D FlashTexture() => _flashTexture ??= BuildFlashTexture();
    private static Texture2D StreakTexture() => _streakTexture ??= BuildStreakTexture();

    private static Texture2D BuildStreakTexture()
    {
        var image = Image.CreateEmpty(BandTextureWidth, BandTextureHeight, false, Image.Format.Rgba8);
        for (var x = 0; x < BandTextureWidth; x++)
        {
            var along = (x + 0.5f) / BandTextureWidth;
            var head = Mathf.Pow(along, 2.2f);
            for (var y = 0; y < BandTextureHeight; y++)
            {
                var across = Mathf.Abs((y + 0.5f) / BandTextureHeight * 2f - 1f);
                var profile = Mathf.Pow(1f - across, 0.7f) * 0.55f + Mathf.Pow(1f - across, 6f) * 0.45f;
                image.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp(head * profile, 0f, 1f)));
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D BuildWashTexture()
    {
        const int size = 128;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        {
            var dy = (y + 0.5f) / size * 2f - 1f;
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size * 2f - 1f;
                var radius = Mathf.Min(1f, Mathf.Sqrt(dx * dx + dy * dy));
                image.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(1f - radius, 1.6f)));
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D BuildFlashTexture()
    {
        (float Offset, Color Color)[] stops =
        [
            (0f, new Color(1f, 1f, 1f, 0.60f)),
            (0.45f, new Color("bff2ff") with { A = 0.42f }),
            (0.75f, new Color(UiFonts.Cyan, 0.18f)),
            (1f, new Color(UiFonts.Cyan, 0f)),
        ];
        const int size = 128;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        {
            var dy = (y + 0.5f) / size * 2f - 1f;
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size * 2f - 1f;
                var radius = Mathf.Min(1f, Mathf.Sqrt(dx * dx + dy * dy));
                image.SetPixel(x, y, SampleStops(stops, radius));
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static Color SampleStops((float Offset, Color Color)[] stops, float position)
    {
        for (var i = 1; i < stops.Length; i++)
        {
            if (position > stops[i].Offset)
                continue;
            var span = Mathf.Max(1e-5f, stops[i].Offset - stops[i - 1].Offset);
            var t = Mathf.Clamp((position - stops[i - 1].Offset) / span, 0f, 1f);
            return stops[i - 1].Color.Lerp(stops[i].Color, t);
        }
        return stops[^1].Color;
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
        if (_relayContext is null)
            return;
        _relayContext.Visible = IsTrackHandoff && _coverage > 0.001f;
        _relayContext.Modulate = new Color(1f, 1f, 1f, _coverage);
    }

    private static string DefaultLabel(TransitionKind kind) => kind == TransitionKind.Restart
        ? "SIGNAL RELOCK" : "TRACK RELAY";
    private static string DefaultDetail(TransitionKind kind) => kind == TransitionKind.Restart
        ? "REARMING CURRENT TRACK" : "SYNCING PLAYFIELD";

    private partial class HandoffDrawLayer : Control
    {
        public Action? Content;
        public override void _Draw() => Content?.Invoke();
    }
}
