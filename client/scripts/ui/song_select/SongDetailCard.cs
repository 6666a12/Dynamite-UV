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
    private const float CardHeight = 392f;
    private const float CardCut = 14f;
    private const float CardPad = 32f;
    private const float CoverSize = 288f;
    private const float CoverInsetX = 32f;
    private const float CoverInsetY = 52f;
    private const float InfoInsetX = 360f;
    private const float TransportInsetX = 360f;
    private const float TransportInsetY = 256f;
    private const float TransportSize = 64f;
    private const float BarInsetX = 444f;
    private const float BarInsetY = 303f;

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
        CustomMinimumSize = new Vector2(listWidth, CardHeight);
        Size = new Vector2(listWidth, CardHeight);
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        AddChild(new CutPanel
        {
            Position = Vector2.Zero,
            Size = new Vector2(listWidth, CardHeight),
            Cut = CardCut,
            Fill = new Color("18244a"),
            Border = UiFonts.Cyan,
            BorderWidth = 2f,
        });
        AddChild(new ColorRect
        {
            Position = new Vector2(2, 122),
            Size = new Vector2(6, 148),
            Color = UiFonts.Cyan,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var coverPos = new Vector2(CoverInsetX, CoverInsetY);
        var coverSize = new Vector2(CoverSize, CoverSize);
        AddChild(new CoverPlaceholder
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

        _title = MakeLabel(new Vector2(InfoInsetX, 44), new Vector2(listWidth - InfoInsetX - 40, 64),
            46, UiFonts.Text);
        AddChild(_title);
        _artist = MakeLabel(new Vector2(InfoInsetX, 136), new Vector2(700, 40), 24, UiFonts.Text);
        AddChild(_artist);
        _charter = MakeLabel(new Vector2(InfoInsetX, 174), new Vector2(700, 36), 22, UiFonts.Dim);
        AddChild(_charter);

        _transport = new PreviewTransport
        {
            MotionProfileProvider = motionProfileProvider,
            Position = new Vector2(TransportInsetX, TransportInsetY),
            Size = new Vector2(TransportSize, TransportSize),
        };
        _transport.Pressed += () => TransportPressed?.Invoke();
        AddChild(_transport);

        var previewLabel = MakeLabel(new Vector2(BarInsetX, 258), new Vector2(200, 26), 14, UiFonts.Dim);
        previewLabel.Text = "PREVIEW";
        AddChild(previewLabel);
        _previewTime = MakeLabel(new Vector2(listWidth - CardPad - 320, 258),
            new Vector2(320, 26), 15, UiFonts.Dim);
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
            Color = new Color(UiFonts.Cyan, 0.75f),
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
    }

    /// <summary>当前封面贴图（START 把它交给过场 relay）。</summary>
    public Texture2D? CoverTexture => _cover.Texture;

    /// <summary>展开态：收拢动画落点才转 false。</summary>
    public bool IsPresenting => _motion.IsPresenting;

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

    public void SetTransportPlaying(bool playing) => _transport.SetPlaying(playing);

    public void SetTransportEnabled(bool enabled) => _transport.Disabled = !enabled;

    /// <summary>卡内读数位：详情还在加载。</summary>
    public void ShowChartLoading() => _previewTime.Text = "加载谱面中…";

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
        _previewFill.Size = new Vector2(barWidth * fraction, 6);
        _previewKnob.Position = new Vector2(BarInsetX + barWidth * fraction - 7, BarInsetY - 4);
        _previewTime.Text = $"{FormatSeconds(elapsed)} / {FormatSeconds(duration)}";
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
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
        CustomMinimumSize = new Vector2(_listWidth, height);
        Size = new Vector2(_listWidth, height);
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
