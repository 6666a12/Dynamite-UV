using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Single quiet mast, account entry and production identity.</summary>
internal static class HomeUtilityRow
{
    public static void Build(Node parent, string buildDescription, Action<Control, int> register)
    {
        var mast = new HomeMast { Name = "HomeMast", Size = new Vector2(1920, 120), MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(mast); register(mast, 0);
        var account=Game.OnlineSession.Connection.State.Account;
        var login = new Button { Name = "HomeAccount", Position = new(1525.5f,27), Size = new(319.5f,63),
            Text = account?.Username ?? "登录", TooltipText = account?.Username ?? "登录账号", Alignment=HorizontalAlignment.Left,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, FocusMode=Control.FocusModeEnum.All,
            MouseDefaultCursorShape=Control.CursorShape.PointingHand, Flat=true };
        login.AddThemeFontOverride("font",UiFonts.Cjk); login.AddThemeFontSizeOverride("font_size",24);
        login.AddThemeColorOverride("font_color",new Color("a4b7c2"));
        foreach(var state in new[]{"normal","hover","pressed","focus"})
        {
            var skin=new StyleBoxFlat { BgColor = state=="hover"||state=="pressed" ? new Color("182c36") : Colors.Transparent,
                BorderWidthBottom = state=="focus" ? 3 : 1, BorderColor = state=="focus" ? new Color("70e1e0") : new Color("30454f"),
                ContentMarginLeft=47, ContentMarginRight=60 };
            login.AddThemeStyleboxOverride(state,skin);
        }
        login.Draw += () => {
            var c=new Color("8cabb6"); login.DrawArc(new(21,21),6,0,Mathf.Tau,24,c,1.8f,true);
            login.DrawArc(new(21,42),10,Mathf.Pi,Mathf.Tau,24,c,1.8f,true);
            login.DrawLine(new(272,42),new(290,24),c,2f,true);login.DrawPolyline([new(278,24),new(290,24),new(290,36)],c,2f,true);
        };
        login.Pressed += () => TransitionDirector.Navigate(UiRoutes.Online,TransitionKind.Standard,"CONNECT","ACCOUNT");
        parent.AddChild(login);register(login,6);
        Caption(parent,register,"CoreDesignation","MK VI",new(1690,908),new(145,48),UiFonts.TechBold,19,new("b49bff"),HorizontalAlignment.Right,5);
        Caption(parent,register,"BuildIdentity",buildDescription,new(675,1015),new(1168,28),UiFonts.Tech,14,new("8297a4"),HorizontalAlignment.Right,6);
    }
    private static void Caption(Node parent, Action<Control,int> register, string name, string text, Vector2 p,
        Vector2 size, Font font, int px, Color col, HorizontalAlignment align, int order)
    {
        var label=new Label { Name=name, Text=text, Position=p, Size=size, HorizontalAlignment=align,
            VerticalAlignment=VerticalAlignment.Center, MouseFilter=Control.MouseFilterEnum.Ignore };
        UiLabels.Apply(label,font,px,col);parent.AddChild(label);register(label,order);
    }
}

internal sealed partial class HomeMast : Control
{
    public override void _Draw()
    {
        for(int i=0;i<3;i++)
        {
            var p=new Vector2(79.5f+10.5f*i,58.5f-4.5f*i);
            DrawColoredPolygon([p,p+new Vector2(6,-3),p+new Vector2(6,18),p+new Vector2(0,21)],new Color(i==2?"65e2e2":"7e99a9"));
        }
        DrawLine(new(78,112.5f),new(1842,112.5f),new Color("2b3a46"),1.5f,true);
        DrawLine(new(78,112.5f),new(201,112.5f),new Color("65e2e2"),3f,true);
    }
}
