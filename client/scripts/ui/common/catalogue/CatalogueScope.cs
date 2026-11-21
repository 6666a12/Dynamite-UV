using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Engraved scope plate used only under a catalogue cover. This is a fixed
/// instrument mark, not an audio visualisation, remote preview or generated cover art.</summary>
internal sealed partial class CatalogueScope : Control
{
    private Vector2[] _points = [];
    private Vector2 _lastSize;
    public override void _Draw()
    {
        var s = Size; if (s.X < 8 || s.Y < 8) return;
        DrawColoredPolygon(UiGeometry.CutCorners(s, 9), new Color("0B1620"));
        DrawRect(new Rect2(2, 2, s.X-4, s.Y-4), new Color("314351"), false, 1);
        for (var i=1;i<8;i++)
        {
            var q = s * (i/8f); var grid = new Color("1C2A36");
            DrawLine(new(q.X,4), new(q.X,s.Y-4),grid,1); DrawLine(new(4,q.Y),new(s.X-4,q.Y),grid,1);
        }
        if (_lastSize != s || _points.Length == 0)
        {
            _lastSize = s; _points = new Vector2[201];
            for (var i=0;i<_points.Length;i++)
            {
                var u=Mathf.Tau*i/(_points.Length-1);
                var q=new Vector2(Mathf.Cos(3*u+.15f)*.76f,Mathf.Sin(4*u)*.73f).Rotated(-.38f);
                _points[i]=s*.5f+q*(Mathf.Min(s.X,s.Y)*.41f);
            }
        }
        DrawPolyline(_points,new Color(CatalogueCardFace.Accent,.12f),7,true);
        DrawPolyline(_points,CatalogueCardFace.Accent,2.2f,true);
        DrawString(UiFonts.Tech, new(14,26), "SCOPE   /   NO COVER", HorizontalAlignment.Left, s.X-28, 14, MenuStyle.Secondary);
    }
}
