using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>切角面板：样式稿的 clip-path 切角矩形（半透明深色底+描边）。</summary>
public partial class CutPanel : Control
{
    private float _cut = 18f;
    private Color _fill = UiFonts.Panel;
    private Color _border = UiFonts.Line;
    private float _borderWidth = 2f;
    private Vector2[]? _points;
    private Vector2[]? _closedPoints;

    public float Cut
    {
        get => _cut;
        set
        {
            _cut = value;
            _points = null;
            _closedPoints = null;
            Refresh();
        }
    }
    public Color Fill { get => _fill; set { _fill = value; Refresh(); } }
    public Color Border { get => _border; set { _border = value; Refresh(); } }
    public float BorderWidth { get => _borderWidth; set { _borderWidth = value; Refresh(); } }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            _points = null;
            _closedPoints = null;
        }
    }

    public void Refresh()
    {
        if (IsInsideTree())
            QueueRedraw();
    }

    public override void _Draw()
    {
        var c = Mathf.Min(_cut, Mathf.Min(Size.X, Size.Y) * 0.5f);
        Vector2[] pts = _points ??= UiGeometry.CutCorners(Size, c);
        DrawColoredPolygon(pts, _fill);
        if (_borderWidth > 0f)
        {
            var closed = _closedPoints ??= UiGeometry.Close(pts);
            DrawPolyline(closed, _border, _borderWidth, true);
        }
    }
}
