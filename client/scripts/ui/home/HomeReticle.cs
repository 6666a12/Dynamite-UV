using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Stationary etched orbit behind the real model; aligned with its view centre.</summary>
public partial class HomeReticle : Control
{
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw()
    {
        var hub = Size * .5f; const float r = 355.5f;
        DrawArc(hub,r,-.4f,1.72f,78,new Color("263c47"),1.5f,true);
        DrawArc(hub,r,2.06f,4.83f,88,new Color("2a3549"),1.5f,true);
        for (int i=0;i<24;i++)
        {
            var unit=Vector2.FromAngle(i*Mathf.Tau/24); bool major=i%3==0;
            DrawLine(hub+unit*360,hub+unit*(major?370.5f:364.5f),new Color(major?"36505a":"263b49"),1.5f,true);
        }
    }
}
