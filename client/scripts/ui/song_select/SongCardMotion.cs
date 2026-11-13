namespace DynamiteUniverse.Ui;

/// <summary>卡内三行文字（标题 / 曲师 / 谱师）。</summary>
public readonly record struct CardTextContent(string Title, string Artist, string Charter);

/// <summary>卡片动效口径：页面从 <see cref="UiMotionProfile"/> 取值传入，
/// 让 <see cref="SongCardMotion"/> 不依赖 GameSession、Godot 或具体设置项。</summary>
public readonly record struct CardMotionProfile(bool Animated, bool Directional);

/// <summary>一次逐帧推进后，页面还需要落到控件上的差量（false 的项不必重写控件属性）。</summary>
public readonly record struct CardMotionFrame(
    bool HeightChanged,
    bool TextAlphaChanged,
    bool TextReplaced,
    bool TextFadeInSettled,
    bool CoverAlphaChanged,
    bool CollapseLanded);

/// <summary>
/// 选曲展开卡的展示与动画状态：纯 C#，不依赖 Godot，供 client 页面与 tools/core-tests 共用。
///
/// 拥有四段互相独立、各自到终值即清位的动画：
/// 高度撑开/收拢（Full 专用，逐帧插值）、卡内文字淡出→换文→淡入、封面 alpha 过渡、
/// 以及 <see cref="Animating"/> 门闩（任一动画段在途时为 true，静止为 false）。
///
/// 页面只负责控件应用（写高度 / 三个文字 Label 的调制 / 封面调制 / 收拢落点藏卡）与
/// 动效口径换算；<see cref="Advance"/> 返回的 <see cref="CardMotionFrame"/> 说明本帧哪些值变了。
///
/// 固定口径：
/// - 收拢途中 <see cref="IsPresenting"/> 仍为 true，高度落到行高那一刻才转 false（页面据此藏卡）。
/// - 高度/文字/封面都在途时从**当前值**续走，反向不留半截。
/// - 文字换文只在淡出落零、且传入代际仍当前时发生；代际过期则一个字符都不写。
/// </summary>
public sealed class SongCardMotion
{
    /// <summary>高度过渡时长：0.18–0.22s 档位取中值，CubicOut 与进场动画的收尾手感一致。</summary>
    public const double ResizeDuration = 0.20;
    public const double TextFadeOutDuration = 0.08;
    public const double TextFadeInDuration = 0.12;
    public const double CoverFadeInDuration = 0.15;

    /// <summary>收拢请求的结果：已在收拢中（忽略）/ 已起收拢动画 / 瞬时落位完成。</summary>
    public enum CollapseOutcome
    {
        Ignored,
        Animating,
        Completed,
    }

    /// <summary>换文请求的结果：淡出后再换（页面等 <see cref="CardMotionFrame.TextReplaced"/>）/
    /// 无过渡，页面当场写文本并压暗。</summary>
    public enum TextOutcome
    {
        Fading,
        Applied,
    }

    private readonly float _rowHeight;
    private readonly float _cardHeight;

    // 高度过渡的瞬时态：目标高度 + 当前显示高度（Full 专用，Reduced/Off 直接落终值）。
    private float _shownHeight;
    private float _targetHeight;
    private float _resizeFrom;
    private double _resizeElapsed;
    private bool _collapsing;

    // 文字淡出→换文→淡入的推进态；代际号携带页面的请求快照，快速连切时旧淡出落零后
    // 只有代际仍当前才换文，不会覆写新请求的文本/终值。
    private bool _textFading;
    private bool _textFadingIn;
    private float _textFadeFrom;
    private float _textFadeTo;
    private double _textFadeElapsed;
    private double _textFadeDuration;
    private int _textGeneration = -1;
    private CardTextContent _pendingText;
    private float _textAlpha = 1f;

    // 封面淡入淡出：起点/目标成对记录，中途反向从当前 alpha 续走，不需要代际守卫
    //（换封面的 Complete 本身已守代际）。
    private bool _coverFading;
    private float _coverFadeFrom;
    private float _coverFadeTarget;
    private double _coverFadeElapsed;
    private double _coverFadeDuration;
    private float _coverAlpha = 1f;

    public SongCardMotion(float rowHeight, float cardHeight)
    {
        if (!(rowHeight > 0f))
            throw new ArgumentOutOfRangeException(nameof(rowHeight));
        if (!(cardHeight >= rowHeight))
            throw new ArgumentOutOfRangeException(nameof(cardHeight));
        _rowHeight = rowHeight;
        _cardHeight = cardHeight;
        _shownHeight = rowHeight;
        _targetHeight = rowHeight;
    }

    /// <summary>收拢落点：普通行的高度。</summary>
    public float RowHeight => _rowHeight;

    /// <summary>展开落点：完整卡的高度。</summary>
    public float CardHeight => _cardHeight;

    /// <summary>卡的展开态（收拢动画途中仍为 true，落位才转 false）。</summary>
    public bool IsPresenting { get; private set; }

    public bool IsCollapsing => _collapsing;

    /// <summary>逐帧推进的门闩：任一动画段在途时为 true，静止为 false。</summary>
    public bool Animating { get; private set; }

    /// <summary>当前显示高度（页面落 <c>CustomMinimumSize</c>/<c>Size</c> 用，已含行高下限前的原始值）。</summary>
    public float ShownHeight => _shownHeight;

    public float TextAlpha => _textAlpha;

    public float CoverAlpha => _coverAlpha;

    public bool IsTextFading => _textFading;

    /// <summary>最近一次换文请愿的文本：页面在 <see cref="CardMotionFrame.TextReplaced"/> 时写入控件。</summary>
    public CardTextContent PendingText => _pendingText;

    /// <summary>展开卡。返回 true 表示显示高度已当场落位，页面需重新应用高度。
    /// <paramref name="cardWasHidden"/> 为真（卡刚从隐藏态展开）时先把显示高度压到行高占位，
    /// 再从行高撑开；卡已可见时保持当前高度，只调整目标（不改既有"显示高度 == 目标即提前返回"的口径）。</summary>
    public bool Expand(bool cardWasHidden, CardMotionProfile profile)
    {
        IsPresenting = true;
        _collapsing = false;
        if (!profile.Directional)
        {
            StartResize(_cardHeight, profile);
            return true;
        }
        if (cardWasHidden)
            _shownHeight = _rowHeight;
        StartResize(_cardHeight, profile);
        return cardWasHidden;
    }

    /// <summary>收拢卡。<see cref="CollapseOutcome.Animating"/> 期间 <see cref="IsPresenting"/> 仍为 true，
    /// 高度落到行高时 <see cref="CardMotionFrame.CollapseLanded"/> 才置位（页面在那一帧藏卡）。
    /// 已在收拢中再请求收拢返回 <see cref="CollapseOutcome.Ignored"/>，不重启动画。</summary>
    public CollapseOutcome Collapse(CardMotionProfile profile)
    {
        if (!IsPresenting)
            return CollapseOutcome.Ignored;
        if (!profile.Directional || _shownHeight == _rowHeight)
        {
            IsPresenting = false;
            _collapsing = false;
            _shownHeight = _targetHeight = _rowHeight;
            return CollapseOutcome.Completed;
        }
        if (_collapsing)
            return CollapseOutcome.Ignored;
        _collapsing = true;
        StartResize(_rowHeight, profile);
        return CollapseOutcome.Animating;
    }

    /// <summary>发起一次换文：先记下待写文本与代际，再从 <paramref name="currentTextAlpha"/>
    /// 起淡出；无动效或 alpha 已经落零时返回 <see cref="TextOutcome.Applied"/>，由页面当场写文本。</summary>
    public TextOutcome BeginTextRefresh(CardTextContent text, int generation, float currentTextAlpha,
        CardMotionProfile profile)
    {
        _pendingText = text;
        _textGeneration = generation;
        _textAlpha = currentTextAlpha;
        if (!profile.Animated || currentTextAlpha <= 0f)
            return TextOutcome.Applied;
        StartTextFade(fadeIn: false);
        return TextOutcome.Fading;
    }

    /// <summary>换封面前的封面收起：瞬时压到 0（旧封面不留在卡上），随后一段 0→0 的空转淡出
    /// 占住门闩，免得本帧末门闩先回落；Off 只掐掉在途过渡，不碰调制。</summary>
    public void HideCover(CardMotionProfile profile)
    {
        if (!profile.Animated)
        {
            _coverFading = false;
            return;
        }
        _coverAlpha = 0f;
        StartCoverFade(0f, TextFadeOutDuration);
    }

    /// <summary>新封面就绪：淡回 1（Full/Reduced），Off 瞬时落白。</summary>
    public void ShowCover(CardMotionProfile profile)
    {
        if (!profile.Animated)
        {
            _coverFading = false;
            _coverAlpha = 1f;
            return;
        }
        StartCoverFade(1f, CoverFadeInDuration);
    }

    /// <summary>空卡复位：掐掉在途的文字/封面过渡并把封面 alpha 落回 1
    /// （页面负责写占位文本与白色调制）。不清它们会把陈旧内容写回空卡。</summary>
    public void ResetForEmptyCard()
    {
        _textFading = false;
        _textGeneration = -1;
        _textAlpha = 1f;
        _coverFading = false;
        _coverAlpha = 1f;
    }

    /// <summary>逐帧推进四段动画并重算门闩。每段到终值即清位，空闲不逐帧空转。
    /// <paramref name="currentGeneration"/> 是页面当前的请求代际：文字淡出落零时用它决定是否换文。</summary>
    public CardMotionFrame Advance(double delta, CardMotionProfile profile, int currentGeneration)
    {
        var settled = true;
        var heightChanged = false;
        var collapseLanded = false;

        // ---- 高度：Full 才动画，Reduced/Off 直接贴目标 ----
        if (_shownHeight != _targetHeight)
        {
            if (profile.Directional && ResizeDuration > 0.0)
            {
                _resizeElapsed += delta;
                var progress = Math.Clamp((float)(_resizeElapsed / ResizeDuration), 0f, 1f);
                _shownHeight = Lerp(_resizeFrom, _targetHeight,
                    UiEase.CubicBezier(progress, 0.33f, 1f, 0.68f, 1f));
                if (progress >= 1f)
                {
                    _shownHeight = _targetHeight;
                    if (_collapsing)
                    {
                        _collapsing = false;
                        IsPresenting = false;
                        collapseLanded = true;
                    }
                }
                settled = _shownHeight == _targetHeight;
            }
            else
            {
                _shownHeight = _targetHeight;
                if (_collapsing && _targetHeight <= _rowHeight)
                {
                    _collapsing = false;
                    IsPresenting = false;
                    collapseLanded = true;
                }
            }
            heightChanged = true;
        }
        else
            _resizeElapsed = 0.0;

        // ---- 卡内文字淡出→换文→淡入：代际守卫在换文处生效 ----
        var textAlphaChanged = false;
        var textReplaced = false;
        var textFadeInSettled = false;
        if (_textFading)
        {
            _textFadeElapsed += delta;
            var progress = Math.Clamp((float)(_textFadeElapsed / _textFadeDuration), 0f, 1f);
            _textAlpha = Lerp(_textFadeFrom, _textFadeTo, UiEase.Standard(progress));
            textAlphaChanged = true;
            if (progress >= 1f)
            {
                _textFading = false;
                if (_textFadingIn)
                {
                    _textGeneration = -1;
                    textFadeInSettled = true;
                }
                else if (_textGeneration == currentGeneration)
                {
                    // 淡出落零：代际仍当前才换新文并转淡入；途中来了新刷新（代际已变），
                    // 新一次 BeginTextRefresh 会自己启动淡出/换文，这里不动文本。
                    textReplaced = true;
                    // 淡出能起说明本页处于动画模式（动静设置在同一页面内不会变），淡入随之动画。
                    StartTextFade(fadeIn: true);
                    // 淡入刚起步，本帧不算 settled——否则帧末的门闩回收会把淡入冻结在 alpha 0
                    // （平时靠封面淡入碰巧更长把门闩续住，不能依赖这个时序巧合）。
                    settled = false;
                }
            }
            else
                settled = false;
        }

        // ---- 封面淡入淡出：起点→目标模型，中途反向从当前 alpha 续走 ----
        var coverAlphaChanged = false;
        if (_coverFading)
        {
            _coverFadeElapsed += delta;
            var progress = Math.Clamp((float)(_coverFadeElapsed / _coverFadeDuration), 0f, 1f);
            _coverAlpha = Lerp(_coverFadeFrom, _coverFadeTarget, UiEase.Standard(progress));
            coverAlphaChanged = true;
            if (progress >= 1f)
            {
                _coverFading = false;
                _coverAlpha = _coverFadeTarget;
            }
            else
                settled = false;
        }

        Animating = !settled;
        return new CardMotionFrame(heightChanged, textAlphaChanged, textReplaced,
            textFadeInSettled, coverAlphaChanged, collapseLanded);
    }

    private void StartResize(float target, CardMotionProfile profile)
    {
        _targetHeight = target;
        if (!profile.Directional || ResizeDuration <= 0.0)
        {
            _shownHeight = target;
            _resizeElapsed = 0.0;
            return;
        }
        if (_shownHeight == target)
            return;
        _resizeFrom = _shownHeight;
        _resizeElapsed = 0.0;
        Animating = true;
    }

    private void StartTextFade(bool fadeIn)
    {
        _textFadingIn = fadeIn;
        _textFadeFrom = _textAlpha;
        _textFadeTo = fadeIn ? 1f : 0f;
        _textFadeDuration = fadeIn ? TextFadeInDuration : TextFadeOutDuration;
        _textFadeElapsed = 0.0;
        _textFading = true;
        Animating = true;
    }

    private void StartCoverFade(float target, double duration)
    {
        _coverFadeFrom = _coverAlpha;
        _coverFadeTarget = target;
        _coverFadeElapsed = 0.0;
        _coverFadeDuration = duration;
        _coverFading = true;
        Animating = true;
    }

    private static float Lerp(float from, float to, float weight) => from + (to - from) * weight;
}
