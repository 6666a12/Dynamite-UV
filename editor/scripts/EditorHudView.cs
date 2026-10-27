using Godot;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;
using static Godot.Control;
using static DynamiteUniverse.Editor.EditorPageUi;

namespace DynamiteUniverse.Editor;

internal sealed class EditorHudView
{
    internal Control Root { get; }
    internal EditorHudView(Action backRequested) => Root = Build(backRequested);
    private Label _saveState = null!;
    // Edit 主态的裸舞台只有四角纯文字 HUD（docs/editor-ui-design.md §4.1）：
    // 右上 GRID/SNAP(/AUTO)、左下曲名 + 难度色名 + LV、右下时间 + BAR/BPM。
    private Label _hudGrid = null!;
    private Label _hudTitle = null!;
    private Label _hudDifficulty = null!;
    private Label _hudLevel = null!;
    private Label _hudTime = null!;
    private Label _hudBarBpm = null!;
    /// <summary>路径节点编辑态的顶中标签（设计稿 PATH_FIELD 的 `PATH EDIT`）。</summary>
    private Label _hudPathEdit = null!;
    // 设计稿的 HUD 字号带字距（GRID/SNAP 1px、BAR/BPM 1.5px），Godot 侧用 FontVariation 的
    // 字形间距表达；字距为 0 的那几行直接吃原始字体。
    // 正文一律 Medium（窗口 0.833 折 + Regular 偏细，见 EditorFonts）；TechBold 是强调字，不动。
    private static readonly Font HudFont = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 1 };
    // 设计稿的 ls1.5 用整数字形间距（FontVariation 只有整数像素）按仓库既有约定编码为 2：
    // HUD 右下的 BAR·BPM 行与路径编辑态的顶中标签都是这一档。
    private static readonly Font HudFontWide = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 2 };
    private static readonly Font PathEditFont = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 3 };

    /// <summary>Edit 主态的 HUD 层。裸舞台是 GameplayStageRenderer 的事，这一层只有四角纯文字：
    /// 右上 GRID/SNAP(/AUTO)、左下曲名 + 难度色名 + LV、右下游标时间 + BAR/BPM，顶中是路径节点
    /// 编辑态的 `PATH EDIT`；坐标取设计稿 Editor — Main / PATH_FIELD。
    /// 返回包页与保存状态压到左下角曲名行上方，不占舞台。</summary>
    private Control Build(Action backRequested)
    {
        var chrome = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var back = NewCutButton("‹ PACK", 32, 922, 170, 48);
        back.MouseFilter = Control.MouseFilterEnum.Stop; back.Pressed += backRequested; chrome.AddChild(back);
        _saveState = HudLabel(chrome, 220, 930, 1652, 32, EditorFonts.Medium, 15, FlowMuted, HorizontalAlignment.Right);

        _hudGrid = HudLabel(chrome, 672, 23, 1200, 26, HudFont, 12, FlowMuted, HorizontalAlignment.Right);
        // 设计稿 PATH_FIELD 的顶中标签：13px bold cyan ls3，中心 (960,36)，只在路径节点编辑态出现。
        _hudPathEdit = HudLabel(chrome, 360, 23, 1200, 26, PathEditFont, 13, FlowCyan, HorizontalAlignment.Center);
        _hudPathEdit.Text = "PATH EDIT";
        _hudPathEdit.Visible = false;

        var songRow = new HBoxContainer
        {
            Position = new Vector2(48, 1001), Size = new Vector2(1300, 30),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        songRow.AddThemeConstantOverride("separation", 8);
        chrome.AddChild(songRow);
        _hudTitle = HudRowLabel(songRow, UiFonts.TechBold, 18, UiFonts.Text);
        _hudDifficulty = HudRowLabel(songRow, UiFonts.TechBold, 13, FlowAmber);
        _hudLevel = HudRowLabel(songRow, EditorFonts.Medium, 13, FlowMuted);

        _hudTime = HudLabel(chrome, 672, 992, 1200, 28, UiFonts.TechBold, 18, UiFonts.Text, HorizontalAlignment.Right);
        _hudBarBpm = HudLabel(chrome, 672, 1022, 1200, 22, HudFontWide, 11, FlowMuted, HorizontalAlignment.Right);
        return chrome;
    }

    /// <summary>HUD 文字一律单行、不做自动换行：设计稿的四角标签宽度固定，换行会把角标顶出画布。
    /// <paramref name="designSize"/> 是设计稿字号，登记进注册表以便 UI SCALE 重放。</summary>
    private static Label HudLabel(Node parent, float x, float y, float w, float h, Font font, int designSize, Color color, HorizontalAlignment alignment)
    {
        var label = new Label
        {
            Position = new Vector2(x, y), Size = new Vector2(w, h), Text = string.Empty,
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", font);
        EditorFonts.ApplySize(label, designSize);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    /// <summary>曲名行里的标签：字号不同，靠容器高度 + 垂直居中保持同一基线。</summary>
    private static Label HudRowLabel(Node parent, Font font, int designSize, Color color) =>
        HudLabel(parent, 0, 0, 0, 0, font, designSize, color, HorizontalAlignment.Left);

    /// <summary>四角 HUD 的实时数据。所有数值都取运行时状态，设计稿上的 1/32、0.10、02:00.000
    /// 只是展示值：网格分母来自文档，吸附来自画布，BPM/Bar 来自当前差分的时间线。</summary>
    internal void Refresh(EditorDocument? document, AuthoringCanvas canvas, double time, bool auto)
    {
        if (_hudGrid is null || canvas is null) return;
        var grid = $"GRID 1/{canvas.GridDivisor} · SNAP {SnapStepText(canvas)}";
        if (auto) grid += " · AUTO";
        SetLabelText(_hudGrid, grid);
        // 进入路径节点编辑态（单选 Hold/Mixer，或双击插入的宽度拖拽）出标签，退出即消失。
        _hudPathEdit.Visible = canvas.PathEditActive;

        if (document is null)
        {
            SetLabelText(_hudTime, CursorTimeText(time));
            SetLabelText(_hudBarBpm, string.Empty);
            return;
        }

        var chart = document.SelectedChart;
        var tagChanged = SetLabelText(_hudTitle, document.Title);
        tagChanged |= SetLabelText(_hudDifficulty, DifficultyName(chart));
        tagChanged |= SetLabelText(_hudLevel, LevelTagText(chart));
        if (tagChanged) _hudDifficulty.AddThemeColorOverride("font_color", DifficultyColor(chart.Difficulty));

        SetLabelText(_hudTime, CursorTimeText(time));
        SetLabelText(_hudBarBpm, $"BAR {(long)Math.Floor(Math.Max(0d, canvas.CursorBar))} · {canvas.CursorBpm:0.0} BPM");
    }

    /// <summary>吸附被 Z（时间吸附）临时关闭时显示 FREE；X 把空间吸附降到细步长，显示细步长。
    /// 常规步长取设置真源（Settings / EDITOR / SNAP），细步长仍是编辑器常量 0.01。</summary>
    private string SnapStepText(AuthoringCanvas canvas) => !canvas.SnapEnabled || !canvas.TimeSnapOverride
        ? "FREE"
        : (canvas.SpaceSnapOverride ? EditorSettings.Current.SnapStep : EditorGeometry.FineCenterSnap)
            .ToString("0.00", CultureInfo.InvariantCulture);

    internal void RefreshSaveState(EditorDocument? document, string? saveError)
    {
        if (_saveState is null) return;
        string text;
        var color = FlowMuted;
        if (document is null) text = "";
        else if (saveError is not null) { text = saveError; color = new Color("ff879f"); }
        else if (document.IsUnpersisted) text = "MEMORY DRAFT · UNSAVED";
        else text = (document.HasUnsavedChanges ? "UNSAVED CHANGES · " : "SAVED · ") + document.PackageDirectory;
        if (_saveState.Text == text) return;
        _saveState.Text = text;
        _saveState.AddThemeColorOverride("font_color", color);
    }
}
