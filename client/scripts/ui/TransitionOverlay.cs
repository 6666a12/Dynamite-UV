using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>
/// Fixed-design opaque Signal Lock and the Track Handoff presentation.
/// Signal Lock（分片、切边、等待扫描、左下信号标记）保持原样；Track Handoff 换成新设计稿的
/// 中央封面 + 四角双股电缆，并按 充电 → 锁定 → 放电 三相推进。
/// 几何常量逐条取自 .tmp/penpot_build5.py 的 handoff_*_board 常量块。
/// </summary>
public partial class TransitionOverlay : Control
{
    // ---- Signal Lock（保持不变） ----
    private const float EdgeWidth = 150f;
    private const float EdgeCut = 34f;
    private const float ScanWidth = 9f;

    // ---- Track Handoff 设计稿几何（.tmp/penpot_build5.py） ----
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
    /// <summary>锁定后电缆 25% → 100% 的爬升时长（Full；Reduced/Off 瞬时）。</summary>
    private const float LockRampSeconds = 0.09f;
    /// <summary>放电总时长；与揭示（GameplayRevealDuration）重叠播放，差额是尾巴。</summary>
    public const float DischargeSeconds = 0.50f;
    /// <summary>放电白热闪光的峰值位置（占放电时长的比例），之后单调衰减。</summary>
    private const float DischargeFlashPeak = 0.12f;
    /// <summary>冲击波两环：内环 t=0 起跳，外环延迟 80ms；各自 400ms 外扩渐隐。</summary>
    private const float ShockwaveInnerStart = 0f;
    private const float ShockwaveOuterStart = 0.080f;
    private const float ShockwaveSpanSeconds = 0.400f;
    /// <summary>四角爆闪射线长满所需时间（占放电时长）。</summary>
    private const float NodeBurstGrowFraction = 0.40f;
    /// <summary>电缆余晖从满亮落到 12% 的时间（占放电时长）。</summary>
    private const float CableAfterglowFallFraction = 0.70f;

    // 充电：电流向中心流动（彗星状亮带 + 电缆呼吸 + 节点吸收闪光）。
    private const float ChargeCycleSeconds = 1.8f;
    private const float CableBandLength = 150f;
    private const float CableBandWidth = 9f;
    private const float CableBreathMin = 0.20f;
    private const float CableBreathMax = 0.30f;
    private const float CableTravelFraction = 0.75f;   // 周期前 75% 用于行进，其余留给吸收闪光
    private const float NodeFlashSeconds = 0.15f;
    private const float NodeFlashOffset = 0.5f;        // 呼吸与亮带反相

    private static readonly Vector2[] CableFrom =
        [new(0, 0), new(1920, 0), new(0, 1080), new(1920, 1080)];
    private static readonly Vector2[] CableTo =
        [new(720, 300), new(1200, 300), new(720, 780), new(1200, 780)];
    private static readonly Vector2[] CornerPads =
        [new(0, 0), new(1904, 0), new(0, 1064), new(1904, 1064)];
    /// <summary>四条电缆的错相（沿用设计稿 PULSE_TS 的错相）。</summary>
    private static readonly float[] CablePhase = [0.30f, 0.38f, 0.26f, 0.34f];
    private static readonly float[] NodeRays = [30f, 20f, 26f, 18f, 29f, 21f, 25f, 19f];

    private static readonly Color FieldColor = new("070b16");
    private static readonly Color CoverFill = new("0b1228");
    private static readonly Color CoverTint = new(0.02f, 0.04f, 0.10f, 0.18f);

    private enum HandoffPhase
    {
        Charge,
        Lock,
        Discharge,
    }

    private static Texture2D? _washTexture;
    private static Texture2D? _flashTexture;
    private static Texture2D? _streakTexture;

    private HandoffPhase _handoffPhase = HandoffPhase.Charge;
    private float _lockAmount;
    private float _discharge;
    private float _chargePhase;

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
    private CutPanel _coverPanel = null!;
    private ColorRect _headingPlate = null!;
    private HandoffDrawLayer _frontLayer = null!;
    private HandoffDrawLayer _fxLayer = null!;
    private Label _relayHeading = null!;
    private Label _lockLabel = null!;
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
        _handoffPhase = HandoffPhase.Charge;
        _lockAmount = 0f;
        _discharge = 0f;
        _chargePhase = 0f;
        _label.Text = string.IsNullOrWhiteSpace(label) ? DefaultLabel(kind) : label;
        _detail.Text = string.IsNullOrWhiteSpace(detail) ? DefaultDetail(kind) : detail;

        if (IsTrackHandoff)
        {
            var restart = kind == TransitionKind.Restart;
            _relayHeading.Text = restart ? "TRACK HANDOFF · RE-SYNC" : "TRACK HANDOFF";
            _headingPlate.Size = new Vector2(restart ? 380f : 250f, 50f);
            _relayStatus.AddThemeColorOverride("font_color", UiFonts.Dim);
            _relayStatus.Text = "PLAYFIELD / LOCKING";
            _relayTitle.Text = relay?.Title ?? _label.Text;
            _relayDifficulty.Text = relay?.Difficulty ?? _detail.Text;
            _cover.Texture = relay?.Cover;
            _lockLabel.Text = "CONTEXT LOCKED";
        }
        else
        {
            _cover.Texture = null;
        }

        ApplyHandoffPalette();
        UpdateContentVisibility();
        RefreshHandoffLayers();
    }

    /// <summary>READY 到达：进入锁定相位（电缆拉满、节点点亮、状态转 cyan）。</summary>
    public void MarkReady()
    {
        if (!IsTrackHandoff)
            return;
        _handoffPhase = HandoffPhase.Lock;
        // Full 走 90ms 爬升，Reduced/Off 直换（锁定=状态直换无延迟）。
        _lockAmount = _profile.AllowDirectionalMotion ? 0f : 1f;
        _relayStatus.Text = "SYNC ESTABLISHED · READY";
        _relayStatus.AddThemeColorOverride("font_color", UiFonts.Cyan);
        ApplyHandoffPalette();
        RefreshHandoffLayers();
    }

    public void MarkFailed()
    {
        if (!IsTrackHandoff)
            return;
        _relayStatus.Text = "PLAYFIELD / NO READY SIGNAL";
        _relayStatus.AddThemeColorOverride("font_color", UiFonts.Pink);
        QueueRedraw();
    }

    /// <summary>
    /// 揭示开始：handoff 页整页按 coverage 淡出；<paramref name="discharge"/> 时放电与
    /// Gameplay 分层揭示重叠播放（Reduced/Off 无放电）。
    /// </summary>
    public void BeginHandoffReveal(bool discharge)
    {
        if (!IsTrackHandoff)
            return;
        if (discharge && _handoffPhase != HandoffPhase.Discharge)
        {
            _handoffPhase = HandoffPhase.Discharge;
            _discharge = 0f;
        }
        ApplyHandoffPalette();
        RefreshHandoffLayers();
    }

    /// <summary>推进锁定爬升、放电进度和充电脉冲相位（由 TransitionDirector 每帧驱动）。</summary>
    public void AdvanceHandoff(double delta)
    {
        if (!IsTrackHandoff)
            return;

        var changed = false;
        if (_handoffPhase == HandoffPhase.Lock && _lockAmount < 1f)
        {
            _lockAmount = _profile.AllowDirectionalMotion
                ? Mathf.Min(1f, _lockAmount + (float)(delta / LockRampSeconds))
                : 1f;
            ApplyHandoffPalette();
            changed = true;
        }
        else if (_handoffPhase == HandoffPhase.Discharge)
        {
            _discharge = Mathf.Min(1f, _discharge + (float)(delta / DischargeSeconds));
            ApplyHandoffPalette();
            changed = true;
        }
        else if (_handoffPhase == HandoffPhase.Charge)
        {
            // 电流流动同 1.8s 周期；Reduced/Off 不流动（AllowLoop=false）→ 静态 25%。
            if (_profile.AllowLoop)
            {
                _chargePhase = Mathf.PosMod(_chargePhase + (float)(delta / ChargeCycleSeconds), 1f);
                changed = true;
            }
        }

        if (!changed)
            return;
        RefreshHandoffLayers();
    }

    /// <summary>handoff 页可见期间：页面层与放电特效层都要重绘。</summary>
    public void RefreshHandoffLayers()
    {
        QueueRedraw();
        if (IsInstanceValid(_frontLayer))
            _frontLayer.QueueRedraw();
        if (IsInstanceValid(_fxLayer))
            _fxLayer.QueueRedraw();
    }

    /// <summary>放电是否还在收尾（此时 overlay 即便已完全透明也要继续播放）。</summary>
    public bool IsDischargeRunning =>
        _handoffPhase == HandoffPhase.Discharge && _discharge < 1f;

    public void ClearPresentation()
    {
        _relay = null;
        _handoffPhase = HandoffPhase.Charge;
        _lockAmount = 0f;
        _discharge = 0f;
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
        if (IsTrackHandoff)
        {
            // 放电尾巴阶段 overlay 已完全透明，但冲击波/判定线残影仍要画完。
            if (_coverage <= 0f && !IsDischargeRunning)
                return;
            DrawHandoffField(_coverage);
            return;
        }

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
        DrawSignalMarks(direction, 1f);
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

        _coverPanel = new CutPanel
        {
            Position = new Vector2(CoverX, CoverY),
            Size = new Vector2(CoverSize, CoverSize),
            Cut = 22f,
            Fill = CoverFill,
            Border = new Color(UiFonts.Cyan, 0.64f),
            BorderWidth = 2f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _relayContext.AddChild(_coverPanel);

        var art = CoverSize - CoverPad * 2f;
        var artPosition = new Vector2(CoverX + CoverPad, CoverY + CoverPad);
        var artSize = new Vector2(art, art);
        _coverFallback = new CoverPlaceholder
        {
            Position = artPosition,
            Size = artSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _relayContext.AddChild(_coverFallback);
        _cover = new TextureRect
        {
            Position = artPosition,
            Size = artSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _relayContext.AddChild(_cover);
        _relayContext.AddChild(new ColorRect
        {
            Position = artPosition,
            Size = artSize,
            Color = CoverTint,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        // 电缆与节点压在封面图之上（设计稿层级：闪光 → 电缆 → 节点），随页面一起淡出。
        _frontLayer = new HandoffDrawLayer
        {
            Size = UiLayout.DesignSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _frontLayer.Content = DrawHandoffFront;
        _relayContext.AddChild(_frontLayer);

        // 标题下垫 #070B16 底板，避免电缆穿过文字。
        _headingPlate = new ColorRect
        {
            Position = new Vector2(96, 86),
            Size = new Vector2(250, 50),
            Color = FieldColor,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _relayContext.AddChild(_headingPlate);
        _relayHeading = MakeLabel(new Vector2(112, 94), new Vector2(920, 46), 27,
            UiFonts.Text, bold: true);
        _relayContext.AddChild(_relayHeading);

        _relayContext.AddChild(new ColorRect
        {
            Position = new Vector2(1662, 92),
            Size = new Vector2(158, 36),
            Color = FieldColor,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        _lockLabel = MakeLabel(new Vector2(1448, 100), new Vector2(360, 32), 16,
            UiFonts.Cyan, bold: true);
        _lockLabel.Text = "CONTEXT LOCKED";
        _lockLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _relayContext.AddChild(_lockLabel);

        var contextTag = MakeLabel(new Vector2(SkyCx - 400f, 258f - 16f),
            new Vector2(800, 32), 16, UiFonts.Dim, bold: true);
        contextTag.Text = "TRACK CONTEXT / COMMITTED";
        contextTag.HorizontalAlignment = HorizontalAlignment.Center;
        _relayContext.AddChild(contextTag);

        _relayTitle = MakeLabel(new Vector2(SkyCx - 500f, 840f - 34f),
            new Vector2(1000, 68), 44, UiFonts.Text, bold: true);
        _relayTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _relayContext.AddChild(_relayTitle);

        _relayDifficulty = MakeLabel(new Vector2(SkyCx - 400f, 900f - 18f),
            new Vector2(800, 36), 22, UiFonts.Cyan, bold: true);
        _relayDifficulty.HorizontalAlignment = HorizontalAlignment.Center;
        _relayContext.AddChild(_relayDifficulty);

        _relayStatus = MakeLabel(new Vector2(SkyCx - 400f, 1010f - 16f),
            new Vector2(800, 32), 16, UiFonts.Dim, bold: true);
        _relayStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _relayContext.AddChild(_relayStatus);

        // 放电特效层：不跟随页面 alpha，让爆炸尾巴能在揭示结束后自然收尾。
        _fxLayer = new HandoffDrawLayer
        {
            Name = "TrackHandoffDischargeFx",
            Size = UiLayout.DesignSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _fxLayer.Content = DrawHandoffDischargeFx;
        AddChild(_fxLayer);
    }

    private static Label MakeLabel(Vector2 position, Vector2 size, int fontSize, Color color,
        bool bold = false)
    {
        var label = new Label
        {
            Position = position,
            Size = size,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontOverride("font", bold ? UiFonts.TechBold : UiFonts.Tech);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private bool IsTrackHandoff => _kind is TransitionKind.Gameplay or TransitionKind.Restart;

    // ---- Signal Lock 绘制（保持不变） ----

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

    // ---- Track Handoff 绘制（新设计稿） ----

    /// <summary>覆盖层自身绘制：场地、中心辉光、放电冲击波/判定线残影和左下信号标记。</summary>
    private void DrawHandoffField(float alpha)
    {
        if (alpha > 0.001f)
        {
            DrawRect(new Rect2(Vector2.Zero, UiLayout.DesignSize), new Color(FieldColor, alpha));
            DrawTextureRect(WashTexture(),
                new Rect2(SkyCx - 700f, SkyCy - 540f, 1400f, 1080f), false,
                new Color(UiFonts.Cyan, 0.04f * alpha));
            DrawSignalMarks(1f, alpha);
        }

        if (_handoffPhase != HandoffPhase.Discharge)
            return;

        // 设计稿把判定线残影与冲击波放在封面之下；它们是放电特效，不随页面 alpha 消失。
        DrawRect(new Rect2(0, 860, UiLayout.DesignWidth, 2f),
            new Color(UiFonts.Cyan, 0.25f * _discharge));
        DrawShockwave();
    }

    /// <summary>两环先后起跳：内环 t=0，外环 t=80ms，各自 400ms 外扩渐隐。</summary>
    private void DrawShockwave()
    {
        var center = new Vector2(SkyCx, SkyCy);
        var elapsed = _discharge * DischargeSeconds;
        var inner = Mathf.Clamp(
            (elapsed - ShockwaveInnerStart) / ShockwaveSpanSeconds, 0f, 1f);
        var outer = Mathf.Clamp(
            (elapsed - ShockwaveOuterStart) / ShockwaveSpanSeconds, 0f, 1f);
        if (inner > 0f && inner < 1f)
        {
            DrawArc(center, Mathf.Lerp(320f, 420f, UiEase.Standard(inner)), 0f, Mathf.Tau, 96,
                new Color(UiFonts.Cyan, 0.20f * (1f - inner)), 2f, true);
        }
        if (outer > 0f && outer < 1f)
        {
            DrawArc(center, Mathf.Lerp(420f, 520f, UiEase.Standard(outer)), 0f, Mathf.Tau, 96,
                new Color(UiFonts.Cyan, 0.08f * (1f - outer)), 1.5f, true);
        }
    }

    /// <summary>封面之上的页面层：电缆 → 角端子 → 充电节点（随页面 alpha 一起淡出）。</summary>
    private void DrawHandoffFront()
    {
        if (!IsInstanceValid(_frontLayer))
            return;

        if (_handoffPhase == HandoffPhase.Lock && _lockAmount > 0.001f)
        {
            // 封面内侧 8% 辉光。
            _frontLayer.DrawTextureRect(WashTexture(),
                new Rect2(CoverX, CoverY, CoverSize, CoverSize), false,
                new Color(UiFonts.Cyan, 0.08f * _lockAmount));
        }

        var cable = CableOpacity();
        for (var i = 0; i < CableFrom.Length; i++)
            DrawCable(i, cable);

        foreach (var pad in CornerPads)
            _frontLayer.DrawRect(new Rect2(pad.X, pad.Y, 16f, 16f),
                new Color(UiFonts.Cyan, cable), false, 2f);

        for (var i = 0; i < CableTo.Length; i++)
            DrawNode(CableTo[i], NodeFlashAmount(i));
    }

    /// <summary>放电特效层：白热闪光与四角爆闪；不乘页面 alpha，尾巴才能在揭示后收完。</summary>
    private void DrawHandoffDischargeFx()
    {
        if (!IsInstanceValid(_fxLayer) || _handoffPhase != HandoffPhase.Discharge)
            return;

        var flash = DischargeFlashAmount();
        if (flash > 0.001f)
            _fxLayer.DrawTextureRect(FlashTexture(),
                new Rect2(CoverX, CoverY, CoverSize, CoverSize), false,
                new Color(1f, 1f, 1f, flash));

        var fade = Mathf.Pow(1f - _discharge, 1.2f);
        if (fade <= 0.001f)
            return;
        foreach (var node in CableTo)
        {
            for (var i = 0; i < NodeRays.Length; i++)
            {
                var angle = i * Mathf.Pi / 4f;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var grow = Mathf.Clamp(_discharge / NodeBurstGrowFraction, 0f, 1f);
                var length = NodeRays[i] * (0.4f + 0.6f * grow);
                _fxLayer.DrawLine(node, node + direction * (length * 0.5f),
                    new Color("eafdff", 0.95f * fade), 2f, true);
                _fxLayer.DrawLine(node + direction * (length * 0.5f), node + direction * length,
                    new Color("7eeaff", 0.5f * fade), 2f, true);
            }
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

        // 底流呼吸：与亮带同周期、反相（Reduced/Off 不流动，恒定 25%）。
        var tone = opacity;
        if (_handoffPhase == HandoffPhase.Charge && _profile.AllowLoop)
        {
            var breath = 0.5f + 0.5f * Mathf.Sin(
                Mathf.Tau * Mathf.PosMod(_chargePhase + CablePhase[index] + NodeFlashOffset, 1f));
            tone = Mathf.Lerp(CableBreathMin, CableBreathMax, breath);
        }

        var color = new Color(UiFonts.Cyan, tone);
        _frontLayer.DrawLine(from + normal, to + normal, color, 2f, true);
        _frontLayer.DrawLine(from - normal, to - normal, color, 2f, true);

        if (_handoffPhase != HandoffPhase.Charge || !_profile.AllowLoop)
            return;

        // 彗星状亮带：头部在封面角那侧，尾部 150px 内渐隐到 0；缓入缓出滑行。
        var head = CableBandHead(index);
        var headPoint = from + delta * head;
        var tailPoint = headPoint - unit * CableBandLength;
        DrawCableBand(tailPoint, headPoint, Mathf.Sin(Mathf.Pi * Mathf.Clamp(head, 0f, 1f)));
    }

    /// <summary>亮带头部位置（0..1）：每周期前 75% 行进，其余留给节点吸收闪光。</summary>
    private float CableBandHead(int index)
    {
        var cycle = Mathf.PosMod(_chargePhase + CablePhase[index], 1f);
        if (cycle >= CableTravelFraction)
            return 1f;
        // 缓入缓出，不是线性匀速。
        return UiEase.Standard(cycle / CableTravelFraction);
    }

    /// <summary>亮带抵达封面角后，节点的一次 ~150ms 吸收小闪。</summary>
    private float NodeFlashAmount(int index)
    {
        if (_handoffPhase == HandoffPhase.Lock)
            return _lockAmount;
        if (_handoffPhase != HandoffPhase.Charge || !_profile.AllowLoop)
            return 0f;
        var cycle = Mathf.PosMod(_chargePhase + CablePhase[index], 1f);
        if (cycle < CableTravelFraction)
            return 0f;
        var progress = (cycle - CableTravelFraction) / (NodeFlashSeconds / ChargeCycleSeconds);
        if (progress >= 1f)
            return 0f;
        return Mathf.Sin(Mathf.Pi * Mathf.Clamp(progress, 0f, 1f));
    }

    /// <summary>用一条首尾羽化的渐变纹理画亮带：尾在 tailPoint、头在 headPoint。</summary>
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
        _frontLayer.DrawTextureRect(StreakTexture(),
            new Rect2(0f, 0f, BandTextureWidth, BandTextureHeight), false,
            new Color(UiFonts.Cyan, 0.85f * alpha));
        _frontLayer.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawNode(Vector2 node, float flash)
    {
        if (_handoffPhase == HandoffPhase.Lock && _lockAmount > 0.001f)
        {
            _frontLayer.DrawTextureRect(WashTexture(),
                new Rect2(node.X - 14f, node.Y - 14f, 28f, 28f), false,
                new Color(UiFonts.Cyan, 0.9f * _lockAmount));
            _frontLayer.DrawTextureRect(WashTexture(),
                new Rect2(node.X - 9f, node.Y - 9f, 18f, 18f), false,
                new Color(1f, 1f, 1f, 0.85f * _lockAmount));
        }
        else if (flash > 0.001f)
        {
            // 吸收闪光：一圈短促辉光，读作"能量被吸进去了"。
            _frontLayer.DrawTextureRect(WashTexture(),
                new Rect2(node.X - 16f, node.Y - 16f, 32f, 32f), false,
                new Color(UiFonts.Cyan, 0.55f * flash));
            _frontLayer.DrawTextureRect(WashTexture(),
                new Rect2(node.X - 8f, node.Y - 8f, 16f, 16f), false,
                new Color(1f, 1f, 1f, 0.5f * flash));
        }

        Vector2[] diamond =
        [
            new(node.X, node.Y - NodeHalf), new(node.X + NodeHalf, node.Y),
            new(node.X, node.Y + NodeHalf), new(node.X - NodeHalf, node.Y),
        ];
        var stroke = _handoffPhase switch
        {
            HandoffPhase.Discharge => NodeDimOpacity,
            HandoffPhase.Lock => NodeLitOpacity,
            _ => Mathf.Lerp(NodeDimOpacity, 0.95f, flash),
        };
        _frontLayer.DrawColoredPolygon(diamond, CoverFill);
        _frontLayer.DrawPolyline(UiGeometry.Close(diamond), new Color(UiFonts.Cyan, stroke), 2f, true);
    }

    private float CableOpacity()
    {
        if (_handoffPhase == HandoffPhase.Discharge)
        {
            // 余晖落到 12% 的过程放缓：前 70% 走完，之后保持尾辉。
            var fall = UiEase.Standard(Mathf.Clamp(
                _discharge / CableAfterglowFallFraction, 0f, 1f));
            return Mathf.Lerp(1f, CableAfterglowOpacity, fall);
        }
        return Mathf.Lerp(CableChargeOpacity, 1f, _lockAmount);
    }

    /// <summary>放电白热峰值出现在前 ~12%，之后单调衰减。</summary>
    private float DischargeFlashAmount()
    {
        if (_discharge <= 0f)
            return 0f;
        return Mathf.Pow(1f - _discharge, 1.4f) *
               Mathf.Clamp(_discharge / DischargeFlashPeak, 0f, 1f);
    }

    private void ApplyHandoffPalette()
    {
        if (!IsInstanceValid(_coverPanel))
            return;
        _coverPanel.Border = _handoffPhase == HandoffPhase.Discharge
            ? new Color(UiFonts.Cyan, 1f).Lerp(Colors.White, Mathf.Clamp(_discharge * 2f, 0f, 1f))
            : new Color(UiFonts.Cyan, Mathf.Lerp(0.64f, 1f, _lockAmount));
    }

    // ---- 程序化径向渐变（辉光 / 闪光） ----

    private static Texture2D WashTexture() => _washTexture ??= BuildWashTexture();

    private static Texture2D FlashTexture() => _flashTexture ??= BuildFlashTexture();

    private static Texture2D StreakTexture() => _streakTexture ??= BuildStreakTexture();

    /// <summary>首尾羽化的横向渐变（尾部 alpha 0 → 头部 1），用于电缆上的电流亮带。</summary>
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
        // 设计稿 cover_glow_svg('flash') 的四段停止点。
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

    private void UpdateContentVisibility()
    {
        if (_label is null || _relayContext is null)
            return;

        if (IsTrackHandoff)
        {
            // handoff 页整页随 coverage 交叉淡入淡出（文字与绘制层共用同一 alpha）。
            _label.Visible = false;
            _detail.Visible = false;
            _relayContext.Visible = _coverage > 0.001f;
            _relayContext.Modulate = new Color(1f, 1f, 1f, _coverage);
            return;
        }

        var show = _coverage >= 0.82f;
        _label.Visible = show;
        _detail.Visible = show;
        _relayContext.Visible = false;
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

    /// <summary>只负责转发绘制：某一分层的绘制指令必须在该层自己的 _Draw 里发出。</summary>
    private partial class HandoffDrawLayer : Control
    {
        public Action? Content;

        public override void _Draw() => Content?.Invoke();
    }
}
