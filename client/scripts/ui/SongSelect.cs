using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Ui;

/// <summary>
/// 选曲界面（样式稿 #select）：左详情（封面/曲名/谱师/时长/Note）+ 右曲库列表
/// + 底部难度循环按钮（最低档起，点击升档，跳过缺失难度）+ 最佳成绩条 + START。
/// </summary>
public partial class SongSelect : Node2D
{
    private const float ListX = 590f;
    private const float ListY = 180f;
    private const float ListWidth = 1280f;
    private const float ListHeight = 736f;
    private const float RowHeight = 96f;
    private const float RowGap = 22f;
    private const float TouchScrollDeadzone = 12f;

    private int _packIdx;
    private int _diffIdx;
    private int _detailRequestGeneration;
    private bool _localTransitionBusy;
    private readonly List<SongRow> _rows = new();
    private Node2D _topRoot = null!;
    private Node2D _detailRoot = null!;
    private Node2D _listRoot = null!;
    private Node2D _bottomRoot = null!;
    private ScrollContainer _songScroll = null!;
    private int? _scrollTouchId;
    private float _scrollTouchStartY;
    private int _scrollTouchStartOffset;
    private bool _scrollTouchMoved;

    // 详情区
    private TextureRect _cover = null!;
    private CoverPlaceholder _coverFallback = null!;
    private Label _title = null!;
    private Label _artist = null!;
    private Label _meta = null!;
    private ColorRect _detailScan = null!;
    private Tween? _detailScanTween;
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
        _detailRoot = new Node2D { Name = "SongSelectDetail" };
        _listRoot = new Node2D { Name = "SongSelectLibrary" };
        _bottomRoot = new Node2D { Name = "SongSelectBottom" };
        AddChild(_topRoot);
        AddChild(_detailRoot);
        AddChild(_listRoot);
        AddChild(_bottomRoot);

        BuildTopBar();
        BuildDetail();
        BuildList();
        BuildBottomBar();

        RestoreCommittedSelection();
        BeginRefreshAll();
        CallDeferred(MethodName.EnsureSelectedRowVisible);
        PrepareEnterAnimation();
        TransitionDirector.ReportSceneReady(PlayEnterAnimation);
    }

    public override void _Input(InputEvent e)
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;

        switch (e)
        {
            case InputEventScreenTouch touch when touch.Pressed &&
                _scrollTouchId is null && ListRect.HasPoint(touch.Position):
                _scrollTouchId = touch.Index;
                _scrollTouchStartY = touch.Position.Y;
                _scrollTouchStartOffset = _songScroll.ScrollVertical;
                _scrollTouchMoved = false;
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

    private void BuildDetail()
    {
        _detailRoot.AddChild(new CutPanel { Position = new Vector2(40, 112), Size = new Vector2(480, 828) });

        _coverFallback = new CoverPlaceholder
        {
            Position = new Vector2(80, 152),
            Size = new Vector2(400, 311),
            Compact = true,
        };
        _detailRoot.AddChild(_coverFallback);

        _cover = new TextureRect
        {
            Position = new Vector2(80, 152),
            Size = new Vector2(400, 311),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _detailRoot.AddChild(_cover);

        _title = new Label { Position = new Vector2(64, 500), Size = new Vector2(420, 56) };
        UiLabels.Cjk(_title, 46, UiFonts.Text);
        _detailRoot.AddChild(_title);

        _artist = new Label { Position = new Vector2(64, 566), Size = new Vector2(420, 32) };
        UiLabels.Apply(_artist, UiFonts.Cjk, 22, UiFonts.Dim);
        _detailRoot.AddChild(_artist);

        _meta = new Label { Position = new Vector2(64, 616), Size = new Vector2(420, 34) };
        UiLabels.Tech(_meta, 24, UiFonts.Text);
        _detailRoot.AddChild(_meta);

        _detailScan = new ColorRect
        {
            Position = new Vector2(40, 112),
            Size = new Vector2(28, 828),
            Color = new Color(UiFonts.Cyan, 0.42f),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _detailRoot.AddChild(_detailScan);
    }

    private void BuildList()
    {
        _listRoot.AddChild(new CutPanel { Position = new Vector2(560, 112), Size = new Vector2(1320, 828) });

        var chartCount = GameSession.Packs.Sum(pack => pack.Charts.Count);
        var hdr = new Label
        {
            Position = new Vector2(590, 132),
            Size = new Vector2(1260, 30),
            Text = $"SONG LIBRARY · {chartCount} CHARTS",
        };
        UiLabels.Tech(hdr, 20, UiFonts.Dim);
        _listRoot.AddChild(hdr);

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
        var rows = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(ListWidth, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        rows.AddThemeConstantOverride("separation", (int)RowGap);
        _songScroll.AddChild(rows);

        for (var i = 0; i < GameSession.Packs.Count; i++)
        {
            var idx = i;
            var row = new SongRow
            {
                CustomMinimumSize = new Vector2(ListWidth, RowHeight),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Pack = GameSession.Packs[i],
            };
            row.Pressed += () => BrowsePack(idx);
            _rows.Add(row);
            rows.AddChild(row);
        }

        if (GameSession.Packs.Count == 0)
        {
            var empty = new Label
            {
                CustomMinimumSize = new Vector2(ListWidth, 40),
                Text = OS.HasFeature("internal_testdata") || OS.HasFeature("editor") ||
                    OS.HasFeature("editor_runtime")
                    ? "曲库为空：把谱面包放进 user://charts/ 或 res://testdata/packs/"
                    : "曲库为空：把社区谱面包放进 user://charts/",
            };
            UiLabels.Apply(empty, UiFonts.Cjk, 24, UiFonts.Dim);
            rows.AddChild(empty);
        }
    }

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

    private void BrowsePack(int index)
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy ||
            index < 0 || index >= GameSession.Packs.Count)
            return;
        _packIdx = index;
        _diffIdx = 0;
        GameSession.ClearPreload();
        BeginRefreshAll();
    }

    private void SelectVisibleRowAt(Vector2 position)
    {
        var contentY = position.Y - ListY + _songScroll.ScrollVertical;
        var stride = RowHeight + RowGap;
        var index = Mathf.FloorToInt(contentY / stride);
        var withinRow = contentY - index * stride;
        if (withinRow >= 0 && withinRow <= RowHeight)
            BrowsePack(index);
    }

    private void EnsureSelectedRowVisible()
    {
        if (_packIdx >= 0 && _packIdx < _rows.Count)
            _songScroll.EnsureControlVisible(_rows[_packIdx]);
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

        GameSession.CommitSelection(pack, diff, loaded);
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        var texture = _cover.Texture;
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
        GameSession.Packs.Count > 0
            ? GameSession.Packs[Mathf.Clamp(_packIdx, 0, GameSession.Packs.Count - 1)]
            : null;

    private void BeginRefreshAll()
    {
        var generation = ++_detailRequestGeneration;
        var pack = CurrentPack;
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Selected = i == _packIdx;

        if (pack == null || pack.Charts.Count == 0)
        {
            ApplyEmptySelection();
            return;
        }

        _diffIdx = Mathf.Clamp(_diffIdx, 0, pack.Charts.Count - 1);
        var diff = pack.Charts[_diffIdx];
        _title.Text = pack.Title;
        _artist.Text = $"{pack.Artist} · 谱师: {diff.CharterDisplay}";
        _meta.Text = "READING CHART · PREPARING DETAIL";
        _meta.Modulate = new Color(1f, 1f, 1f, 0.35f);
        _cover.Modulate = new Color(1f, 1f, 1f, 0.35f);
        _diffBtn.Disabled = false;
        _diffBtn.Text = diff.Level is { } level
            ? $"{diff.Display.ToUpperInvariant()} · Lv {level}"
            : $"{diff.Display.ToUpperInvariant()} · UNRATED";
        _startBtn.Disabled = true;
        _bestLabel.Text = "BEST —   PREPARING";
        _bestGrade.Text = "";
        StartDetailScan();

        CallDeferred(nameof(CompleteRefreshAll), generation, pack.Id, diff.ChartId);
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
        StopDetailScan();
        if (loaded is not null)
        {
            var dur = loaded.DurationSec;
            _meta.Text = $"时长 {(int)dur / 60}:{(int)dur % 60:D2}    Note {loaded.NoteCount:N0}";
            _startBtn.Disabled = false;
        }
        else
        {
            _meta.Text = "谱面无法加载";
            _startBtn.Disabled = true;
            GameSession.ClearPreload();
        }

        _cover.Texture = CoverTextureCache.Load(pack.CoverPath);
        _cover.Modulate = Colors.White;
        _meta.Modulate = Colors.White;

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

        PlayDetailReadyAnimation();
    }

    private void ApplyEmptySelection()
    {
        StopDetailScan();
        _title.Text = "—";
        _artist.Text = "";
        _meta.Text = "";
        _meta.Modulate = Colors.White;
        _cover.Texture = null;
        _cover.Modulate = Colors.White;
        _diffBtn.Text = "无可用谱面";
        _diffBtn.Disabled = true;
        _startBtn.Disabled = true;
        _bestLabel.Text = "";
        _bestGrade.Text = "";
    }

    private void StartDetailScan()
    {
        _detailScanTween?.Kill();
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.AllowLoop)
        {
            _detailScan.Visible = false;
            return;
        }
        _detailScan.Visible = true;
        _detailScan.Position = new Vector2(40, 112);
        _detailScanTween = CreateTween();
        _detailScanTween.TweenProperty(_detailScan, "position:x", 492f,
                Math.Max(0.01, profile.LoopDuration))
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.InOut);
        _detailScanTween.Finished += () =>
        {
            if (IsInstanceValid(_detailScan))
                _detailScan.Visible = false;
        };
    }

    private void StopDetailScan()
    {
        _detailScanTween?.Kill();
        _detailScanTween = null;
        if (IsInstanceValid(_detailScan))
            _detailScan.Visible = false;
    }

    private void PrepareEnterAnimation()
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
            return;
        _topRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _detailRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _listRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _bottomRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        if (!profile.AllowDirectionalMotion)
            return;
        _detailRoot.Position = new Vector2(-profile.ContentShift, 0f);
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
        TweenEnter(tween, _detailRoot, profile.AllowStagger ? profile.Stagger : 0.0);
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
        foreach (var root in new[] { _topRoot, _detailRoot, _listRoot, _bottomRoot })
        {
            root.Position = Vector2.Zero;
            root.Modulate = Colors.White;
        }
    }

    private void PlayDetailReadyAnimation()
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
            return;
        _meta.Modulate = new Color(1f, 1f, 1f, 0f);
        var original = _meta.Position;
        if (profile.AllowDirectionalMotion)
            _meta.Position = original + new Vector2(0f, profile.ValueShift);
        var tween = CreateTween().SetParallel(true);
        tween.TweenProperty(_meta, "modulate", Colors.White, profile.ValueDuration)
            .SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_meta, "position", original, profile.ValueDuration)
            .SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
    }

    private void NavigateBack()
    {
        if (!_localTransitionBusy && !TransitionDirector.IsBusy)
            TransitionDirector.Navigate(UiRoutes.Main, TransitionKind.Back);
    }

    private static void AddLine(Node owner, Vector2 pos, Vector2 size)
    {
        owner.AddChild(new ColorRect
        {
            Color = UiFonts.Line,
            Position = pos,
            Size = size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
    }
}
