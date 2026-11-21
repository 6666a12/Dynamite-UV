using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>
/// 选曲曲库里就地展开的详情卡：卡片本体 + 封面 + 标题/曲师/谱师 + 试听传输键与只读进度条。
///
/// 持有 <see cref="SongCardMotion"/>，负责高度/文字/封面三段动画的控件应用与换文时机、
/// 空卡与封面的展示、卡内试听状态。列表位置、行可见性、详情与试听加载、底部操作条都留在页面。
/// 本组件不回读全局会话或宿主页面：动效口径由构造参数传入，试听状态由调用方写入，
/// 点击只把意图抛回调用方。
/// </summary>
internal partial class SongDetailCard : Control
{
    // ---- 卡内几何（坐标以卡左上角为原点）----
    private const float CardHeight = 354f;
    private const float CardPad = 46f;
    private const float CoverSize = 285f;
    private const float CoverInsetX = 32f;
    private const float CoverInsetY = 33f;
    private const float InfoInsetX = 351f;
    private const float TransportInsetX = 360f;
    private const float TransportInsetY = 247f;
    private const float TransportSize = 58f;
    private const float BarInsetX = 474f;
    private const float BarInsetY = 277f;

    private readonly float _listWidth;
    private readonly float _rowHeight;
    private readonly Func<UiMotionProfile> _motionProfileProvider;
    private readonly SongCardMotion _motion;
    private readonly TextureRect _cover;
    private readonly Label _title;
    private readonly Label _artist;
    private readonly Label _charter;
    private readonly PreviewTransport _transport;
    private readonly ColorRect _previewFill;
    private readonly KnobMark _previewKnob;
    private readonly Label _previewTime;
    private readonly Label _previewCaption, _tempo;
    private bool _canPlay;
    private readonly SignalReceipt _previewCurrent = new() { Name = "PreviewRailCurrent" };
    private Vector2 _playhead = new(BarInsetX, BarInsetY+3);

    /// <summary>卡本体被点击（收起请求）：卡的其余区域吞掉点击，进度条与文字不拦指针。</summary>
    public event Action? CollapseRequested;

    /// <summary>试听传输键被点击（播放/暂停请求由调用方判断能否执行）。</summary>
    public event Action? TransportPressed;

    public SongDetailCard(float listWidth, float rowHeight, Func<UiMotionProfile> motionProfileProvider)
    {
        _listWidth = listWidth;
        _rowHeight = rowHeight;
        _motionProfileProvider = motionProfileProvider;
        _motion = new SongCardMotion(rowHeight, CardHeight);

        Name = "ExpandedCard";
        CustomMinimumSize = new Vector2(0, CardHeight);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Size = new Vector2(listWidth, CardHeight);
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All; FocusEntered += QueueRedraw; FocusExited += QueueRedraw;

        var coverPos = new Vector2(CoverInsetX, CoverInsetY);
        var coverSize = new Vector2(CoverSize, CoverSize);
        AddChild(new CatalogueScope
        {
            Position = coverPos,
            Size = coverSize,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        _cover = new TextureRect
        {
            Position = coverPos,
            Size = coverSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_cover);

        _title = MakeLabel(new Vector2(InfoInsetX, 71), new Vector2(listWidth - InfoInsetX - 45, 78),
            62, MenuStyle.Text);
        _title.AddThemeFontOverride("font", UiFonts.TechBold); _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; AddChild(_title);
        _artist = MakeLabel(new Vector2(InfoInsetX, 148), new Vector2(listWidth - InfoInsetX - 46, 42), 26, MenuStyle.Secondary);
        AddChild(_artist);
        _charter = MakeLabel(new Vector2(InfoInsetX, 193), new Vector2(listWidth - InfoInsetX - 316, 36), 20, MenuStyle.Secondary);
        _artist.TextOverrunBehavior = _charter.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; AddChild(_charter);
        _tempo = MakeLabel(new Vector2(listWidth - 315, 193), new Vector2(269, 36), 19, MenuStyle.Secondary);
        _tempo.HorizontalAlignment = HorizontalAlignment.Right; AddChild(_tempo);

        _transport = new PreviewTransport
        {
            MotionProfileProvider = motionProfileProvider,
            Position = new Vector2(TransportInsetX, TransportInsetY),
            Size = new Vector2(TransportSize, TransportSize),
        };
        _transport.Pressed += () => TransportPressed?.Invoke();
        AddChild(_transport);

        _previewCaption = MakeLabel(new Vector2(BarInsetX, 235), new Vector2(295, 30), 15, MenuStyle.Secondary);
        _previewCaption.Text = "PREVIEW"; AddChild(_previewCaption);
        _previewTime = MakeLabel(new Vector2(listWidth - CardPad - 320, 235),
            new Vector2(320, 26), 15, MenuStyle.Secondary);
        _previewTime.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_previewTime);

        var barWidth = listWidth - CardPad - BarInsetX;
        AddChild(new ColorRect
        {
            Position = new Vector2(BarInsetX, BarInsetY),
            Size = new Vector2(barWidth, 6),
            Color = new Color(1f, 1f, 1f, 0.12f),
            MouseFilter = MouseFilterEnum.Ignore,
        });
        _previewFill = new ColorRect
        {
            Position = new Vector2(BarInsetX, BarInsetY),
            Size = new Vector2(0, 6),
            Color = CatalogueCardFace.Accent,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_previewFill);
        _previewKnob = new KnobMark
        {
            Position = new Vector2(BarInsetX - 7, BarInsetY - 4),
            Size = new Vector2(14, 14),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_previewKnob);
        AddChild(_previewCurrent);
    }

    /// <summary>当前封面贴图（START 把它交给过场 relay）。</summary>
    public Texture2D? CoverTexture => _cover.Texture;

    /// <summary>展开态：收拢动画落点才转 false。</summary>
    public bool IsPresenting => _motion.IsPresenting;
    public bool CanPreview => IsPresenting && !_motion.IsCollapsing;

    public void HandleListTap(Vector2 position)
    {
        if (_transport.GetGlobalRect().HasPoint(position))
        {
            if (!_transport.Disabled) TransportPressed?.Invoke();
        }
        else CollapseRequested?.Invoke();
    }

    /// <summary>逐帧推进的门闩：没有高度/文字/封面动画段时为 false，页面据此整段早退。</summary>
    public bool Animating => _motion.Animating;

    /// <summary>当前动效口径（GENERAL / MOTION 三档）：每次现取，切档后下一帧即生效。</summary>
    private CardMotionProfile CardProfile
    {
        get
        {
            var profile = _motionProfileProvider();
            return new CardMotionProfile(profile.IsAnimated, profile.AllowDirectionalMotion);
        }
    }

    /// <summary>展开：显示高度与可见性都归本组件，页面只需把本节点移到对应行位。
    /// Full 从行高占位撑开（卡已可见时沿用当前高度），Reduced/Off 当场贴满。</summary>
    public void Expand()
    {
        var applyHeight = _motion.Expand(!Visible, CardProfile);
        Visible = true;
        if (applyHeight)
            ApplyHeight();
    }

    /// <summary>请求收拢。返回 true 表示已当场落位（页面据此恢复普通行）；
    /// 动画收拢返回 false，落点由 <see cref="Advance"/> 的返回值通知。</summary>
    public bool Collapse()
    {
        if (_motion.Collapse(CardProfile) != SongCardMotion.CollapseOutcome.Completed)
            return false;
        Visible = false;
        return true;
    }

    /// <summary>逐帧推进三段动画并应用变了的控件。收拢落位时在应用其余差量前回调页面，
    /// 保持原有重入顺序。<paramref name="currentGeneration"/> 是页面当前的请求代际，
    /// 文字淡出落零时用它决定是否换文。</summary>
    public void Advance(double delta, int currentGeneration, Action collapseLanded)
    {
        var frame = _motion.Advance(delta, CardProfile, currentGeneration);
        if (frame.CollapseLanded)
        {
            // 收拢落点才交还展开态并藏卡（对齐非动画路径的收尾语义）；
            // 漏掉这一步会让被展开的那一行永久隐藏。
            Visible = false;
            collapseLanded();
        }
        if (frame.TextReplaced)
        {
            _title.Text = _motion.PendingText.Title;
            _artist.Text = _motion.PendingText.Artist;
            _charter.Text = _motion.PendingText.Charter;
        }
        if (frame.TextFadeInSettled)
            SetExtendedInfoDim(false);
        if (frame.TextAlphaChanged)
            ApplyTextAlpha();
        if (frame.CoverAlphaChanged)
            ApplyCoverAlpha();
        if (frame.HeightChanged)
            ApplyHeight();
    }

    /// <summary>换文：动画模式下淡出→落零换文→淡入；无动效或 alpha 已落零时当场写文本并压暗。</summary>
    public void ShowText(CardTextContent text, int generation)
    {
        if (_motion.BeginTextRefresh(text, generation, _title.Modulate.A, CardProfile) ==
            SongCardMotion.TextOutcome.Fading)
            return;
        _title.Text = _motion.PendingText.Title;
        _artist.Text = _motion.PendingText.Artist;
        _charter.Text = _motion.PendingText.Charter;
        SetExtendedInfoDim(true);
    }

    /// <summary>换封面前把封面压到 0（旧封面不留在卡上）；Off 保持旧封面显示。</summary>
    public void HideCover()
    {
        _motion.HideCover(CardProfile);
        ApplyCoverAlpha();
    }

    /// <summary>新封面就绪：换贴图并淡回 1（Off 瞬时）。</summary>
    public void ShowCover(Texture2D? texture)
    {
        _cover.Texture = texture;
        _motion.ShowCover(CardProfile);
        ApplyCoverAlpha();
    }

    /// <summary>详情就绪时的文字亮度收尾：淡出还在半途就不打断它，让
    /// "淡出落零→换文→淡入"自己走完；提前落 alpha 反而会把旧文本定格在卡上。</summary>
    public void SettleTextDim()
    {
        if (!_motion.IsTextFading)
            SetExtendedInfoDim(false);
    }

    /// <summary>空卡：掐掉在途动画并复位占位文本与封面。</summary>
    public void ShowEmpty()
    {
        _motion.ResetForEmptyCard();
        _title.Text = "—";
        _artist.Text = "";
        _charter.Text = "";
        SetExtendedInfoDim(false);
        _cover.Texture = null;
        ApplyCoverAlpha();
    }

    public void SetTransportPlaying(bool playing)
    {
        _transport.SetPlaying(playing);
        _previewKnob.SetPlaying(playing);
        var label = playing ? "PREVIEW   /   PLAYING" : _canPlay ? "PREVIEW   /   IDLE" : "PREVIEW";
        if (_previewCaption.Text != label) _previewCaption.Text = label;
    }

    public void ConfirmTransport()
    {
        if (!CanPreview || !_canPlay) return;
        var start = _transport.Position + _transport.Size * .5f;
        _previewCurrent.EmitConductor(CatalogueCardFace.Accent, .76f, start,
            new(start.X,296), new(start.X+12,308), new(443,308), new(BarInsetX,BarInsetY+3), _playhead);
    }
    public void ClearTransportCurrent() => _previewCurrent.Cancel();

    public void SetTempo(string value) => _tempo.Text = value;

    public void SetTransportEnabled(bool enabled) { _canPlay = enabled; _transport.Disabled = !enabled; if (!enabled) _previewCaption.Text = "PREVIEW"; }

    /// <summary>卡内读数位：详情还在加载。</summary>
    public void ShowChartLoading() { _tempo.Text = ""; _previewTime.Text = "加载谱面中…"; }

    /// <summary>卡内读数位：详情加载失败。</summary>
    public void ShowChartUnavailable() => _previewTime.Text = "谱面无法加载";

    /// <summary>卡内读数位：试听还在加载。</summary>
    public void ShowPreviewLoading() => _previewTime.Text = "加载预览中…";

    /// <summary>卡内读数位：试听不可用。</summary>
    public void ShowPreviewUnavailable() => _previewTime.Text = "预览不可用";

    /// <summary>只读试听进度：按比例写填充宽、旋钮位置与时间读数。</summary>
    public void ShowPreviewProgress(float fraction, double elapsed, double duration)
    {
        var barWidth = _listWidth - CardPad - BarInsetX;
        _playhead = new(BarInsetX + barWidth*fraction, BarInsetY+3);
        _previewCurrent.TrackReceiver(_playhead);
        _previewFill.Size = new Vector2(barWidth * fraction, 6);
        _previewKnob.Position = new Vector2(BarInsetX + barWidth * fraction - 7, BarInsetY - 4);
        _previewTime.Text = $"{FormatSeconds(elapsed)} / {FormatSeconds(duration)}";
    }

    public override void _Draw()
    {
        CatalogueCardFace.Draw(this, selected: 1f, expanded: true);
        var etch = new Color("665777");
        DrawLine(new(337,308),new(443,308),etch,1.6f,true); DrawLine(new(443,308),new(BarInsetX,BarInsetY+3),etch,1.6f,true);
        for (var x=BarInsetX+7; x<_listWidth-CardPad; x+=59) DrawLine(new(x,BarInsetY+13),new(x,BarInsetY+19),etch,1.3f,true);
        if (HasFocus()) DrawLine(new(5,135),new(5,220),CatalogueCardFace.Accent,3,true);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } ||
            (HasFocus() && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter or Key.Space }))
        {
            CollapseRequested?.Invoke();
            AcceptEvent();
        }
    }

    /// <summary>把当前显示高度落到卡上：ClipContents 让未撑开的高度裁掉卡内下半部分，
    /// 下面的行随 VBox 重排平滑滑动。</summary>
    private void ApplyHeight()
    {
        var height = Mathf.Max(_rowHeight, _motion.ShownHeight);
        CustomMinimumSize = new Vector2(0, height);
        Size = new Vector2(Size.X, height);
        ClipContents = true;
    }

    private void ApplyTextAlpha()
    {
        // 谱师名的字体色本身就是 Dim，调制用白色 alpha 即可；再乘 Dim 会在淡入完成、
        // SetExtendedInfoDim(false) 落回白色调制时出现一次亮度跳变。
        var text = new Color(1f, 1f, 1f, _motion.TextAlpha);
        _title.Modulate = text;
        _artist.Modulate = text;
        _charter.Modulate = text;
    }

    private void ApplyCoverAlpha() => _cover.Modulate = new Color(1f, 1f, 1f, _motion.CoverAlpha);

    private void SetExtendedInfoDim(bool dim)
    {
        var alpha = dim ? 0.35f : 1f;
        _title.Modulate = new Color(1f, 1f, 1f, dim ? 0.6f : 1f);
        _artist.Modulate = new Color(1f, 1f, 1f, alpha);
        _charter.Modulate = new Color(1f, 1f, 1f, alpha);
    }

    private static Label MakeLabel(Vector2 position, Vector2 size, int fontSize, Color color)
    {
        var label = new Label
        {
            Position = position,
            Size = size,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        UiLabels.Tech(label, fontSize, color);
        return label;
    }

    private static string FormatSeconds(double seconds)
    {
        var total = (int)Math.Round(seconds);
        return $"{total / 60}:{total % 60:D2}";
    }
}
