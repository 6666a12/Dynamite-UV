using Godot;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;
using static Godot.Control;
using static DynamiteUniverse.Editor.EditorPageUi;

namespace DynamiteUniverse.Editor;

internal sealed partial class EditorStartPage
{
    internal Control Root { get; }
    internal string PackagePath { get => _packagePath.Text; set => _packagePath.Text = value; }
    internal event Action? BrowseRequested;
    internal event Action? OpenRequested;
    internal event Action? CreateRequested;
    internal event Action? SettingsRequested;
    internal EditorStartPage() => Root = Build();
    internal void Report(string message, bool error)
    {
        _startMessage.Text = message;
        _startMessage.AddThemeColorOverride("font_color", error ? new Color("ff879f") : FlowMuted);
    }
    internal void Tick(double delta)
    {
        if (_settingsKey is not { } settingsKey) return;
        var before = _settingsHover.Value;
        _ = _settingsHover.Update(settingsKey.IsHovered(), delta);
        if (_settingsHover.Value != before)
            settingsKey.AddThemeColorOverride("font_color", FlowMuted.Lerp(FlowCyan, _settingsHover.Value));
    }
    private LineEdit _packagePath = null!;
    private Tween? _pathPlateTween;
    private Label _startMessage = null!;
    // Start 板同理：eyebrow ls8、wordmark ls10、水印 ls12、卡面 ls2。
    private static readonly Font StartEyebrow = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 8 };
    private static readonly Font StartWordmark = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 10 };
    private static readonly Font StartWatermark = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 12 };
    private static readonly Font StartCaption = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 2 };

    /// <summary>Start 板（设计稿 Start）。背景是两行重复水印 + 椭圆轨道系统，居中标题组下面是
    /// 两张 400×150 大卡：OPEN PACKAGE 承担选目录（BROWSE），NEW PACKAGE 进 Create 页。
    /// 设计板没有路径输入行，所以路径能力压成卡下一条 slim 行——手输/粘贴 + 回车或 OPEN
    /// 打开，卡选完目录只回填路径，不会因为误点就开始解析包。</summary>
    private Control Build()
    {
        var page = NewPage(rule: false);

        var backdrop = new StartBackdrop { MouseFilter = Control.MouseFilterEnum.Ignore };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        page.AddChild(backdrop);
        // 水印两行是刻意的出血位（x=-20 / -260），所以 Label 给足宽度且不裁切。
        // 字号一律传**设计稿值**，由 EditorFonts.ApplySize 登记表统一换算与重放（UI SCALE 立即生效）。
        AddStartText(page, -20, 60, 6000, 80, Watermark, 64, new Color(FlowCyan, .10f), StartWatermark);
        AddStartText(page, -260, 975, 6000, 80, Watermark, 64, new Color(UiFonts.Text, .07f), StartWatermark);

        // 居中标题组：eyebrow 中心 (960,246)、wordmark 中心 (960,340)。
        AddStartText(page, 0, 234, 1920, 24, "DYNAMITE UNIVERSE", 16, FlowMuted, StartEyebrow, HorizontalAlignment.Center);
        AddStartText(page, 0, 266, 1920, 148, "DYNAMAKER UV", 118, FlowCyan, StartWordmark, HorizontalAlignment.Center);

        var openCard = new StartCard
        {
            Position = new Vector2(540, 680), Size = new Vector2(400, 150),
            Fill = FlowPanel, Border = UiFonts.Line, Glyph = StartIcon.Folder, GlyphColor = FlowCyan
        };
        openCard.Pressed += () => BrowseRequested?.Invoke();
        page.AddChild(openCard);
        AddStartText(openCard, 100, 57, 280, 36, "OPEN PACKAGE", 26, FlowCyan, StartCaption);

        var newCard = new StartCard
        {
            Position = new Vector2(980, 680), Size = new Vector2(400, 150),
            Fill = FlowCyan, Glyph = StartIcon.Plus, GlyphColor = UiFonts.InkText
        };
        newCard.Pressed += () => { CreateRequested?.Invoke(); };
        page.AddChild(newCard);
        AddStartText(newCard, 100, 57, 280, 36, "NEW PROJECT", 26, UiFonts.InkText, StartCaption);

        _packagePath = NewStartPathField(page, 540, 862, 640, 52);
        var openChip = new StartCard
        {
            Position = new Vector2(1192, 862), Size = new Vector2(188, 52), Cut = 10f, Fill = FlowCyan
        };
        openChip.Pressed += () => OpenRequested?.Invoke();
        page.AddChild(openChip);
        AddStartText(openChip, 0, 0, 188, 52, "OPEN", 15, UiFonts.InkText, StartCaption, HorizontalAlignment.Center);

        _startMessage = AddFlowLabel(page, 540, 918, 840, 48, string.Empty, 15, FlowMuted);
        // 设计板没有 Settings 入口（§4.7 只写「Start 页进入」），按同一套语言补一枚低调文字键在右下角：
        // dim 13px ls2，hover 转 cyan，与页面底部同一 y 带（Settings 板的 RESET/BACK 行）。
        var settings = new Button
        {
            Position = new Vector2(1620, 976), Size = new Vector2(260, 32), Text = "SETTINGS",
            Alignment = HorizontalAlignment.Right, MouseFilter = Control.MouseFilterEnum.Stop,
            // 不吃键盘焦点：编辑器里 SPACE / ENTER 是预览快捷键，按钮抢焦点会把它们吃掉。
            FocusMode = Control.FocusModeEnum.None
        };
        var flat = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            settings.AddThemeStyleboxOverride(state, flat);
        settings.AddThemeFontOverride("font", StartCaption);
        EditorFonts.ApplySize(settings, 13);
        // SETTINGS 文字键：stylebox 全空、无自绘形状，hover 的 dim → cyan 过渡按共享 hover 档
        // lerp——font_color 覆盖由每帧的过渡值重写（hover/pressed 静态覆盖移除，颜色统一由值决定）。
        // 推进挂在 shell 自己的 _Process（它本就为 HUD 每帧在跑），到终值即停写，无空转成本。
        settings.AddThemeColorOverride("font_color", FlowMuted);
        _settingsKey = settings;
        settings.Pressed += () => SettingsRequested?.Invoke();
        page.AddChild(settings);
        return page;
    }

    /// <summary>Start 页 SETTINGS 文字键与其 hover 过渡值：壳层 _Process 每帧推进，
    /// MOTION OFF 首帧即落终值；到终值后不再写颜色覆盖（值已到位，无逐帧成本）。</summary>
    private Button? _settingsKey;
    private HoverFade _settingsHover;

    private const string Watermark =
        "DESIGN · CHART · SHIP · DESIGN · CHART · SHIP · DESIGN · CHART · SHIP · DESIGN · CHART · SHIP · DESIGN · CHART · SHIP";

    /// <summary>Start 板的文字：字距走 FontVariation，垂直居中——放置一律给框中心，
    /// 与设计稿的 ctxt/vtxt 锚点同一套语义。<paramref name="designSize"/> 是设计稿字号，
    /// 登记进 <see cref="EditorFonts"/> 的注册表，UI SCALE 换档时随之重放。</summary>
    private static Label AddStartText(Node parent, float x, float y, float w, float h, string text,
        int designSize, Color color, Font font, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Position = new Vector2(x, y), Size = new Vector2(w, h), Text = text,
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", font);
        EditorFonts.ApplySize(label, designSize);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    /// <summary>路径行：切角底板 + 无样式 LineEdit（样式全给底板，与客户端搜索框同一做法）。</summary>
    private LineEdit NewStartPathField(Control page, float x, float y, float w, float h)
    {
        var plate = new CutPanel
        {
            Position = new Vector2(x, y), Size = new Vector2(w, h), Cut = 10f,
            Fill = new Color("0f1429", .85f), Border = UiFonts.Line, BorderWidth = 2f,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        page.AddChild(plate);
        var field = new LineEdit
        {
            Position = new Vector2(x + 14f, y + 5f), Size = new Vector2(w - 28f, h - 10f),
            PlaceholderText = "PACKAGE DIRECTORY",
            CaretBlink = true,
        };
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "focus", "read_only" }) field.AddThemeStyleboxOverride(state, empty);
        field.AddThemeFontOverride("font", EditorFonts.Medium);
        EditorFonts.ApplySize(field, 17);
        field.AddThemeColorOverride("font_color", UiFonts.Text);
        field.AddThemeColorOverride("font_placeholder_color", FlowMuted);
        field.AddThemeColorOverride("caret_color", FlowCyan);
        // 样式全给底板，LineEdit 三个 stylebox 都是空的，焦点反馈只能画在底板描边上：
        // 聚焦时 Border 平滑过渡到 cyan（.12s），失焦过渡回去；快速进出焦点要 kill 上一个
        // Tween，避免两个动画打架把描边留在中间色。MOTION OFF 尊重设置真源，直接跳终值。
        field.FocusEntered += () => TweenPathPlateBorder(plate, UiFonts.Cyan);
        field.FocusExited += () => TweenPathPlateBorder(plate, UiFonts.Line);
        field.TextSubmitted += _ => OpenRequested?.Invoke();
        page.AddChild(field);
        return field;
    }

    private void TweenPathPlateBorder(CutPanel plate, Color target)
    {
        _pathPlateTween?.Kill();
        _pathPlateTween = null;
        if (EditorSettings.Current.MotionMode == UiMotionMode.Off)
        {
            plate.Border = target;
            return;
        }
        var from = plate.Border;
        _pathPlateTween = _packagePath.CreateTween();
        _pathPlateTween.TweenMethod(Callable.From<float>(t => plate.Border = from.Lerp(target, t)),
            0f, 1f, .12f);
    }

    /// <summary>Start 板的背景装饰层（设计稿 START 段的轨道系统）：两个椭圆描边、轨道上
    /// 六个 18×18 切角方块、中心 70×70 切角方块与一条 640×2 横线。静态帧，不带动画。</summary>
    private sealed partial class StartBackdrop : Control
    {
        private const float CenterX = 960f, CenterY = 555f;
        private const float NodeSize = 18f, NodeCut = 5f, CoreSize = 70f, CoreCut = 18f;
        /// <summary>轨道方块：极坐标 (角度, rx, ry)，取设计稿同一组参数。</summary>
        private static readonly (float Angle, float Rx, float Ry)[] OrbitNodes =
        [
            (25f, 280f, 95f), (140f, 280f, 95f), (75f, 210f, 150f),
            (250f, 210f, 150f), (200f, 280f, 95f), (320f, 210f, 150f),
        ];

        public override void _Draw()
        {
            DrawPolyline(UiGeometry.Close(Orbit(280f, 95f)), new Color(FlowCyan, .35f), 2f, true);
            DrawPolyline(UiGeometry.Close(Orbit(210f, 150f)), new Color(FlowCyan, .22f), 2f, true);
            foreach (var node in OrbitNodes)
            {
                var angle = Mathf.DegToRad(node.Angle);
                var x = Mathf.Round(CenterX + node.Rx * Mathf.Cos(angle) - NodeSize * .5f);
                var y = Mathf.Round(CenterY + node.Ry * Mathf.Sin(angle) - NodeSize * .5f);
                DrawColoredPolygon(UiGeometry.CutCorners(x, y, NodeSize, NodeSize, NodeCut), FlowCyan);
            }
            DrawColoredPolygon(
                UiGeometry.CutCorners(CenterX - CoreSize * .5f, CenterY - CoreSize * .5f, CoreSize, CoreSize, CoreCut), FlowCyan);
            DrawRect(new Rect2(640f, 470f, 640f, 2f), new Color(FlowCyan, .6f), true);
        }

        /// <summary>椭圆按 96 段采点：Godot 只有圆弧，设计稿两个轨道都是椭圆描边。</summary>
        private static Vector2[] Orbit(float rx, float ry)
        {
            const int steps = 96;
            var points = new Vector2[steps];
            for (var i = 0; i < steps; i++)
            {
                var angle = i * Mathf.Tau / steps;
                points[i] = new Vector2(CenterX + rx * Mathf.Cos(angle), CenterY + ry * Mathf.Sin(angle));
            }
            return points;
        }
    }

    /// <summary>Start 板的切角卡与动作键：Button 只承载点击与 hover，形状和 44px 图标自绘
    /// （StyleBox 表达不了「只切左上 + 右下」），26px 标题按设计稿字距另挂 Label。</summary>
    private sealed partial class StartCard : Button
    {
        private static readonly Vector2 GlyphAt = new(34f, 53f);
        // hover 统一过渡：卡面提亮（+8%）按 0..1 值 lerp；hover 变化才开 _Process，到终值自关。
        private HoverFade _hoverFade;

        public float Cut = 28f;
        public Color Fill = Colors.Transparent;
        public Color Border = Colors.Transparent;
        public float BorderWidth = 2f;
        public Color GlyphColor = Colors.White;
        public StartIcon Glyph = StartIcon.None;

        public override void _Ready()
        {
            var empty = new StyleBoxEmpty();
            foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" }) AddThemeStyleboxOverride(state, empty);
            MouseEntered += () => { SetProcess(true); };
            MouseExited += () => { SetProcess(true); };
            SetProcess(false);
        }

        public override void _Process(double delta)
        {
            // hover 统一过渡：MOTION OFF 时 Update 直接跳终值，SetProcess(false) 也在同帧生效。
            QueueRedraw();
            if (_hoverFade.Update(IsHovered(), delta)) return;
            SetProcess(false);
        }

        public override void _Draw()
        {
            EditorOverlayDraw.CutPanel(this, new Rect2(Vector2.Zero, Size), Cut,
                Fill.Lerp(Fill.Lightened(.08f), _hoverFade.Value), Border, BorderWidth);
            if (Glyph == StartIcon.Plus)
            {
                // 设计稿 ICON_PLUS_BIG：44 框里的两条 3.5px 实心笔。
                DrawLine(GlyphAt + new Vector2(22f, 8f), GlyphAt + new Vector2(22f, 36f), GlyphColor, 3.5f, true);
                DrawLine(GlyphAt + new Vector2(8f, 22f), GlyphAt + new Vector2(36f, 22f), GlyphColor, 3.5f, true);
            }
            else if (Glyph == StartIcon.Folder)
            {
                // 设计稿 ICON_FOLDER：M4 8h11l3 4h22v24H4z 的 2px 描边。
                DrawPolyline(UiGeometry.Close(new[]
                {
                    GlyphAt + new Vector2(4f, 8f), GlyphAt + new Vector2(15f, 8f), GlyphAt + new Vector2(18f, 12f),
                    GlyphAt + new Vector2(40f, 12f), GlyphAt + new Vector2(40f, 36f), GlyphAt + new Vector2(4f, 36f),
                }), GlyphColor, 2f, true);
            }
        }
    }

    private enum StartIcon { None, Folder, Plus }
}
