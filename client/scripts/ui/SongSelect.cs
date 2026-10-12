using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Ui;

/// <summary>
/// 选曲界面 v2（样式稿「Game — Song Select v2」）：
/// 左列 QUICK SETTINGS（MODE 两段互斥 + BLEED/MIRROR/AUTO 三个同规格开关 + 全部设置入口）、
/// 右列曲库（表头 + 搜索 + 筛选占位 + 就地展开的详情卡 + 预览播放）、底部操作条。
/// 几何与交互口径见 docs/editor-ui-design.md §2.1。
/// </summary>
public partial class SongSelect : Node2D
{
    // ---- 曲库列几何 ----
    private const float ListX = 590f;
    private const float ListY = 180f;
    private const float ListWidth = 1280f;
    private const float ListHeight = 736f;
    private const float RowHeight = 96f;
    private const float RowGap = 22f;
    private const float PanelY = 112f;
    private const float PanelHeight = 828f;
    private const float PanelBottomPad = 24f;
    private const float HeaderY = 132f;
    private const float HeaderCy = 146f;
    private const float FilterX = 1470f;
    private const float FilterWidth = 56f;
    private const float SearchX = 1534f;
    private const float SearchY = 120f;
    private const float SearchWidth = 336f;
    private const float SearchHeight = 52f;
    private const float SearchCut = 14f;

    // ---- 展开卡几何（卡内坐标以卡左上角 (590,180) 为原点） ----
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

    // ---- 展开卡动效 ----
    // 高度过渡 0.18–0.22s 档位取中值，CubicOut 与进场动画的收尾手感一致。
    private const double CardResizeDuration = 0.20;
    private const double CardTextFadeOutDuration = 0.08;
    private const double CardTextFadeInDuration = 0.12;
    private const double CardCoverFadeInDuration = 0.15;

    // ---- QUICK SETTINGS 列几何 ----
    private const float QsX = 40f;
    private const float QsY = 112f;
    private const float QsWidth = 480f;
    private const float QsHeight = 828f;
    private const float QsL = 76f;
    private const float QsR = 484f;
    private const float ModeTop = 300f;
    private const float ModeRowHeight = 72f;
    private const float ModeRowGap = 8f;
    private const float SwitchTop = 540f;
    private const float SwitchPitch = 80f;
    private const float SwitchWidth = 108f;
    private const float SwitchHeight = 50f;
    // 三个开关的 13px 副文案（设计稿 §2.1）。BLEED 在 HARDCORE 下换成锁定说明。
    private const string BleedSubStandard = "可自由开关的游玩修饰";
    private const string BleedSubHardcore = "HARDCORE 下强制开启";
    private const string MirrorSub = "谱面左右镜像";
    private const string AutoSub = "自动演示 · 不写成绩";
    private const float SwitchKnob = 40f;

    private const float TouchScrollDeadzone = 12f;
    private const double PreviewFallbackStart = 0.0;
    private const double PreviewFallbackDuration = 30.0;

    private int _packIdx = -1;
    private int _diffIdx;
    private int _detailRequestGeneration;
    private bool _localTransitionBusy;
    private readonly List<SongRow> _rows = new();
    private readonly List<int> _visibleOrder = new();
    private Node2D _topRoot = null!;
    private Node2D _settingsRoot = null!;
    private Node2D _listRoot = null!;
    private Node2D _bottomRoot = null!;
    private ScrollContainer _songScroll = null!;
    private VBoxContainer _rowBox = null!;
    private int? _scrollTouchId;
    private float _scrollTouchStartY;
    private int _scrollTouchStartOffset;
    private bool _scrollTouchMoved;
    /// <summary>惯性滚动状态（见 <see cref="ScrollInertia"/>）：滚轮与触摸板只推 <see cref="_scrollTarget"/>，
    /// <see cref="_scrollDisplay"/> 是逐帧趋近后的显示值。<see cref="_scrollWritten"/> 记录上一次写进
    /// <see cref="ScrollContainer.ScrollVertical"/> 的整数，用来识别"这个值被别人改了"（选中行
    /// EnsureControlVisible、滚动条拖动、触摸拖动、键盘翻页），那些都是直接跳、惯性要让位。</summary>
    private bool _scrollAnimating;
    private float _scrollDisplay;
    private float _scrollTarget;
    private int _scrollWritten;

    // 表头 / 搜索
    private Label _libTitle = null!;
    private LineEdit _search = null!;
    private Label _emptyState = null!;

    // QUICK SETTINGS
    private readonly QuickModeRow[] _modeRows = new QuickModeRow[2];
    private Label _modeDesc = null!;
    private QuickSwitch _bleedSwitch = null!;
    private QuickSwitch _mirrorSwitch = null!;
    private QuickSwitch _autoSwitch = null!;
    private GlyphIcon _bleedIcon = null!;
    private GlyphIcon _mirrorIcon = null!;
    private GlyphIcon _autoIcon = null!;
    private Label _bleedSub = null!;

    // 展开卡
    private Control _card = null!;
    private CoverPlaceholder _cardCoverFallback = null!;
    private TextureRect _cardCover = null!;
    private Label _cardTitle = null!;
    private Label _cardArtist = null!;
    private Label _cardCharter = null!;
    private PreviewTransport _transport = null!;
    private ColorRect _previewTrack = null!;
    private ColorRect _previewFill = null!;
    private KnobMark _previewKnob = null!;
    private Label _previewTime = null!;
    private Label _previewLabel = null!;
    private bool _expanded;
    // 展开卡高度动画的瞬时态：目标高度 + 当前显示高度（Full 专用，Reduced/Off 直接落终值）。
    private float _cardShownHeight;
    private float _cardTargetHeight;
    private float _cardResizeFrom;
    private double _cardResizeElapsed;
    private bool _cardCollapsing;
    // 卡内文字淡出→换文→淡入的推进态；代际号携带 _detailRequestGeneration 的快照，
    // 快速连切时旧淡出落零后只有代际仍当前才换文，不会覆写新请求的文本/终值。
    private bool _cardTextFadeActive;
    private bool _cardTextFadingIn;
    private float _cardTextFadeFrom;
    private float _cardTextFadeTo;
    private double _cardTextFadeElapsed;
    private double _cardTextFadeDuration;
    private int _cardTextFadeGeneration = -1;
    private string _cardPendingTitle = "";
    private string _cardPendingArtist = "";
    private string _cardPendingCharter = "";
    // 封面淡入淡出（Begin 压到 0 代替旧的 0.35 瞬变，Complete 加载完成后淡回 1）：
    // 起点/目标成对记录，中途反向从当前 alpha 续走，不需要代际守卫（Complete 本身已守）。
    private bool _cardCoverFading;
    private float _cardCoverFadeFrom;
    private float _cardCoverFadeTarget;
    private double _cardCoverFadeElapsed;
    private double _cardCoverFadeDuration;
    // _Process 的卡动画门闩：没有高度/文字/封面动画段时整段推进早退，
    // 避免每帧白算三次 alpha 插值和一次容器高度检查。
    private bool _cardAnimating;

    // 预览播放
    private AudioStreamPlayer _preview = null!;
    private AudioStream? _previewStream;
    private string? _previewStreamPath;
    private double _previewStart;
    private double _previewDuration;
    private bool _previewPlaying;

    // 底部
    private CutButton _diffBtn = null!;
    private Label _bestLabel = null!;
    private Label _bestGrade = null!;
    private CutButton _startBtn = null!;

    public override void _Ready()
    {
        GameSession.EnsureInit();

        UiLayout.AddBackground(this);

        _topRoot = new Node2D { Name = "SongSelectTop" };
        _settingsRoot = new Node2D { Name = "SongSelectQuickSettings" };
        _listRoot = new Node2D { Name = "SongSelectLibrary" };
        _bottomRoot = new Node2D { Name = "SongSelectBottom" };
        AddChild(_topRoot);
        AddChild(_settingsRoot);
        AddChild(_listRoot);
        AddChild(_bottomRoot);

        _preview = new AudioStreamPlayer { Name = "PreviewPlayer", Bus = "Music" };
        AddChild(_preview);

        BuildTopBar();
        BuildQuickSettings();
        BuildLibrary();
        BuildBottomBar();
        RefreshQuickSettings();
        ApplySearchFilter();

        RestoreCommittedSelection();
        BeginRefreshAll();
        SetProcess(true);
        CallDeferred(MethodName.EnsureSelectedRowVisible);
        PrepareEnterAnimation();
        TransitionDirector.ReportSceneReady(PlayEnterAnimation);
    }

    public override void _Process(double delta)
    {
        // 列表滚动的惯性推进先做：滚轮的显示值要每帧追上目标（见 ScrollInertia）。
        StepListScroll(delta);
        if (_cardAnimating)
            StepCardAnimations(delta);
        if (!_previewPlaying)
            return;
        // 播放器位置是流内位置（含 Play(from) 的起点），减去起点即本次试听已播时长。
        UpdatePreviewReadout(_preview.GetPlaybackPosition() - _previewStart);
    }

    /// <summary>展开卡的两段逐帧推进：Resize 是 Full 专用的高度撑开，Fade 走 alpha（Reduced 允许）。
    /// 由 <see cref="_cardAnimating"/> 作门闩：没有动画段时整段早退，_Process 里只剩滚动与预览读数。
    /// 每段到终值即清位，空闲不逐帧空转。</summary>
    private void StepCardAnimations(double delta)
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        var settled = true;

        // ---- 高度：Full 才动画，Reduced/Off 直接贴目标 ----
        if (_cardShownHeight != _cardTargetHeight)
        {
            if (profile.AllowDirectionalMotion && CardResizeDuration > 0.0)
            {
                _cardResizeElapsed += delta;
                var progress = Mathf.Clamp((float)(_cardResizeElapsed / CardResizeDuration), 0f, 1f);
                _cardShownHeight = Mathf.Lerp(_cardResizeFrom, _cardTargetHeight,
                    UiEase.CubicBezier(progress, 0.33f, 1f, 0.68f, 1f));
                if (progress >= 1f)
                {
                    _cardShownHeight = _cardTargetHeight;
                    if (_cardCollapsing)
                    {
                        // 收拢落点才交还 _expanded 并恢复普通行（对齐非动画路径的收尾语义）；
                        // 漏掉这一步会让被展开的那一行永久隐藏。
                        _cardCollapsing = false;
                        _expanded = false;
                        _card.Visible = false;
                        ApplySearchFilter();
                    }
                }
                settled = _cardShownHeight == _cardTargetHeight;
            }
            else
            {
                _cardShownHeight = _cardTargetHeight;
                if (_cardCollapsing && _cardTargetHeight <= RowHeight)
                {
                    _cardCollapsing = false;
                    _expanded = false;
                    _card.Visible = false;
                    ApplySearchFilter();
                }
            }
            ApplyCardHeight();
        }
        else
            _cardResizeElapsed = 0.0;

        // ---- 卡内文字淡出→换文→淡入：代际守卫在换文处生效 ----
        if (_cardTextFadeActive)
        {
            _cardTextFadeElapsed += delta;
            var progress = Mathf.Clamp((float)(_cardTextFadeElapsed / _cardTextFadeDuration), 0f, 1f);
            var alpha = Mathf.Lerp(_cardTextFadeFrom, _cardTextFadeTo, UiEase.Standard(progress));
            _cardTitle.Modulate = new Color(1f, 1f, 1f, alpha);
            _cardArtist.Modulate = new Color(1f, 1f, 1f, alpha);
            // 谱师名的字体色本身就是 Dim，调制用白色 alpha 即可；再乘 Dim 会在淡入完成、
            // SetExtendedInfoDim(false) 落回白色调制时出现一次亮度跳变。
            _cardCharter.Modulate = new Color(1f, 1f, 1f, alpha);
            if (progress >= 1f)
            {
                _cardTextFadeActive = false;
                if (_cardTextFadingIn)
                {
                    _cardTextFadeGeneration = -1;
                    SetExtendedInfoDim(false);
                }
                else if (_cardTextFadeGeneration == _detailRequestGeneration)
                {
                    // 淡出落零：代际仍当前才换新文并转淡入；途中来了新刷新（代际已变），
                    // 新一次 BeginRefreshAll 会自己启动淡出/换文，这里不动文本。
                    _cardTitle.Text = _cardPendingTitle;
                    _cardArtist.Text = _cardPendingArtist;
                    _cardCharter.Text = _cardPendingCharter;
                    StartCardTextFade(fadeIn: true);
                    // 淡入刚起步，本帧不算 settled——否则帧末的门闩回收会把淡入冻结在 alpha 0
                    // （平时靠封面淡入碰巧更长把门闩续住，不能依赖这个时序巧合）。
                    settled = false;
                }
            }
            else
                settled = false;
        }

        // ---- 封面淡入淡出：起点→目标模型，中途反向从当前 alpha 续走 ----
        if (_cardCoverFading)
        {
            _cardCoverFadeElapsed += delta;
            var progress = Mathf.Clamp((float)(_cardCoverFadeElapsed / _cardCoverFadeDuration), 0f, 1f);
            var alpha = Mathf.Lerp(_cardCoverFadeFrom, _cardCoverFadeTarget, UiEase.Standard(progress));
            _cardCover.Modulate = new Color(1f, 1f, 1f, alpha);
            if (progress >= 1f)
            {
                _cardCoverFading = false;
                _cardCover.Modulate = new Color(1f, 1f, 1f, _cardCoverFadeTarget);
            }
            else
                settled = false;
        }
        // SongSelect 的 _Process 还承担惯性滚动与预览读数，不能停整个 _Process；
        // 这里用 _cardAnimating 作门闩，静止态整段早退，保持每帧成本可忽略。
        _cardAnimating = !settled;
    }

    /// <summary>启动一段封面 alpha 过渡：起点取当前 alpha，动画途中反向也从当前值续走。</summary>
    private void StartCardCoverFade(float target, double duration)
    {
        _cardCoverFadeFrom = _cardCover.Modulate.A;
        _cardCoverFadeTarget = target;
        _cardCoverFadeElapsed = 0.0;
        _cardCoverFadeDuration = duration;
        _cardCoverFading = true;
        _cardAnimating = true;
    }

    /// <summary>取消在途的封面 alpha 过渡（Off 模式用）：不清调制，只掐掉 fade，
    /// 防止上次动画态残留的 0→0 fade 把新封面逐帧盖回透明。
    /// _cardAnimating 门闩由 StepCardAnimations 末行按 !settled 每帧重算，
    /// 没有其它在途动画时自然回落，无需在此手动清。</summary>
    private void CancelCardCoverFade()
    {
        _cardCoverFading = false;
    }

    /// <summary>把当前显示高度落到卡上：ClipContents 让未撑开的高度裁掉卡内下半部分，
    /// 下面的行随 VBox 重排平滑滑动。</summary>
    private void ApplyCardHeight()
    {
        var height = Mathf.Max(RowHeight, _cardShownHeight);
        _card.CustomMinimumSize = new Vector2(ListWidth, height);
        _card.Size = new Vector2(ListWidth, height);
        _card.ClipContents = true;
    }

    /// <summary>启动一次高度过渡：起点总是当前显示高度，动画途中点别的行/再点收起时
    /// 直接从半截位置反向，不留半截状态；同时点亮 _Process 的动画门闩。</summary>
    private void StartCardResize(float target)
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        _cardTargetHeight = target;
        if (!profile.AllowDirectionalMotion || CardResizeDuration <= 0.0)
        {
            _cardShownHeight = target;
            _cardResizeElapsed = 0.0;
            ApplyCardHeight();
            return;
        }
        if (_cardShownHeight == target)
            return;
        _cardResizeFrom = _cardShownHeight;
        _cardResizeElapsed = 0.0;
        _cardAnimating = true;
    }

    /// <summary>本次滚动的有效时间常数：client 没有惯性强弱设置项，用固定常量；但
    /// GENERAL / MOTION = OFF 时按"无动效"语义强制为 0（与编辑器同一选择，见
    /// docs/editor-ui-design.md §4 第 14 条）。</summary>
    private static double ScrollTau =>
        GameSession.Settings.MotionMode == UiMotionMode.Off ? 0d : ScrollInertia.TauSeconds;

    /// <summary>惯性推进列表滚动。与编辑器的滚轮处理同一模型：推力只改
    /// <see cref="_scrollTarget"/>，显示值按帧率无关的指数趋近。
    ///
    /// <see cref="_scrollDisplay"/> 是浮点累加器，只在写进 <see cref="ScrollContainer.ScrollVertical"/> 时取整：
    /// 若把取整后的值当状态，尾巴上不足 1px 的步进会被反复抹掉，动画永远收敛不了。
    ///
    /// 检测"别人改了滚动位置"是必要的：<see cref="ScrollContainer.EnsureControlVisible"/>（选中行/展开卡）、
    /// 拖动滚动条、触摸拖动都会直接写 <c>ScrollVertical</c>。这些是直接跳转，必须以它们为准并中止惯性，
    /// 否则惯性会在下一帧把它们拽回去。</summary>
    private void StepListScroll(double delta)
    {
        var actual = _songScroll.ScrollVertical;
        if (actual != _scrollWritten)
        {
            _scrollWritten = actual;
            _scrollDisplay = actual;
            _scrollTarget = actual;
            _scrollAnimating = false;
            return;
        }
        if (!_scrollAnimating) return;

        var reached = Mathf.Abs(_scrollTarget - _scrollDisplay) <= ScrollInertia.SnapPixels;
        if (reached) _scrollDisplay = _scrollTarget;
        else _scrollDisplay = ScrollInertia.Step(_scrollDisplay, _scrollTarget, ScrollTau, delta);
        _scrollAnimating = !reached;
        WriteListScroll();
    }

    /// <summary>可滚动的最大偏移。取内部 VScrollBar 的 max−page（Godot 把 page 当作最小 max），
    /// 内容不超高时为 0。</summary>
    private float MaxListScroll()
    {
        var bar = _songScroll.GetVScrollBar();
        return Mathf.Max(0f, (float)(bar.MaxValue - bar.Page));
    }

    /// <summary>把显示值写进容器并回读真实落点：ScrollContainer 会把越界值钳到可滚范围之内。
    /// 回读不一致说明这个位置够不到（内容高度或页高这一帧变了），累加器与目标一起拉回来，
    /// 否则惯性会每帧重走却前进不了。</summary>
    private void WriteListScroll()
    {
        var wanted = Mathf.RoundToInt(_scrollDisplay);
        _songScroll.ScrollVertical = wanted;
        var landed = _songScroll.ScrollVertical;
        if (landed != wanted)
        {
            _scrollDisplay = landed;
            _scrollTarget = landed;
            _scrollAnimating = false;
        }
        _scrollWritten = landed;
    }

    /// <summary>推一次列表滚动增量（滚轮一格 / 触摸板一段）。惯性途中继续加在目标上，
    /// 所以快速连滚会累积而不是每一格从头开始。惯性为 0（MOTION = OFF）时直接落目标，
    /// 与改动前的瞬时逐格滚动一致。</summary>
    private void PushListScroll(float pixels)
    {
        if (!_scrollAnimating)
        {
            // 新手势以真实位置起步，不沿用上一次外部改动留下的旧显示值。
            var actual = _songScroll.ScrollVertical;
            _scrollDisplay = actual;
            _scrollTarget = actual;
            _scrollWritten = actual;
        }
        _scrollTarget = Mathf.Clamp(_scrollTarget + pixels, 0f, MaxListScroll());
        if (ScrollTau <= 0d)
        {
            _scrollDisplay = _scrollTarget;
            WriteListScroll();
            _scrollAnimating = false;
            return;
        }
        _scrollAnimating = true;
    }

    /// <summary>滚轮一格的距离沿用 ScrollContainer 原生口径（页高 / 8），逐格距离与改动前一致，
    /// 差别只在它是滑过去而不是瞬移。</summary>
    private float WheelScrollPixels(float factor) =>
        (float)(_songScroll.GetVScrollBar().Page / 8.0) * factor;

    public override void _ExitTree()
    {
        StopPreview();
        _previewStream = null;
    }

    public override void _Input(InputEvent e)
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;

        // 滚轮/触摸板滚动先接管（见 HandleListScrollInput 的注释）：必须在 GUI 拿到事件之前，
        // 否则 ScrollContainer 会先把位置瞬移掉。事件已被消费就直接返回，不再走后面的触摸分支。
        if (HandleListScrollInput(e))
            return;

        // 搜索框聚焦时 Esc 先清空并交出焦点，避免直接吃掉整页的返回语义。
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } &&
            _search.HasFocus())
        {
            _search.Text = "";
            ApplySearchFilter();
            _search.ReleaseFocus();
            GetViewport().SetInputAsHandled();
            return;
        }

        switch (e)
        {
            case InputEventScreenTouch touch when touch.Pressed &&
                _scrollTouchId is null && ListRect.HasPoint(touch.Position):
                _scrollTouchId = touch.Index;
                _scrollTouchStartY = touch.Position.Y;
                _scrollTouchStartOffset = _songScroll.ScrollVertical;
                _scrollTouchMoved = false;
                // 手指落下就交出手指跟随：惯性拖尾会顶住手指下的内容。
                // 触摸拖动本身是直接操纵（内容跟手），保持 1:1，不吃惯性。
                StopListInertia();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenDrag drag when _scrollTouchId == drag.Index:
            {
                var delta = drag.Position.Y - _scrollTouchStartY;
                if (Math.Abs(delta) >= TouchScrollDeadzone)
                    _scrollTouchMoved = true;
                if (_scrollTouchMoved)
                    _songScroll.ScrollVertical = _scrollTouchStartOffset - Mathf.RoundToInt(delta);
                GetViewport().SetInputAsHandled();
                break;
            }
            case InputEventScreenTouch touch when !touch.Pressed &&
                _scrollTouchId == touch.Index:
                if (!_scrollTouchMoved && ListRect.HasPoint(touch.Position))
                    SelectVisibleRowAt(touch.Position);
                _scrollTouchId = null;
                _scrollTouchMoved = false;
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>滚轮与触摸板滚动在 <c>_Input</c> 里截住（它先于 GUI 派发，见 Godot 的输入顺序）：
    /// 交给 ScrollContainer 自己处理就是瞬移，接管后才谈得上惯性。返回 true 表示本次事件已被
    /// 消费（已 SetInputAsHandled）；指针不在列表上时不拦截，让事件照常往下走。</summary>
    private bool HandleListScrollInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel
                when ListRect.HasPoint(wheel.Position):
            {
                var up = wheel.ButtonIndex == MouseButton.WheelUp;
                PushListScroll(WheelScrollPixels((up ? -1f : 1f) * wheel.Factor));
                GetViewport().SetInputAsHandled();
                return true;
            }
            // 触摸板两指滚动走 PanGesture，位移口径与 ScrollContainer 原生一致（页高 / 8）。
            case InputEventPanGesture pan when ListRect.HasPoint(pan.Position):
                PushListScroll(WheelScrollPixels(pan.Delta.Y));
                GetViewport().SetInputAsHandled();
                return true;
        }
        return false;
    }

    /// <summary>中止惯性：显示值与目标值停在当前位置（滚轮连滚中被外部改动打断时用）。</summary>
    private void StopListInertia()
    {
        _scrollAnimating = false;
        _scrollDisplay = _songScroll.ScrollVertical;
        _scrollTarget = _scrollDisplay;
        _scrollWritten = _songScroll.ScrollVertical;
    }

    private static Rect2 ListRect => new(ListX, ListY, ListWidth, ListHeight);

    private void BuildTopBar()
    {
        var bar = new ColorRect
        {
            Color = new Color(0.04f, 0.06f, 0.12f, 0.9f),
            Size = new Vector2(UiLayout.DesignWidth, 76),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _topRoot.AddChild(bar);
        AddLine(_topRoot, new Vector2(0, 76), new Vector2(UiLayout.DesignWidth, 2));

        var logo = new Label { Position = new Vector2(66, 16), Text = "Dynamite Universe" };
        UiLabels.Tech(logo, 34, UiFonts.Text);
        _topRoot.AddChild(logo);

        var back = new CutButton
        {
            Position = new Vector2(1736, 12),
            Size = new Vector2(140, 52),
            Text = "返回",
            FontSize = 22,
        };
        back.Pressed += NavigateBack;
        _topRoot.AddChild(back);
    }

    // ---- 左列 QUICK SETTINGS ----

    private void BuildQuickSettings()
    {
        _settingsRoot.AddChild(new CutPanel
        {
            Position = new Vector2(QsX, QsY),
            Size = new Vector2(QsWidth, QsHeight),
        });

        var heading = MakeLabel(new Vector2(QsL, 142), new Vector2(300, 28), 14, UiFonts.Dim);
        heading.Text = "QUICK SETTINGS";
        _settingsRoot.AddChild(heading);
        AddLine(_settingsRoot, new Vector2(QsL, 176), new Vector2(QsR - QsL, 1.5f));

        var modeLabel = MakeLabel(new Vector2(QsL, 218), new Vector2(200, 32), 26, UiFonts.Text);
        modeLabel.Text = "MODE";
        _settingsRoot.AddChild(modeLabel);
        _modeDesc = MakeLabel(new Vector2(QsL, 252), new Vector2(408, 26), 15, UiFonts.Dim);
        _settingsRoot.AddChild(_modeDesc);

        for (var i = 0; i < 2; i++)
        {
            var index = i;
            var row = new QuickModeRow
            {
                Position = new Vector2(QsL, ModeTop + i * (ModeRowHeight + ModeRowGap)),
                Size = new Vector2(QsR - QsL, ModeRowHeight),
                Label = index == 0 ? "STANDARD" : "HARDCORE",
                Glyph = index == 0 ? UiGlyphKind.Standard : UiGlyphKind.Hardcore,
            };
            row.Chosen += () => SetGameplayMode(index == 0
                ? GameplayMode.Standard : GameplayMode.Hardcore);
            _settingsRoot.AddChild(row);
            _modeRows[i] = row;
        }

        AddLine(_settingsRoot, new Vector2(QsL, 480), new Vector2(QsR - QsL, 1.5f),
            new Color(UiFonts.Line, 0.6f));

        _bleedSwitch = AddQuickSwitch(0, "BLEED", BleedSubStandard, UiGlyphKind.Bleed,
            out _bleedIcon, out _bleedSub);
        _mirrorSwitch = AddQuickSwitch(1, "MIRROR", MirrorSub, UiGlyphKind.Mirror,
            out _mirrorIcon, out _);
        _autoSwitch = AddQuickSwitch(2, "AUTO", AutoSub, UiGlyphKind.Auto,
            out _autoIcon, out _);
        _bleedSwitch.Toggled += value =>
        {
            GameSession.Settings.BleedEnabled = value;
            PersistSettings();
            RefreshQuickSettings();
        };
        _mirrorSwitch.Toggled += value =>
        {
            GameSession.Settings.MirrorEnabled = value;
            PersistSettings();
            RefreshQuickSettings();
        };
        _autoSwitch.Toggled += value =>
        {
            GameSession.Settings.AutoEnabled = value;
            PersistSettings();
            RefreshQuickSettings();
        };

        AddLine(_settingsRoot, new Vector2(QsL, 772), new Vector2(QsR - QsL, 1.5f),
            new Color(UiFonts.Line, 0.6f));

        var all = new QuickActionRow
        {
            Position = new Vector2(QsL, 824),
            Size = new Vector2(QsR - QsL, 80),
            Glyph = UiGlyphKind.Sliders,
            Text = "全部设置",
            SubText = "ALL SETTINGS",
        };
        all.Chosen += () =>
        {
            if (_localTransitionBusy || TransitionDirector.IsBusy)
                return;
            StopPreview();
            TransitionDirector.Navigate(UiRoutes.Settings, TransitionKind.Standard,
                "OPEN CHANNEL", "SETTINGS");
        };
        _settingsRoot.AddChild(all);
    }

    private QuickSwitch AddQuickSwitch(int index, string label, string subText, UiGlyphKind glyph,
        out GlyphIcon icon, out Label sub)
    {
        var cy = SwitchTop + index * SwitchPitch;
        icon = new GlyphIcon
        {
            Position = new Vector2(QsL + 27, cy - 10),
            Size = new Vector2(20, 20),
            Glyph = glyph,
            Color = UiFonts.Text,
        };
        _settingsRoot.AddChild(icon);

        var title = MakeLabel(new Vector2(QsL + 64, cy - 25), new Vector2(260, 28), 20, UiFonts.Text);
        title.Text = label;
        _settingsRoot.AddChild(title);
        sub = MakeLabel(new Vector2(QsL + 64, cy - 2), new Vector2(300, 22), 13, UiFonts.Dim);
        // 副文案必须在这里落字：三个开关各有各的说明，只在 RefreshQuickSettings 里改 BLEED
        // 会让 MIRROR / AUTO 的 13px 小字永远空着。
        sub.Text = subText;
        _settingsRoot.AddChild(sub);

        var toggle = new QuickSwitch
        {
            Position = new Vector2(QsR - SwitchWidth, cy - SwitchHeight * 0.5f),
            Size = new Vector2(SwitchWidth, SwitchHeight),
        };
        _settingsRoot.AddChild(toggle);
        return toggle;
    }

    private void SetGameplayMode(GameplayMode mode)
    {
        if (GameSession.Settings.GameplayMode == mode)
            return;
        GameSession.Settings.GameplayMode = mode;
        // 锁定期（HARDCORE）不改写 BleedEnabled，切回 STANDARD 时恢复用户此前的选择。
        PersistSettings();
        RefreshQuickSettings();
    }

    /// <summary>把设置值刷到控件上；HARDCORE 下 BLEED 强制开并锁定。</summary>
    private void RefreshQuickSettings()
    {
        var settings = GameSession.Settings;
        var hardcore = settings.GameplayMode == GameplayMode.Hardcore;
        _modeRows[0].Selected = !hardcore;
        _modeRows[1].Selected = hardcore;
        _modeDesc.Text = hardcore
            ? SettingsScreen.GameplayModeTag(GameplayMode.Hardcore)
            : SettingsScreen.GameplayModeTag(GameplayMode.Standard);
        _modeDesc.Modulate = Colors.White;

        _bleedSwitch.SetState(hardcore || settings.BleedEnabled, locked: hardcore);
        _bleedSub.Text = hardcore ? BleedSubHardcore : BleedSubStandard;
        _mirrorSwitch.SetState(settings.MirrorEnabled, locked: false);
        _autoSwitch.SetState(settings.AutoEnabled, locked: false);

        _bleedIcon.Color = _bleedSwitch.IsOn ? UiFonts.Cyan : UiFonts.Text;
        _mirrorIcon.Color = _mirrorSwitch.IsOn ? UiFonts.Cyan : UiFonts.Text;
        _autoIcon.Color = _autoSwitch.IsOn ? UiFonts.Cyan : UiFonts.Text;
        _bleedIcon.QueueRedraw();
        _mirrorIcon.QueueRedraw();
        _autoIcon.QueueRedraw();
    }

    private static void PersistSettings() => GameSession.Settings.Save();

    // ---- 右列曲库 ----

    private void BuildLibrary()
    {
        _listRoot.AddChild(new CutPanel
        {
            Position = new Vector2(560, PanelY),
            Size = new Vector2(1320, PanelHeight),
        });

        _libTitle = MakeLabel(new Vector2(ListX, HeaderY), new Vector2(700, 30), 20, UiFonts.Dim);
        _listRoot.AddChild(_libTitle);

        // 筛选占位：只有描边、整体压到 55%，无交互（筛选项待拍板）。
        _listRoot.AddChild(new DisabledPlaceholder
        {
            Position = new Vector2(FilterX, SearchY),
            Size = new Vector2(FilterWidth, SearchHeight),
            Cut = SearchCut,
            Modulate = new Color(1f, 1f, 1f, 0.55f),
        });
        _listRoot.AddChild(new GlyphIcon
        {
            Position = new Vector2(FilterX + (FilterWidth - 22) * 0.5f, HeaderCy - 11),
            Size = new Vector2(22, 22),
            Glyph = UiGlyphKind.Funnel,
            Color = UiFonts.Dim,
            Modulate = new Color(1f, 1f, 1f, 0.55f),
        });

        BuildSearchField();

        _songScroll = new ScrollContainer
        {
            Position = new Vector2(ListX, ListY),
            Size = new Vector2(ListWidth, ListHeight),
            ClipContents = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            ScrollVerticalCustomStep = RowHeight + RowGap,
        };
        _listRoot.AddChild(_songScroll);
        _rowBox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(ListWidth, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _rowBox.AddThemeConstantOverride("separation", (int)RowGap);
        _songScroll.AddChild(_rowBox);

        for (var i = 0; i < GameSession.Packs.Count; i++)
        {
            var idx = i;
            var row = new SongRow
            {
                CustomMinimumSize = new Vector2(ListWidth, RowHeight),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Pack = GameSession.Packs[i],
            };
            row.Pressed += () => OnRowPressed(idx);
            _rows.Add(row);
            _rowBox.AddChild(row);
        }

        BuildExpandedCard();

        _emptyState = MakeLabel(Vector2.Zero, new Vector2(ListWidth, 60), 24, UiFonts.Dim);
        _emptyState.Text = "没有匹配的谱面";
        _emptyState.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyState.Visible = false;
        _rowBox.AddChild(_emptyState);

        if (GameSession.Packs.Count == 0)
        {
            _emptyState.Text = OS.HasFeature("internal_testdata") || OS.HasFeature("editor")
                ? "曲库为空：把谱面包放进 user://charts/ 或 res://testdata/packs/"
                : "曲库为空：把社区谱面包放进 user://charts/";
            _emptyState.Visible = true;
        }
    }

    private void BuildSearchField()
    {
        _listRoot.AddChild(new CutPanel
        {
            Position = new Vector2(SearchX, SearchY),
            Size = new Vector2(SearchWidth, SearchHeight),
            Cut = SearchCut,
            Fill = new Color("0f1429", 0.85f),
            Border = UiFonts.Line,
            BorderWidth = 2f,
        });
        _listRoot.AddChild(new GlyphIcon
        {
            Position = new Vector2(SearchX + 24, HeaderCy - 11),
            Size = new Vector2(22, 22),
            Glyph = UiGlyphKind.Search,
            Color = UiFonts.Dim,
        });

        // LineEdit 承担输入与 IME 组合态，样式全部让给切角底板。
        _search = new LineEdit
        {
            Position = new Vector2(SearchX + 54, SearchY + 8),
            Size = new Vector2(SearchWidth - 54 - 16, SearchHeight - 16),
            PlaceholderText = "搜索谱面…",
            CaretBlink = true,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        var empty = new StyleBoxEmpty();
        _search.AddThemeStyleboxOverride("normal", empty);
        _search.AddThemeStyleboxOverride("focus", empty);
        _search.AddThemeStyleboxOverride("read_only", empty);
        _search.AddThemeFontOverride("font", UiFonts.Tech);
        _search.AddThemeFontSizeOverride("font_size", 18);
        _search.AddThemeColorOverride("font_color", UiFonts.Text);
        _search.AddThemeColorOverride("font_placeholder_color", UiFonts.Dim);
        _search.AddThemeColorOverride("caret_color", UiFonts.Cyan);
        _search.TextChanged += _ => ApplySearchFilter();
        _listRoot.AddChild(_search);
    }

    private void BuildExpandedCard()
    {
        _card = new Control
        {
            Name = "ExpandedCard",
            CustomMinimumSize = new Vector2(ListWidth, CardHeight),
            Size = new Vector2(ListWidth, CardHeight),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _card.AddChild(new CutPanel
        {
            Position = Vector2.Zero,
            Size = new Vector2(ListWidth, CardHeight),
            Cut = CardCut,
            Fill = new Color("18244a"),
            Border = UiFonts.Cyan,
            BorderWidth = 2f,
        });
        _card.AddChild(new ColorRect
        {
            Position = new Vector2(2, 122),
            Size = new Vector2(6, 148),
            Color = UiFonts.Cyan,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var coverPos = new Vector2(CoverInsetX, CoverInsetY);
        var coverSize = new Vector2(CoverSize, CoverSize);
        _cardCoverFallback = new CoverPlaceholder
        {
            Position = coverPos,
            Size = coverSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _card.AddChild(_cardCoverFallback);
        _cardCover = new TextureRect
        {
            Position = coverPos,
            Size = coverSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _card.AddChild(_cardCover);

        _cardTitle = MakeLabel(new Vector2(InfoInsetX, 44), new Vector2(ListWidth - InfoInsetX - 40, 64),
            46, UiFonts.Text);
        _card.AddChild(_cardTitle);
        _cardArtist = MakeLabel(new Vector2(InfoInsetX, 136), new Vector2(700, 40), 24, UiFonts.Text);
        _card.AddChild(_cardArtist);
        _cardCharter = MakeLabel(new Vector2(InfoInsetX, 174), new Vector2(700, 36), 22, UiFonts.Dim);
        _card.AddChild(_cardCharter);

        _transport = new PreviewTransport
        {
            Position = new Vector2(TransportInsetX, TransportInsetY),
            Size = new Vector2(TransportSize, TransportSize),
        };
        _transport.Pressed += TogglePreviewPause;
        _card.AddChild(_transport);

        _previewLabel = MakeLabel(new Vector2(BarInsetX, 258), new Vector2(200, 26), 14, UiFonts.Dim);
        _previewLabel.Text = "PREVIEW";
        _card.AddChild(_previewLabel);
        _previewTime = MakeLabel(new Vector2(ListWidth - CardPad - 320, 258),
            new Vector2(320, 26), 15, UiFonts.Dim);
        _previewTime.HorizontalAlignment = HorizontalAlignment.Right;
        _card.AddChild(_previewTime);

        var barWidth = ListWidth - CardPad - BarInsetX;
        _previewTrack = new ColorRect
        {
            Position = new Vector2(BarInsetX, BarInsetY),
            Size = new Vector2(barWidth, 6),
            Color = new Color(1f, 1f, 1f, 0.12f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _card.AddChild(_previewTrack);
        _previewFill = new ColorRect
        {
            Position = new Vector2(BarInsetX, BarInsetY),
            Size = new Vector2(0, 6),
            Color = new Color(UiFonts.Cyan, 0.75f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _card.AddChild(_previewFill);
        _previewKnob = new KnobMark
        {
            Position = new Vector2(BarInsetX - 7, BarInsetY - 4),
            Size = new Vector2(14, 14),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _card.AddChild(_previewKnob);

        _card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                CollapseCard();
                _card.AcceptEvent();
            }
        };
        _rowBox.AddChild(_card);
    }

    private void BuildBottomBar()
    {
        _diffBtn = new CutButton
        {
            Position = new Vector2(40, 966),
            Size = new Vector2(300, 88),
            StyleKind = CutButton.ButtonStyle.Solid,
            SubText = "点击切换难度 ▲",
            FontSize = 30,
            TechFont = true,
        };
        _diffBtn.Pressed += BrowseNextDifficulty;
        _bottomRoot.AddChild(_diffBtn);

        _bottomRoot.AddChild(new CutPanel { Position = new Vector2(360, 966), Size = new Vector2(1120, 88), Cut = 10 });

        _bestLabel = new Label { Position = new Vector2(390, 992), Size = new Vector2(850, 40) };
        UiLabels.Tech(_bestLabel, 28, UiFonts.Dim);
        _bottomRoot.AddChild(_bestLabel);

        _bestGrade = new Label
        {
            Position = new Vector2(1310, 976),
            Size = new Vector2(140, 68),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        UiLabels.Tech(_bestGrade, 48, UiFonts.Dim);
        _bottomRoot.AddChild(_bestGrade);

        _startBtn = new CutButton
        {
            Position = new Vector2(1500, 966),
            Size = new Vector2(380, 88),
            Text = "START ▶",
            StyleKind = CutButton.ButtonStyle.Solid,
            FontSize = 34,
            TechFont = true,
        };
        _startBtn.Pressed += CommitAndStart;
        _bottomRoot.AddChild(_startBtn);
    }

    private static Label MakeLabel(Vector2 position, Vector2 size, int fontSize, Color color)
    {
        var label = new Label
        {
            Position = position,
            Size = size,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiLabels.Tech(label, fontSize, color);
        return label;
    }

    private static void AddLine(Node owner, Vector2 position, Vector2 size, Color? color = null)
    {
        owner.AddChild(new ColorRect
        {
            Color = color ?? UiFonts.Line,
            Position = position,
            Size = size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
    }

    // ---- 列表交互：选择 / 展开 / 搜索 ----

    private void OnRowPressed(int index)
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;
        if (_expanded && index == _packIdx)
        {
            CollapseCard();
            return;
        }
        var changedPack = index != _packIdx;
        if (changedPack)
        {
            _packIdx = index;
            _diffIdx = 0;
            GameSession.ClearPreload();
        }
        ExpandCard();
        BeginRefreshAll();
    }

    private void SelectVisibleRowAt(Vector2 position)
    {
        var contentY = position.Y - ListY + _songScroll.ScrollVertical;
        var stride = RowHeight + RowGap;
        var index = Mathf.FloorToInt(contentY / stride);
        var withinRow = contentY - index * stride;
        if (withinRow < 0 || withinRow > RowHeight)
            return;
        if (index < 0 || index >= _visibleOrder.Count)
            return;
        OnRowPressed(_visibleOrder[index]);
    }

    private void ExpandCard()
    {
        if (_packIdx < 0 || _packIdx >= _rows.Count)
            return;
        var wasHidden = !_card.Visible;
        _expanded = true;
        _cardCollapsing = false;
        _card.Visible = true;
        // 统一走过滤器刷新行可见性：既藏掉本次展开的那一行，也把**上一次**展开的行恢复成
        // 普通行。只写 `_rows[_packIdx].Visible = false` 会让旧行永久留在隐藏态，
        // 连续切歌时列表会一行行消失。
        ApplySearchFilter();
        _rowBox.MoveChild(_card, _rows[_packIdx].GetIndex());
        // Full 从一个行高的占位撑开到完整卡（Reduced/Off 保持瞬时，不动几何状态）。
        if (UiMotionProfile.For(GameSession.Settings.MotionMode).AllowDirectionalMotion)
        {
            if (wasHidden)
            {
                _cardShownHeight = RowHeight;
                ApplyCardHeight();
            }
            StartCardResize(CardHeight);
        }
        else
        {
            // 瞬时路径也要把高度贴满：上一次 Full 模式收拢会把显示高度留在行高，
            // 之后切到 Reduced/Off 再展开时若没有这一步，卡会停在 96px 把内容裁掉。
            _cardShownHeight = CardHeight;
            _cardTargetHeight = CardHeight;
            ApplyCardHeight();
        }
        CallDeferred(nameof(EnsureSelectedRowVisible));
        // 试听在 CompleteRefreshAll（谱面就绪）里启动。
    }

    private void CollapseCard()
    {
        if (!_expanded)
            return;
        StopPreview();
        // 收拢动画期间保持 _expanded=true：行维持隐藏、卡占位逐帧收缩，收完才换回普通行，
        // 避免收缩途中"行 + 半截卡"双重占高。途中再点行由 ExpandCard 干净反向。
        if (UiMotionProfile.For(GameSession.Settings.MotionMode).AllowDirectionalMotion)
        {
            if (_cardCollapsing)
                return;
            _cardCollapsing = true;
            StartCardResize(RowHeight);
            return;
        }
        _expanded = false;
        _card.Visible = false;
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        var query = _search.Text.Trim();
        _visibleOrder.Clear();
        for (var i = 0; i < _rows.Count; i++)
        {
            var match = RowMatches(i, query);
            _rows[i].Visible = match && !(_expanded && i == _packIdx);
            if (match)
                _visibleOrder.Add(i);
        }

        if (_rows.Count > 0)
        {
            _emptyState.Visible = _visibleOrder.Count == 0 && !_expanded;
            _emptyState.Text = "没有匹配的谱面";
        }

        // 选中项保持合法：被过滤掉就选第一个可见项（没有可见项时清空选择）。
        if (_visibleOrder.Count == 0)
        {
            if (_expanded)
                CollapseCard();
            _packIdx = -1;
            ApplyEmptySelection();
            return;
        }

        if (_packIdx < 0 || !_visibleOrder.Contains(_packIdx))
        {
            if (_expanded)
                CollapseCard();
            _packIdx = _visibleOrder[0];
            _diffIdx = 0;
            GameSession.ClearPreload();
            BeginRefreshAll();
        }
    }

    private bool RowMatches(int index, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        var pack = GameSession.Packs[index];
        if (pack.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            pack.Artist.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var chart in pack.Charts)
        {
            if (chart.CharterDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void EnsureSelectedRowVisible()
    {
        if (_expanded && IsInstanceValid(_card))
        {
            _songScroll.EnsureControlVisible(_card);
            return;
        }
        if (_packIdx >= 0 && _packIdx < _rows.Count && _rows[_packIdx].Visible)
            _songScroll.EnsureControlVisible(_rows[_packIdx]);
    }

    // ---- 预览播放 ----

    private void StartPreview()
    {
        StopPreview();
        var pack = CurrentPack;
        if (pack == null || pack.Charts.Count == 0)
            return;
        var diff = pack.Charts[Mathf.Clamp(_diffIdx, 0, pack.Charts.Count - 1)];
        var loaded = GetOrCacheChart(pack, diff);
        if (loaded == null)
            return;

        var preview = pack.PreviewFor(diff)
            ?? new ChartPreview(PreviewFallbackStart, PreviewFallbackDuration);
        _previewStart = Math.Max(0.0, preview.StartSec);
        _previewDuration = preview.DurationSec > 0.0 ? preview.DurationSec : PreviewFallbackDuration;

        if (_previewStream == null ||
            !StringComparer.Ordinal.Equals(_previewStreamPath, loaded.ResolvedAudioPath))
        {
            // Godot 没有本地文件流式播放 API；整段加载并在切歌时释放。
            _previewStream = Res.LoadAudio(loaded.ResolvedAudioPath);
            _previewStreamPath = _previewStream != null ? loaded.ResolvedAudioPath : null;
        }
        if (_previewStream == null)
        {
            _transport.SetPlaying(false);
            _previewTime.Text = "预览不可用";
            UpdatePreviewReadout(0);
            return;
        }

        _preview.Stream = _previewStream;
        _preview.Play((float)_previewStart);
        _previewPlaying = true;
        _transport.SetPlaying(true);
        UpdatePreviewReadout(0);
    }

    private void StopPreview()
    {
        _previewPlaying = false;
        if (_preview.Stream != null)
        {
            _preview.Stop();
            _preview.Stream = null;
        }
        _previewStream = null;
        _previewStreamPath = null;
        _transport.SetPlaying(false);
        UpdatePreviewReadout(0);
    }

    private void TogglePreviewPause()
    {
        if (_previewStream == null || !_expanded)
            return;
        if (_previewPlaying)
        {
            _previewPlaying = false;
            _preview.StreamPaused = true;
            _transport.SetPlaying(false);
            return;
        }
        _previewPlaying = true;
        _preview.StreamPaused = false;
        _transport.SetPlaying(true);
    }

    /// <summary>进度只用自身计时器推进（进度条不可拖，只做显示）。</summary>
    private void UpdatePreviewReadout(double elapsed)
    {
        elapsed = Mathf.Clamp(elapsed, 0.0, _previewDuration);
        var barWidth = ListWidth - CardPad - BarInsetX;
        var fraction = _previewDuration <= 0.0 ? 0f : (float)(elapsed / _previewDuration);
        _previewFill.Size = new Vector2(barWidth * fraction, 6);
        _previewKnob.Position = new Vector2(BarInsetX + barWidth * fraction - 7, BarInsetY - 4);
        _previewTime.Text = $"{FormatSeconds(elapsed)} / {FormatSeconds(_previewDuration)}";
        if (_previewPlaying && elapsed >= _previewDuration)
        {
            // 播完自动停，不循环。
            _previewPlaying = false;
            _preview.Stop();
            _transport.SetPlaying(false);
        }
    }

    private static string FormatSeconds(double seconds)
    {
        var total = (int)Math.Round(seconds);
        return $"{total / 60}:{total % 60:D2}";
    }

    // ---- 选择 / 加载 / 结算联动（沿用既有流程） ----

    private void RestoreCommittedSelection()
    {
        var selection = GameSession.CurrentSelection;
        if (selection is null)
            return;

        var packIndex = GameSession.Packs.FindIndex(pack =>
            StringComparer.Ordinal.Equals(pack.Id, selection.Pack.Id));
        if (packIndex < 0)
            return;

        _packIdx = packIndex;
        var diffIndex = GameSession.Packs[packIndex].Charts.FindIndex(diff =>
            StringComparer.Ordinal.Equals(diff.ChartId, selection.Diff.ChartId));
        if (diffIndex >= 0)
            _diffIdx = diffIndex;
    }

    private void BrowseNextDifficulty()
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;
        var pack = CurrentPack;
        if (pack == null || pack.Charts.Count == 0)
            return;
        _diffIdx = (_diffIdx + 1) % pack.Charts.Count;
        GameSession.ClearPreload();
        BeginRefreshAll();
    }

    private LoadedChart? GetOrCacheChart(ChartPack pack, ChartDiff diff)
    {
        if (GameSession.GetPreload(pack, diff) is { } cached)
            return cached;
        if (!pack.TryLoadChart(diff, out var loaded) || loaded is null)
            return null;
        GameSession.CachePreload(pack, diff, loaded);
        return loaded;
    }

    private void CommitAndStart()
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;
        var pack = CurrentPack;
        if (pack == null || pack.Charts.Count == 0)
            return;

        _diffIdx = Mathf.Clamp(_diffIdx, 0, pack.Charts.Count - 1);
        var diff = pack.Charts[_diffIdx];
        var loaded = GetOrCacheChart(pack, diff);
        if (loaded is null)
            return;

        StopPreview();
        GameSession.CommitSelection(pack, diff, loaded);
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        var texture = _cardCover.Texture;
        CoverTextureCache.Remember(pack.CoverPath, texture);
        var level = diff.Level is { } value ? $"Lv {value}" : "UNRATED";
        var relay = new TrackRelayPresentation(
            pack.Title,
            $"{diff.Display.ToUpperInvariant()} · {level}",
            texture,
            RelaySource.Selection);

        _localTransitionBusy = true;
        _startBtn.CommitPulse(profile.FocusDuration);
        if (_packIdx >= 0 && _packIdx < _rows.Count)
            _rows[_packIdx].CommitPulse(profile.FocusDuration);
        SetSelectionActionsEnabled(false);

        if (!profile.IsAnimated)
        {
            BeginGameplayNavigation(relay);
            return;
        }

        GetTree().CreateTimer(profile.FocusDuration).Timeout += () =>
        {
            if (IsInsideTree())
                BeginGameplayNavigation(relay);
        };

        void BeginGameplayNavigation(TrackRelayPresentation presentation)
        {
            var started = TransitionDirector.Navigate(
                UiRoutes.Gameplay,
                TransitionKind.Gameplay,
                "TRACK HANDOFF",
                $"{pack.Title} · {diff.Display.ToUpperInvariant()} · {level}",
                relay: presentation);
            if (!started)
            {
                _localTransitionBusy = false;
                SetSelectionActionsEnabled(true);
            }
        }
    }

    private void SetSelectionActionsEnabled(bool enabled)
    {
        _diffBtn.Disabled = !enabled;
        _startBtn.Disabled = !enabled ||
            (CurrentPack is not { } pack || pack.Charts.Count == 0 ||
             GameSession.GetPreload(pack, pack.Charts[Mathf.Clamp(
                 _diffIdx, 0, pack.Charts.Count - 1)]) is null);
        foreach (var row in _rows)
            row.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
    }

    private ChartPack? CurrentPack =>
        _packIdx >= 0 && _packIdx < GameSession.Packs.Count
            ? GameSession.Packs[_packIdx]
            : null;

    /// <summary>卡内文字淡出/淡入：Full/Reduced 有效，Off 直接落终值。代际快照由调用方传入；
    /// 途中被新的 BeginRefreshAll 打断时由新调用重置各字段，旧推进不会覆写新文本。</summary>
    private void StartCardTextFade(bool fadeIn)
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
        {
            SetExtendedInfoDim(fadeIn ? false : true);
            return;
        }
        _cardTextFadingIn = fadeIn;
        _cardTextFadeFrom = _cardTitle.Modulate.A;
        _cardTextFadeTo = fadeIn ? 1f : 0f;
        _cardTextFadeDuration = fadeIn ? CardTextFadeInDuration : CardTextFadeOutDuration;
        _cardTextFadeElapsed = 0.0;
        _cardTextFadeActive = true;
        _cardAnimating = true;
    }

    private void BeginRefreshAll()
    {
        var generation = ++_detailRequestGeneration;
        var pack = CurrentPack;
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Selected = i == _packIdx;

        _libTitle.Text = $"SONG LIBRARY · {GameSession.Packs.Sum(p => p.Charts.Count)} CHARTS";

        if (pack == null || pack.Charts.Count == 0)
        {
            ApplyEmptySelection();
            return;
        }

        _diffIdx = Mathf.Clamp(_diffIdx, 0, pack.Charts.Count - 1);
        var diff = pack.Charts[_diffIdx];
        // 文本淡出→换文→淡入：换文延后到淡出落零那一刻，代际快照当场比对，
        // 快速连切时只有最新一次请求能把新文本写上卡。
        _cardPendingTitle = pack.Title;
        _cardPendingArtist = pack.Artist;
        _cardPendingCharter = $"谱师 {diff.CharterDisplay}";
        _cardTextFadeGeneration = generation;
        if (UiMotionProfile.For(GameSession.Settings.MotionMode).IsAnimated &&
            _cardTitle.Modulate.A > 0f)
            StartCardTextFade(fadeIn: false);
        else
        {
            // 无过渡（Off 或上一次淡出已把 alpha 落 0）：直接换文，交 Complete 淡入。
            _cardTitle.Text = _cardPendingTitle;
            _cardArtist.Text = _cardPendingArtist;
            _cardCharter.Text = _cardPendingCharter;
            SetExtendedInfoDim(true);
        }
        // 封面淡出只在动画模式做：Off 保持旧封面显示，等 Complete 换 texture 并瞬时落白；
        // 同时取消在途 fade，防止上次动画态的残留 0→0 fade 把新封面逐帧盖回透明。
        if (UiMotionProfile.For(GameSession.Settings.MotionMode).IsAnimated)
        {
            _cardCover.Modulate = new Color(1f, 1f, 1f, 0f);
            StartCardCoverFade(0f, CardTextFadeOutDuration);
        }
        else
            CancelCardCoverFade();
        _transport.Disabled = true;
        _previewTime.Text = "READING CHART";
        _diffBtn.Disabled = false;
        _diffBtn.Text = diff.Level is { } level
            ? $"{diff.Display.ToUpperInvariant()} · Lv {level}"
            : $"{diff.Display.ToUpperInvariant()} · UNRATED";
        _startBtn.Disabled = true;
        _bestLabel.Text = "BEST —   PREPARING";
        _bestGrade.Text = "";

        CallDeferred(nameof(CompleteRefreshAll), generation, pack.Id, diff.ChartId);
    }

    private void SetExtendedInfoDim(bool dim)
    {
        var alpha = dim ? 0.35f : 1f;
        _cardTitle.Modulate = new Color(1f, 1f, 1f, dim ? 0.6f : 1f);
        _cardArtist.Modulate = new Color(1f, 1f, 1f, alpha);
        _cardCharter.Modulate = new Color(1f, 1f, 1f, alpha);
    }

    private void CompleteRefreshAll(int generation, string packId, string chartId)
    {
        if (generation != _detailRequestGeneration || !IsInsideTree())
            return;
        var pack = CurrentPack;
        if (pack == null || !StringComparer.Ordinal.Equals(pack.Id, packId) ||
            pack.Charts.Count == 0)
            return;
        _diffIdx = Mathf.Clamp(_diffIdx, 0, pack.Charts.Count - 1);
        var diff = pack.Charts[_diffIdx];
        if (!StringComparer.Ordinal.Equals(diff.ChartId, chartId))
            return;

        var loaded = GetOrCacheChart(pack, diff);
        if (generation != _detailRequestGeneration)
            return;
        if (loaded is not null)
        {
            _startBtn.Disabled = false;
            _transport.Disabled = false;
        }
        else
        {
            _startBtn.Disabled = true;
            _transport.Disabled = true;
            _previewTime.Text = "谱面无法加载";
            GameSession.ClearPreload();
        }

        _cardCover.Texture = CoverTextureCache.Load(pack.CoverPath);
        // 加载完成后封面从 0 淡入（Full/Reduced），Off 瞬时；代际守卫保证只有当前请求走到这里。
        if (UiMotionProfile.For(GameSession.Settings.MotionMode).IsAnimated)
            StartCardCoverFade(1f, CardCoverFadeInDuration);
        else
            _cardCover.Modulate = Colors.White;
        if (_cardTextFadeActive)
        {
            // Complete 在 Begin 后一帧就到，文字淡出多半还在半途：不打断它，
            // 让"淡出落零→代际守卫换文→淡入"自然走完；提前落 alpha 反而会把旧文本定格在卡上。
        }
        else
            SetExtendedInfoDim(false);
        if (_expanded && loaded is not null)
            StartPreview();

        ScoreRecord? rec = null;
        if (loaded != null && loaded.TryGetScoreIdentity(out var identity))
            rec = GameSession.Scores.Get(identity);
        else if (loaded != null)
            rec = GameSession.Scores.Get(pack.Id, diff.LegacyScoreKey(pack.PackageFormat));
        if (rec != null)
        {
            _bestLabel.Text = $"BEST {rec.Score:N0}   CLEAR {rec.Acc:F2}%";
            _bestGrade.Text = rec.Grade;
            _bestGrade.AddThemeColorOverride("font_color", UiFonts.GradeColor(rec.Grade));
        }
        else
        {
            _bestLabel.Text = "BEST —   尚无成绩";
            _bestGrade.Text = "";
        }
    }

    private void ApplyEmptySelection()
    {
        // 清空选择时掐掉进行中的卡内动画：否则旧淡出的落零换文（代际未变时）或封面淡入
        // 会把陈旧内容写回空卡，"—" 也可能停在半截 alpha 上。
        _cardTextFadeActive = false;
        _cardTextFadeGeneration = -1;
        _cardCoverFading = false;
        _cardTitle.Text = "—";
        _cardArtist.Text = "";
        _cardCharter.Text = "";
        SetExtendedInfoDim(false);
        _cardCover.Texture = null;
        _cardCover.Modulate = Colors.White;
        _diffBtn.Text = "无可用谱面";
        _diffBtn.Disabled = true;
        _startBtn.Disabled = true;
        _bestLabel.Text = "";
        _bestGrade.Text = "";
        StopPreview();
    }

    // ---- 进场动画 ----

    private void PrepareEnterAnimation()
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
            return;
        _topRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _settingsRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _listRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _bottomRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        if (!profile.AllowDirectionalMotion)
            return;
        _settingsRoot.Position = new Vector2(-profile.ContentShift, 0f);
        _listRoot.Position = new Vector2(profile.ContentShift, 0f);
        _bottomRoot.Position = new Vector2(0f, profile.ListShift);
    }

    private void PlayEnterAnimation()
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
        {
            ResetEnterState();
            return;
        }

        _localTransitionBusy = true;
        var tween = CreateTween().SetParallel(true);
        TweenEnter(tween, _topRoot, 0.0);
        TweenEnter(tween, _settingsRoot, profile.AllowStagger ? profile.Stagger : 0.0);
        TweenEnter(tween, _listRoot, profile.AllowStagger ? profile.Stagger * 2.0 : 0.0);
        TweenEnter(tween, _bottomRoot, profile.AllowStagger ? profile.Stagger * 3.0 : 0.0);
        tween.Finished += () =>
        {
            ResetEnterState();
            _localTransitionBusy = false;
        };

        void TweenEnter(Tween sequence, CanvasItem item, double delay)
        {
            sequence.TweenProperty(item, "modulate", Colors.White, profile.PanelDuration)
                .SetDelay(delay).SetTrans(Tween.TransitionType.Expo)
                .SetEase(Tween.EaseType.Out);
            if (item is Node2D node)
                sequence.TweenProperty(node, "position", Vector2.Zero, profile.PanelDuration)
                    .SetDelay(delay).SetTrans(Tween.TransitionType.Expo)
                    .SetEase(Tween.EaseType.Out);
        }
    }

    private void ResetEnterState()
    {
        foreach (var root in new[] { _topRoot, _settingsRoot, _listRoot, _bottomRoot })
        {
            root.Position = Vector2.Zero;
            root.Modulate = Colors.White;
        }
    }

    private void NavigateBack()
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;
        StopPreview();
        TransitionDirector.Navigate(UiRoutes.Main, TransitionKind.Back);
    }

    // ---- 设计稿专用小控件 ----

    /// <summary>只画一个设计稿图标。</summary>
    private partial class GlyphIcon : Control
    {
        public UiGlyphKind Glyph { get; init; }
        public Color Color { get; set; } = UiFonts.Text;
        public float Stroke { get; init; } = 2f;

        public override void _Draw() => UiGlyphs.Draw(this, Glyph, Vector2.Zero, Size.X, Color, Stroke);
    }

    /// <summary>筛选占位块：按设计稿只画描边。</summary>
    private partial class DisabledPlaceholder : Control
    {
        public float Cut { get; init; } = 14f;

        public override void _Draw()
        {
            var points = UiGeometry.CutCorners(Size, Cut);
            DrawPolyline(UiGeometry.Close(points), UiFonts.Line, 2f, true);
        }
    }

    /// <summary>进度条末端 14×14 切角旋钮。</summary>
    private partial class KnobMark : Control
    {
        public override void _Draw() =>
            DrawColoredPolygon(UiGeometry.CutCorners(Size, 4f), UiFonts.Cyan);
    }

    /// <summary>MODE 两段互斥行：选中 = #18244A 填充 + cyan 描边 + 左侧定位条。</summary>
    private partial class QuickModeRow : Control
    {
        private bool _selected;
        private bool _hover;
        private UiHoverFade _hoverFade;

        [Signal]
        public delegate void ChosenEventHandler();

        public string Label { get; init; } = "";
        public UiGlyphKind Glyph { get; init; }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                QueueRedraw();
            }
        }

        public override void _Ready()
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => SetHover(true);
            MouseExited += () => SetHover(false);
            SetProcess(false);
        }

        /// <summary>hover 渐变只在过渡期跑 _Process：落终值即停表，空闲不逐帧重绘
        /// （推进条件与重绘条件必须同为「值未到终值」，任一写反都会退化成空闲无限重绘）。</summary>
        private void SetHover(bool hover)
        {
            if (_hover == hover)
                return;
            _hover = hover;
            if (!UiMotionProfile.For(GameSession.Settings.MotionMode).IsAnimated)
                _hoverFade.Snap(hover);
            SetProcess(true);
            QueueRedraw();
        }

        public override void _Process(double delta)
        {
            if (_hoverFade.Update(_hover, delta))
                QueueRedraw();
            if (_hoverFade.Settled(_hover))
                SetProcess(false);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
                InputEventScreenTouch { Pressed: true })
            {
                EmitSignal(SignalName.Chosen);
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            var points = UiGeometry.CutCorners(Size, 12f);
            var ink = _selected ? UiFonts.Cyan : UiFonts.Dim;
            if (_selected)
            {
                DrawColoredPolygon(points, new Color("18244a"));
                DrawPolyline(UiGeometry.Close(points), UiFonts.Cyan, 2f, true);
                DrawRect(new Rect2(2, 14, 6, Size.Y - 28), new Color(UiFonts.Cyan, 0.92f));
            }
            else
            {
                if (_hoverFade.Value > 0f)
                    DrawColoredPolygon(points, new Color(0.06f, 0.08f, 0.16f, 0.35f)
                        .Lerp(new Color(UiFonts.PanelHover, 0.55f), _hoverFade.Value));
                DrawPolyline(UiGeometry.Close(points), UiFonts.Line, 1.5f, true);
            }

            UiGlyphs.Draw(this, Glyph, new Vector2(24, (Size.Y - 26) * 0.5f), 26, ink, 2f);
            DrawString(UiFonts.TechBold, new Vector2(64, Size.Y * 0.5f + 6.5f), Label,
                HorizontalAlignment.Left, Size.X - 64, 18, ink);
        }
    }

    /// <summary>108×50 小开关（方块 40）；锁定态整体压暗 + 方块停右侧 60% + 左侧挂锁。</summary>
    private partial class QuickSwitch : Control
    {
        private bool _on;
        private bool _locked;
        private float _knob;
        private Tween? _tween;
        private bool _disabled;

        [Signal]
        public delegate void ToggledEventHandler(bool value);

        public bool IsOn => _on;
        public bool Locked => _locked;

        public bool Disabled
        {
            get => _disabled;
            set
            {
                _disabled = value;
                QueueRedraw();
            }
        }

        public override void _Ready()
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
            _knob = _on ? 1f : 0f;
        }

        public void SetState(bool on, bool locked)
        {
            _on = on;
            _locked = locked;
            // 锁定态方块停在右侧 60%（设计稿），解锁后回到正常端点。
            var target = locked ? 0.6f : on ? 1f : 0f;
            if (Math.Abs(_knob - target) < 0.01f)
            {
                QueueRedraw();
                return;
            }
            if (UiMotionProfile.For(GameSession.Settings.MotionMode).IsAnimated)
                StartKnobTween(target);
            else
                _knob = target;
            QueueRedraw();
        }

        private void StartKnobTween(float target)
        {
            _tween?.Kill();
            var duration = UiMotionProfile.For(GameSession.Settings.MotionMode).ValueDuration;
            _tween = CreateTween();
            _tween.TweenMethod(Callable.From<float>(value =>
                {
                    _knob = value;
                    QueueRedraw();
                }), _knob, target, duration)
                .SetTrans(Tween.TransitionType.Linear);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (_locked || _disabled)
                return;
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
                InputEventScreenTouch { Pressed: true })
            {
                _on = !_on;
                StartKnobTween(_on ? 1f : 0f);
                EmitSignal(SignalName.Toggled, _on);
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            var dim = _locked ? 0.55f : 1f;
            var track = UiGeometry.CutCorners(Size, 14f);
            if (_on)
            {
                DrawColoredPolygon(track, new Color(UiFonts.Cyan, 0.14f * dim));
                DrawPolyline(UiGeometry.Close(track), new Color(UiFonts.Cyan, dim), 2.5f, true);
            }
            else
            {
                DrawPolyline(UiGeometry.Close(track), new Color(UiFonts.Line, dim), 2.5f, true);
            }

            var knobX = 12f + _knob * (Size.X - 24f - SwitchKnob);
            var knob = UiGeometry.CutCorners(knobX, (Size.Y - SwitchKnob) * 0.5f, SwitchKnob, SwitchKnob, 10f);
            DrawColoredPolygon(knob, _on ? new Color(UiFonts.Cyan, dim) : new Color(UiFonts.Dim, 0.55f));
            if (_locked)
                UiGlyphs.Draw(this, UiGlyphKind.Lock, new Vector2(12f, Size.Y * 0.5f - 10f), 20,
                    new Color(UiFonts.Cyan, 0.85f), 1.8f);
        }
    }

    /// <summary>「全部设置」入口：滑杆图标 + 主副两行。</summary>
    private partial class QuickActionRow : Control
    {
        private bool _hover;
        private UiHoverFade _hoverFade;

        [Signal]
        public delegate void ChosenEventHandler();

        public UiGlyphKind Glyph { get; init; }
        public string Text { get; init; } = "";
        public string SubText { get; init; } = "";

        public override void _Ready()
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => SetHover(true);
            MouseExited += () => SetHover(false);
            SetProcess(false);
        }

        /// <summary>hover 渐变只在过渡期跑 _Process，落终值即停表（同 QuickModeRow 的约定）。</summary>
        private void SetHover(bool hover)
        {
            if (_hover == hover)
                return;
            _hover = hover;
            if (!UiMotionProfile.For(GameSession.Settings.MotionMode).IsAnimated)
                _hoverFade.Snap(hover);
            SetProcess(true);
            QueueRedraw();
        }

        public override void _Process(double delta)
        {
            if (_hoverFade.Update(_hover, delta))
                QueueRedraw();
            if (_hoverFade.Settled(_hover))
                SetProcess(false);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
                InputEventScreenTouch { Pressed: true })
            {
                EmitSignal(SignalName.Chosen);
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            var points = UiGeometry.CutCorners(Size, 14f);
            var hover = _hoverFade.Value;
            DrawColoredPolygon(points, new Color(0.06f, 0.08f, 0.16f, 0.85f)
                .Lerp(new Color(UiFonts.PanelHover, 0.9f), hover));
            DrawPolyline(UiGeometry.Close(points),
                UiFonts.Line.Lerp(UiFonts.Cyan, hover), 2f, true);
            UiGlyphs.Draw(this, Glyph, new Vector2(26, (Size.Y - 26) * 0.5f), 26, UiFonts.Cyan, 2.2f);
            DrawString(UiFonts.Cjk, new Vector2(72, Size.Y * 0.5f - 2f), Text,
                HorizontalAlignment.Left, Size.X - 88, 24, UiFonts.Text);
            DrawString(UiFonts.Tech, new Vector2(72, Size.Y * 0.5f + 24f), SubText,
                HorizontalAlignment.Left, Size.X - 88, 13, UiFonts.Dim);
        }
    }

    /// <summary>64×64 预览传输键：描边 + cyan 图标，图标在暂停/播放之间切换。</summary>
    private partial class PreviewTransport : Control
    {
        private bool _playing;
        private bool _disabled;
        private bool _hover;
        private UiHoverFade _hoverFade;

        [Signal]
        public delegate void PressedEventHandler();

        public bool Disabled
        {
            get => _disabled;
            set
            {
                _disabled = value;
                QueueRedraw();
            }
        }

        public void SetPlaying(bool playing)
        {
            if (_playing == playing)
                return;
            _playing = playing;
            QueueRedraw();
        }

        public override void _Ready()
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => SetHover(true);
            MouseExited += () => SetHover(false);
            SetProcess(false);
        }

        /// <summary>hover 渐变只在过渡期跑 _Process（同 QuickModeRow 的约定）。</summary>
        private void SetHover(bool hover)
        {
            if (_hover == hover)
                return;
            _hover = hover;
            if (!UiMotionProfile.For(GameSession.Settings.MotionMode).IsAnimated)
                _hoverFade.Snap(hover);
            SetProcess(true);
            QueueRedraw();
        }

        public override void _Process(double delta)
        {
            if (_hoverFade.Update(_hover, delta))
                QueueRedraw();
            if (_hoverFade.Settled(_hover))
                SetProcess(false);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (_disabled)
                return;
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
                InputEventScreenTouch { Pressed: true })
            {
                EmitSignal(SignalName.Pressed);
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            var points = UiGeometry.CutCorners(Size, 18f);
            var ink = _disabled ? new Color(UiFonts.Dim, 0.5f) : UiFonts.Cyan;
            DrawColoredPolygon(points, new Color(0.06f, 0.08f, 0.16f, 0.85f));
            DrawPolyline(UiGeometry.Close(points), _disabled
                ? new Color(UiFonts.Line, 0.6f)
                : new Color(UiFonts.Cyan, 0.85f).Lerp(UiFonts.Cyan, _hoverFade.Value), 2f, true);
            UiGlyphs.Draw(this, _playing ? UiGlyphKind.Pause : UiGlyphKind.Play,
                new Vector2((Size.X - 24) * 0.5f, (Size.Y - 24) * 0.5f), 24, ink, 2f);
        }
    }
}
