using Godot;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;
using static Godot.Control;
using static DynamiteUniverse.Editor.EditorPageUi;

namespace DynamiteUniverse.Editor;


/// <summary>Edit 主态的切角动作键（`‹ PACK`）：形状与 hover/pressed 自绘，
/// 切角 10 = 设计稿底部操作条那一档。hover 提亮走统一过渡（0..1 lerp）。</summary>
internal sealed partial class EditCutButton : Button
{
    private const float Cut = 10f;
    private const float HoverLift = .12f;
    // hover 统一过渡：accent 提亮按 0..1 值 lerp；hover 变化才开 _Process，到终值自关。
    private HoverFade _hoverFade;

    public override void _Ready()
    {
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            AddThemeStyleboxOverride(state, empty);
        // Button 的 hover 状态变了 Godot 会自行重绘文字态，但形状画在本层 _Draw：
        // hover 值要靠 _Process 推进，进/出 hover 都开，到终值自关（不空转）。
        MouseEntered += () => SetProcess(true);
        MouseExited += () => SetProcess(true);
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        // MOTION OFF 时 Update 直接跳终值，同一帧 SetProcess(false) 生效，无过渡残留。
        QueueRedraw();
        if (_hoverFade.Update(IsHovered(), delta)) return;
        SetProcess(false);
    }

    public override void _Draw()
    {
        // 按钮的文字与状态色全由这里决定，所以字体颜色覆盖也要齐全（默认主题的 hover/pressed 色不同）。
        var mode = GetDrawMode();
        var pressed = mode is DrawMode.Pressed or DrawMode.HoverPressed;
        var accent = UiFonts.Cyan.Lerp(UiFonts.Cyan.Lightened(HoverLift), _hoverFade.Value);
        EditorOverlayDraw.CutPanel(this, new Rect2(Vector2.Zero, Size), Cut,
            pressed ? new Color(UiFonts.Cyan, .28f) : new Color(UiFonts.PanelHover, .85f), accent, 2f);
    }
}
