using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>The wiring is stationary, except for its three leads which physically start at the
/// currently projected ports. A receipt travels on the same polyline as the passive conductor.
/// It continues briefly behind the existing director during its cover, never delays navigation.</summary>
internal sealed partial class HomePortWiring : Control
{
    private static readonly Color Guide = new("39545e"), Terminal = new("7e99a9"), Live = new("65e2e2");
    private static readonly Vector2 Jack = new Vector2(852, 706.5f)-HomeActions.Origin;
    private static readonly Vector2[] Spine = [Jack, new(843,193.5f), new(928.5f,108), new(1021.5f,108), new(1278,15)];
    private HomeActionTile[] _ports = [];
    private CatalogueDrum? _drum;
    private ScrollContainer? _scroll;
    private Vector2[] _path = [];
    private float _elapsed, _length;
    private const float Span = .19f;

    public override void _Ready() => SetProcess(false);
    public void Bind(CatalogueDrum drum, ScrollContainer scroll, HomeActionTile[] ports)
    {
        _drum = drum; _scroll = scroll; _ports = ports;
        scroll.GetVScrollBar().ValueChanged += ScrollChanged;
        foreach (var tile in ports) tile.ItemRectChanged += QueueRedraw;
        QueueRedraw();
    }
    private void ScrollChanged(double _) => QueueRedraw();
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_scroll)) _scroll!.GetVScrollBar().ValueChanged -= ScrollChanged;
        foreach (var tile in _ports) if (GodotObject.IsInstanceValid(tile)) tile.ItemRectChanged -= QueueRedraw;
    }
    private Vector2 InSheet(HomeActionTile tile, Vector2 local) =>
        _scroll!.GetGlobalTransform().AffineInverse() * (tile.GetGlobalTransform()*local);
    private Vector2 Display(Vector2 sheet) => GetGlobalTransformWithCanvas().AffineInverse() * _drum!.ProjectPoint(sheet);

    public void Acknowledge(int i)
    {
        if (_drum is null || MenuContactFeedback.Mode == UiMotionMode.Off) return;
        var row = _ports[i]; var material = InSheet(row, row.Contact);
        // FollowFocus normally reveals the row. At an edge use the visible section of the
        // rail; never start a stray beam outside the band on keyboard activation.
        material.Y = Mathf.Clamp(material.Y, 2, Size.Y-2);
        var pad = Display(material); var at = InSheet(row,row.LastActivation);
        at.Y = Mathf.Clamp(at.Y, 2, Size.Y-2);
        _path = [Display(at), pad, new(Jack.X,pad.Y), ..Spine];
        _elapsed = 0; _length = 0;
        for (int j=1; j<_path.Length; j++) _length += _path[j-1].DistanceTo(_path[j]);
        SetProcess(true); QueueRedraw();
    }
    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed >= Span || MenuContactFeedback.Mode == UiMotionMode.Off) { _path = []; SetProcess(false); }
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (_scroll is null || _drum is null) return;
        DrawLine(new(Jack.X,4),new(Jack.X,Size.Y-4),Guide,1.5f,true);
        DrawPolyline(Spine,Guide,2f,true);
        DrawRect(new Rect2(Jack-new Vector2(3,5),new Vector2(6,10)), Terminal);
        foreach(var port in _ports)
        {
            var mat=InSheet(port,port.Contact);
            if (mat.Y < 0 || mat.Y >= Size.Y) continue;
            var pad=Display(mat);
            DrawLine(pad,new(Jack.X,pad.Y),Guide,1.5f,true);
            DrawRect(new Rect2(Jack.X-2.5f,pad.Y-2.5f,5,5),Terminal);
        }
        if (_path.Length==0) return;
        var t=Mathf.Clamp(_elapsed/Span,0,1);
        if (MenuContactFeedback.Mode==UiMotionMode.Full)
        {
            var head=_length*Mathf.SmoothStep(0,1,Mathf.Min(t/.59f,1));
            float tail=Mathf.Min(head,160f), fade=1-Mathf.SmoothStep(.64f,1,t);
            for(int j=0;j<18 && tail>0;j++)
            {
                var a=Along(head-tail*j/18);var b=Along(head-tail*(j+1)/18);
                DrawLine(b,a,new Color(Live,fade*(1-j/18f)*.94f),3f,true);
            }
        }
        else DrawLine(_path[1]-new Vector2(6,0),_path[1]+new Vector2(5,0),new Color(Live,1-t),2f,true);
        var light=MenuContactFeedback.Mode==UiMotionMode.Full ? Mathf.SmoothStep(.43f,.59f,t)*(1-Mathf.SmoothStep(.65f,1,t)) : .4f*(1-t);
        if(light>.01f) DrawCircle(Spine[^1],6,new Color(Live,light*.8f),true,-1,true);
    }
    private Vector2 Along(float d)
    {
        for(int i=1;i<_path.Length;i++)
        {
            var len=_path[i-1].DistanceTo(_path[i]);if(len<.001f)continue;
            if(d<=len) return _path[i-1].Lerp(_path[i],d/len);d-=len;
        }
        return _path[^1];
    }
}
