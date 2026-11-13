using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.IO;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Ui;

public partial class SongSelect
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
    private static Rect2 ListRect => new(ListX, ListY, ListWidth, ListHeight);
    private VBoxContainer _rowBox = null!;
    private Label _libTitle = null!;
    private LineEdit _search = null!;
    private Label _emptyState = null!;

    // 展开卡（卡内几何与展示都在 SongDetailCard）
    private SongDetailCard _detailCard = null!;

    // 底部
    private CutButton _diffBtn = null!;
    private Label _bestLabel = null!;
    private Label _clearLabel = null!;
    private Label _bestGrade = null!;
    private CutButton _startBtn = null!;

    private void BuildTopBar()
    {
        var logo = new Label { Position = new Vector2(64, 43), Text = "Dynamite Universe" };
        UiLabels.Tech(logo, 34, MenuStyle.Text);
        _topRoot.AddChild(logo);

        var back = new CutButton
        {
            LowBorder = true,
            Accent = MenuStyle.Accent,
            Position = new Vector2(1708, 36),
            Size = new Vector2(180, 56),
            Text = "返回",
            FontSize = 22,
        };
        back.Pressed += NavigateBack;
        _topRoot.AddChild(back);
    }

    // ---- 右列曲库 ----

    private void BuildLibrary()
    {
        _libTitle = MakeLabel(new Vector2(ListX, HeaderY), new Vector2(700, 30), 20, MenuStyle.Secondary);
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
        _listViewport = new SongListViewport(_songScroll, () => ListRect, () => ScrollTau,
            SelectVisibleRowAt);

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

        _emptyState = MakeLabel(Vector2.Zero, new Vector2(ListWidth, 60), 24, MenuStyle.Secondary);
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
        AddLine(_listRoot, new Vector2(SearchX, SearchY + SearchHeight - 2),
            new Vector2(SearchWidth, 1), new Color("2A4156"));
        _listRoot.AddChild(new GlyphIcon
        {
            Position = new Vector2(SearchX + 24, HeaderCy - 11),
            Size = new Vector2(22, 22),
            Glyph = UiGlyphKind.Search,
            Color = MenuStyle.Secondary,
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
        _search.AddThemeColorOverride("font_color", MenuStyle.Text);
        _search.AddThemeColorOverride("font_placeholder_color", MenuStyle.Secondary);
        _search.AddThemeColorOverride("caret_color", UiFonts.Cyan);
        _search.TextChanged += _ => ApplySearchFilter();
        _listRoot.AddChild(_search);
    }

    private void BuildExpandedCard()
    {
        // 卡的子树、几何、动画与卡内展示都在组件里；页面只负责把它放进列表、移动位置，
        // 并把卡上的两个点击意图接回页面流程。
        _detailCard = new SongDetailCard(ListWidth, RowHeight,
            () => UiMotionProfile.For(GameSession.Settings.MotionMode));
        _detailCard.CollapseRequested += CollapseCard;
        _detailCard.TransportPressed += TogglePreviewPause;
        _rowBox.AddChild(_detailCard);
    }

    private void BuildBottomBar()
    {
        _bottomRoot.AddChild(new ColorRect
        {
            Position = new Vector2(0, 950), Size = new Vector2(1920, 130),
            Color = new Color("121F32"), MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _diffBtn = new CutButton
        {
            LowBorder = true,
            Accent = MenuStyle.Accent,
            Position = new Vector2(40, 966),
            Size = new Vector2(300, 88),
            MinimalAccent = true,
            AlignLeft = true,
            SubText = "点击切换难度 ▲",
            FontSize = 30,
            TechFont = true,
        };
        _diffBtn.Pressed += BrowseNextDifficulty;
        _bottomRoot.AddChild(_diffBtn);

        var bestHeading = MakeLabel(new Vector2(390, 976), new Vector2(240, 22), 14, MenuStyle.Muted);
        bestHeading.Text = "BEST";
        _bottomRoot.AddChild(bestHeading);
        var clearHeading = MakeLabel(new Vector2(722, 976), new Vector2(240, 22), 14, MenuStyle.Muted);
        clearHeading.Text = "CLEAR";
        _bottomRoot.AddChild(clearHeading);
        _bestLabel = MakeLabel(new Vector2(390, 1000), new Vector2(310, 44), 30, MenuStyle.Text);
        _bottomRoot.AddChild(_bestLabel);
        _clearLabel = MakeLabel(new Vector2(722, 1000), new Vector2(460, 44), 30, MenuStyle.Secondary);
        _bottomRoot.AddChild(_clearLabel);

        _bestGrade = new Label
        {
            Position = new Vector2(1310, 976),
            Size = new Vector2(140, 68),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        UiLabels.Tech(_bestGrade, 48, MenuStyle.Secondary);
        _bottomRoot.AddChild(_bestGrade);

        _startBtn = new CutButton
        {
            LowBorder = true,
            Accent = MenuStyle.Accent,
            Position = new Vector2(1500, 966),
            Size = new Vector2(380, 88),
            Text = "START ▶",
            StyleKind = CutButton.ButtonStyle.Solid,
            FontSize = 32,
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
}
