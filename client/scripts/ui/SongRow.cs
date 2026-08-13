using Godot;
using DuxCommunity.Game;

namespace DuxCommunity.Ui;

/// <summary>
/// 选曲列表行（样式稿）：左对齐曲名+曲师，右侧彩色难度徽章（切角小块+等级数字）。
/// </summary>
public partial class SongRow : Control
{
    private ChartPack? _pack;
    private bool _selected;

    [Signal]
    public delegate void PressedEventHandler();

    public ChartPack? Pack { get => _pack; set { _pack = value; Refresh(); } }

    public bool Selected { get => _selected; set { _selected = value; Refresh(); } }

    private bool _hover;

    public override void _Ready()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _hover = true; Refresh(); };
        MouseExited += () => { _hover = false; Refresh(); };
    }

    public void Refresh()
    {
        if (IsInsideTree())
            QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        var pressed = e is InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true,
            } or InputEventScreenTouch { Pressed: true };
        if (pressed)
        {
            AcceptEvent();
            EmitSignal(SignalName.Pressed);
        }
    }

    public override void _Draw()
    {
        const float cut = 10f;
        Vector2[] pts =
        {
            new(cut, 0), new(Size.X, 0), new(Size.X, Size.Y - cut),
            new(Size.X - cut, Size.Y), new(0, Size.Y), new(0, cut),
        };
        var fill = _selected ? new Color("18244a")
            : _hover ? new Color(UiFonts.PanelHover, 0.6f)
            : new Color(0.06f, 0.08f, 0.16f, 0.35f);
        DrawColoredPolygon(pts, fill);
        if (_selected || _hover)
        {
            var closed = new Vector2[pts.Length + 1];
            pts.CopyTo(closed, 0);
            closed[^1] = pts[0];
            DrawPolyline(closed, _selected ? UiFonts.Cyan : UiFonts.Line, 2f, true);
        }
        if (_pack == null)
            return;

        // 左：曲名 + 曲师·谱师
        DrawString(UiFonts.Cjk, new Vector2(24, Size.Y * 0.44f), _pack.Title,
            HorizontalAlignment.Left, Size.X * 0.55f, 30, UiFonts.Text);
        DrawString(UiFonts.Cjk, new Vector2(24, Size.Y * 0.78f),
            $"{_pack.Artist} · 谱师 {_pack.Charter}",
            HorizontalAlignment.Left, Size.X * 0.55f, 18, UiFonts.Dim);

        // 右：难度徽章（从右往左排，giga 在最右）
        const float chipW = 46f, chipH = 32f, gap = 8f;
        var x = Size.X - 20f;
        var y = (Size.Y - chipH) * 0.5f;
        for (var i = _pack.Charts.Count - 1; i >= 0; i--)
        {
            var chart = _pack.Charts[i];
            var badge = chart.Level > 0
                ? chart.Level.ToString()
                : string.IsNullOrEmpty(chart.Diff) ? "?" : UiFonts.DiffName(chart.Diff)[..1];
            x -= chipW;
            var col = UiFonts.DiffColor(chart.Diff);
            const float cc = 6f;
            Vector2[] chip =
            {
                new(x + cc, y), new(x + chipW, y), new(x + chipW, y + chipH - cc),
                new(x + chipW - cc, y + chipH), new(x, y + chipH), new(x, y + cc),
            };
            DrawColoredPolygon(chip, col);
            DrawString(UiFonts.TechBold, new Vector2(x, y + chipH * 0.5f + 7f),
                badge, HorizontalAlignment.Center, chipW, 19, UiFonts.InkText);
            x -= gap;
        }
    }
}
