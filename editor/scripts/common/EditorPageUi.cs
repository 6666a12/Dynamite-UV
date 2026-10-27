using Godot;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;
using static Godot.Control;

namespace DynamiteUniverse.Editor;

internal static class EditorPageUi
{

    // Keep page chrome on the same palette as the game HUD. UiFonts is linked
    // from the client project in the editor csproj so both surfaces share the
    // same font, difficulty colors, panel fill and line colors.
    internal static readonly Color FlowBackground = UiFonts.Bg;
    internal static readonly Color FlowPanel = UiFonts.Panel;
    internal static readonly Color FlowCyan = UiFonts.Cyan;
    internal static readonly Color FlowMuted = UiFonts.Dim;
    internal static readonly Color FlowAmber = UiFonts.Hard;

    /// <summary>页底：背景 + 可选标题分隔线。Start 板按设计稿没有那条线。</summary>
    internal static Control NewPage(bool rule = true)
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = FlowBackground, MouseFilter = Control.MouseFilterEnum.Stop };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        page.AddChild(background);
        if (!rule) return page;
        var hairline = new ColorRect { Position = new Vector2(90, 118), Size = new Vector2(1545, 2), Color = new Color(FlowCyan, .45f), MouseFilter = Control.MouseFilterEnum.Ignore };
        page.AddChild(hairline);
        return page;
    }

    internal static Label AddFlowLabel(Node parent, float x, float y, float w, float h, string text, int designSize, Color color)
    {
        var label = LabelAt(x, y, w, h, text, designSize, color);
        parent.AddChild(label); return label;
    }

    /// <summary>编辑态的描边动作键：切角形状自绘（全编辑器统一的「只切左上 + 右下」语言，
    /// StyleBoxFlat 的圆角表达不了切角，Start 板的卡也是同一做法）。</summary>
    internal static EditCutButton NewCutButton(string text, float x, float y, float w, float h)
    {
        var button = new EditCutButton
        {
            Position = new Vector2(x, y), Size = new Vector2(w, h), Text = text,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        button.AddThemeFontOverride("font", EditorFonts.Medium);
        EditorFonts.ApplySize(button, 17);
        // 自绘按钮的四个状态色都显式给，避免默认主题的 hover/pressed 色把文字变白。
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, UiFonts.Text);
        return button;
    }

    /// <summary>页面的普通文字标签。<paramref name="designSize"/> 是设计稿字号，登记进
    /// <see cref="EditorFonts"/> 的注册表，UI SCALE 换档时统一重放。</summary>
    internal static Label LabelAt(float x, float y, float w, float h, string text, int designSize, Color color)
    {
        var label = new Label
        { Position = new Vector2(x, y), Size = new Vector2(w, h), Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", EditorFonts.Medium);
        EditorFonts.ApplySize(label, designSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    internal static Color DifficultyColor(V2Difficulty difficulty) => difficulty switch
    {
        V2Difficulty.Casual => UiFonts.Casual,
        V2Difficulty.Normal => UiFonts.Normal,
        V2Difficulty.Hard => UiFonts.Hard,
        V2Difficulty.Mega => UiFonts.Mega,
        V2Difficulty.Giga => UiFonts.Giga,
        V2Difficulty.Tech => EditorOverlayDraw.Tech,
        _ => UiFonts.Dim,
    };

    internal static string DifficultyName(EditableChart chart) => chart.Difficulty == V2Difficulty.Custom
        ? chart.DifficultyKey ?? "CUSTOM"
        : chart.Difficulty.ToString().ToUpperInvariant();

    internal static string LevelTagText(EditableChart chart) => chart.Unrated || chart.Level is null
        ? "· LV --"
        : $"· LV {chart.Level.Value.ToString(CultureInfo.InvariantCulture)}";

    internal static bool SetLabelText(Label label, string text)
    {
        if (label.Text == text) return false;
        label.Text = text;
        return true;
    }

    internal static string CursorTimeText(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0d, seconds)).ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
}
