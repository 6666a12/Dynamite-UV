using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Engraved flat strike surface. It never handles input: the enclosing formal tap
/// rectangle, down-stamp and calibration clock remain the sole owners of sampling.</summary>
internal sealed partial class CalibrationStrikePlate : Control
{
    private readonly Color _trace = new("38545e");
    private ContactRim _rim = null!;
    internal bool Frozen { get; set; }
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        _rim = ContactRim.Attach(this, () => !Frozen);
    }
    internal void Touch(Vector2 local)
    {
        if (!Frozen && new Rect2(Vector2.Zero, Size).HasPoint(local)) _rim.Excite(local);
    }
    public override void _Draw()
    {
        var w=Size.X;var h=Size.Y;var mid=w*.5f;
        DrawColoredPolygon(UiGeometry.CutCorners(Size,8),new Color(Frozen?"14232d":"192d38"));
        DrawLine(new(12,1),new(w-12,1),Frozen?_trace:new Color("5b8289"),1.4f,true);
        for(var i=-1;i<=1;i++)
            DrawLine(new(18,h*.5f+9*i),new(w-18,h*.5f+9*i),_trace,1,true);
        for(var i=-4;i<=4;i++)
            DrawLine(new(mid+28*i,h*.5f-14),new(mid+28*i,h*.5f+14),_trace,1,true);
        DrawLine(new(mid-27,h-4),new(mid+27,h-4),new Color("85b9bd"),2,true);
        DrawLine(new(mid,h),new(mid,h+17),_trace,1.2f,true);
        DrawLine(new(mid,h+17),new(mid+12,h+27),_trace,1.2f,true);
    }
}
