using Godot;

namespace DuxCommunity.Ui;

/// <summary>切角面板：样式稿的 clip-path 切角矩形（半透明深色底+描边）。</summary>
public partial class CutPanel : Control
{
    private float _cut = 18f;
    private Color _fill = UiFonts.Panel;
    private Color _border = UiFonts.Line;
    private float _borderWidth = 2f;

    public float Cut { get => _cut; set { _cut = value; Refresh(); } }
    public Color Fill { get => _fill; set { _fill = value; Refresh(); } }
    public Color Border { get => _border; set { _border = value; Refresh(); } }
    public float BorderWidth { get => _borderWidth; set { _borderWidth = value; Refresh(); } }

    public void Refresh()
    {
        if (IsInsideTree())
            QueueRedraw();
    }

    public override void _Draw()
    {
        var c = Mathf.Min(_cut, Mathf.Min(Size.X, Size.Y) * 0.5f);
        Vector2[] pts =
        {
            new(c, 0), new(Size.X, 0), new(Size.X, Size.Y - c),
            new(Size.X - c, Size.Y), new(0, Size.Y), new(0, c),
        };
        DrawColoredPolygon(pts, _fill);
        if (_borderWidth > 0f)
        {
            var closed = new Vector2[pts.Length + 1];
            pts.CopyTo(closed, 0);
            closed[^1] = pts[0];
            DrawPolyline(closed, _border, _borderWidth, true);
        }
    }
}
