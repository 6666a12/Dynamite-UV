using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>The formal song library's left-hand instrument. Its six input keys emit intents;
/// it only displays values read back by SongSelect, never saves settings, starts audio or navigates.</summary>
internal sealed partial class SongChannelStrip : Control
{
    internal enum Port { Standard, Hardcore, Bleed, Mirror, Auto, Settings }
    internal event Action<Port>? Chosen;
    public required Func<UiMotionProfile> MotionProfileProvider { private get; init; }
    private const float S = 1.5f; // native 1920 space. Engraving coordinates below use a 280-wide strip.
    private static readonly Color Ink = new("EDF5FB"), Muted = new("A5B9C7"), Etch = new("526875"), Rule = new("2B3B48"), Cyan = new("65E2E2"), Deep = new("0B1721");
    private static readonly Rect2[] Regions = [new(40,188,137,67), new(177,188,143,67), new(40,275,280,65), new(40,347,280,65), new(40,419,280,65), new(40,532,280,66)];
    private static readonly string[] Ids = ["ModeStandard","ModeHardcore","ChannelBleed","ChannelMirror","ChannelAuto","AllSettings"];
    private static readonly string[] Names = ["STANDARD","HARDCORE","BLEED","MIRROR","AUTO","全部设置"];
    private static readonly Vector2[] Bus = [new(262,244),new(262,251),new(272,261),new(312,261),new(312,297)];
    private readonly ChannelStripKey[] _keys = new ChannelStripKey[6];
    private SignalReceipt _receipt = null!;
    private bool _hard, _bleed, _mirror, _auto, _armed, _connected;
    private float _cursor, _mirrorTravel, _bleedLight, _autoLight;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; Size = new(500, 920);
        for (var i = 0; i < Regions.Length; i++)
        {
            var ix = i; var box = Regions[i];
            var k = new ChannelStripKey { Name = Ids[i], Position = box.Position*S, Size = box.Size*S,
                AccessibilityName = Names[i] };
            _keys[i] = k; k.Activated += () => Chosen?.Invoke((Port)ix); k.InkChanged += QueueRedraw;
            AddChild(k);
        }
        _receipt = new SignalReceipt { Name = "HardcoreBleedReceipt" }; AddChild(_receipt);
        VisibilityChanged += () => { if (!IsVisibleInTree()) { _armed = false; _receipt.Cancel(); Snap(); SetProcess(false); } };
        SetProcess(false);
    }

    internal void ShowValues(bool hardcore, bool bleedPreference, bool mirror, bool auto, bool modeCommitted = false)
    {
        var wasHard = _hard;
        _hard = hardcore; _bleed = hardcore || bleedPreference; _mirror = mirror; _auto = auto;
        _keys[(int)Port.Bleed].Lock(_hard);
        _keys[(int)Port.Standard].AccessibilityName = _hard ? "STANDARD" : "STANDARD，当前模式";
        _keys[(int)Port.Hardcore].AccessibilityName = _hard ? "HARDCORE，当前模式" : "HARDCORE";
        for (int i = 0; i < 3; i++) _keys[i+2].AccessibilityName = $"{Names[i+2]} · {(Enabled(i)?"ON":"OFF")}{(i==0 && _hard?" · 锁定":"")}";
        if (!_hard) { _armed = false; _receipt.Cancel(); }
        if (!_connected || !MotionProfileProvider().AllowDirectionalMotion) Snap();
        if (_connected && modeCommitted && !wasHard && _hard)
        {
            if (MenuContactFeedback.Mode == UiMotionMode.Full && MotionProfileProvider().AllowDirectionalMotion) _armed = true;
            else if (MenuContactFeedback.Mode != UiMotionMode.Off) Send();
        }
        _connected = true; SetProcess(NeedsFrame); QueueRedraw();
    }
    private bool Enabled(int i) => i==0 ? _bleed : i==1 ? _mirror : _auto;
    private bool NeedsFrame => _armed || _cursor != (_hard ? 1 : 0) || _mirrorTravel != (_mirror ? 1 : 0) || _bleedLight != (_bleed ? 1 : 0) || _autoLight != (_auto ? 1 : 0);
    private void Snap() { _cursor = _hard?1:0; _bleedLight=_bleed?1:0; _mirrorTravel=_mirror?1:0; _autoLight=_auto?1:0; }
    private void Send() => _receipt.Emit(Cyan, Array.ConvertAll(Bus,p=>p*S));
    private static float Chase(float v,bool on,float weight) { var target=on?1f:0f; var next=Mathf.Lerp(v,target,weight); return Math.Abs(next-target)<.002f?target:next; }
    public override void _Process(double delta)
    {
        if (!MotionProfileProvider().AllowDirectionalMotion) Snap();
        else
        {
            var weight=1f-Mathf.Exp(-24f*(float)delta);
            _cursor=Chase(_cursor,_hard,weight); _mirrorTravel=Chase(_mirrorTravel,_mirror,weight);
            _bleedLight=Chase(_bleedLight,_bleed,weight); _autoLight=Chase(_autoLight,_auto,weight);
        }
        if (_armed && _cursor==1) { _armed=false; if (_hard && IsVisibleInTree()) Send(); }
        QueueRedraw(); SetProcess(NeedsFrame);
    }

    private void Text(float x,float y,string text,int px,Color color,bool cjk=false,bool bold=true) =>
        DrawString(cjk?UiFonts.Cjk:bold?MenuStyle.HeadingFont:UiFonts.Tech, new Vector2(x,y)*S, text, HorizontalAlignment.Left,-1,Mathf.RoundToInt(px*S),color);
    private void Wire(float x,float y,float u,float v,Color col,float w=1) => DrawLine(new Vector2(x,y)*S,new Vector2(u,v)*S,col,w*S,true);
    private void Rect(float x,float y,float width,float height,Color col,bool fill=true,float w=1) => DrawRect(new Rect2(x*S,y*S,width*S,height*S),col,fill,fill ? -1f : w*S);
    private void Marks(Port port)
    {
        var k=_keys[(int)port]; if(k is null || k.Unavailable) return;
        var b=Regions[(int)port]; var x=b.Position.X+1; var y=b.Position.Y+5;var x2=b.End.X-2;var y2=b.End.Y-5;
        var amount = Math.Max(k.Hover*.048f,k.Held?.070f:0f);
        if(amount>0) Rect(x+2,y,x2-x-4,y2-y,new Color(Cyan,amount));
        if (port is Port.Standard or Port.Hardcore) return; // Mode focus is drawn around the lettering, not the hit area.
        if(k.HasFocus() || k.Held) { var edge=new Color(Cyan,.60f); Wire(x,y,x+8,y,edge); Wire(x,y,x,y+8,edge); Wire(x2-8,y2,x2,y2,edge); Wire(x2,y2-8,x2,y2,edge); }
    }
    private void ModeLabel(Port port, float x, Color ink)
    {
        const float baseline = 226, gap = 4, tip = 4;
        const int px = 20;
        var label = Names[(int)port];
        var key = _keys[(int)port];
        if (key.HasFocus() || key.Held)
        {
            var width = MenuStyle.HeadingFont.GetStringSize(label, fontSize: Mathf.RoundToInt(px*S)).X / S;
            float left = x-gap, right = x+width+gap, top = baseline-18, bottom = baseline+3;
            var edge = new Color(Cyan,.60f);
            Wire(left+tip,top,left,top,edge); Wire(left,top,left,bottom,edge); Wire(left,bottom,left+tip,bottom,edge);
            Wire(right-tip,top,right,top,edge); Wire(right,top,right,bottom,edge); Wire(right,bottom,right-tip,bottom,edge);
        }
        Text(x,baseline,label,px,ink);
    }
    public override void _Draw()
    {
        if(!_connected) return;
        Wire(40,115,57,115,Cyan,2);Text(66,119,"QUICK SETTINGS",12,Cyan);Text(242,119,"CH  /  01–03",11,Muted);
        Text(40,161,"快捷设置",31,Ink,true);Wire(40,175,320,175,Rule);
        Marks(Port.Standard);Marks(Port.Hardcore);
        Text(42,199,"JUDGEMENT MODE",10,Muted);
        ModeLabel(Port.Standard,42,Muted.Lerp(Ink,1-_cursor)); ModeLabel(Port.Hardcore,208,Muted.Lerp(Ink,_cursor));
        Wire(43,239,317,239,Etch);for(var j=0;j<27;j++){var tx=45+10.4f*j;Wire(tx,240,tx,j%5==0?245:243,Rule);}
        var cursor=Mathf.Lerp(93,262,_cursor);Wire(cursor-20,238,cursor+20,238,Cyan,2);
        DrawColoredPolygon([new Vector2(cursor-4,240)*S,new Vector2(cursor+4,240)*S,new Vector2(cursor,245)*S],Cyan);
        Wire(40,265,320,265,Rule);for(int i=1;i<Bus.Length;i++) DrawLine(Bus[i-1]*S,Bus[i]*S,new Color("28414C"),1.5f,true);
        Row(0,"失误扣血",_bleedLight);Row(1,"谱面左右镜像",_mirrorTravel);Row(2,"开启后不写成绩",_autoLight);
        Wire(40,508,320,508,Etch);Marks(Port.Settings);Text(42,559,"全部设置",19,Ink,true);Text(43,581,"ALL SETTINGS",11,Muted);
        var ax=296+2*_keys[5].Hover;Wire(ax-9,559,ax+9,559,Cyan,1.5f);Wire(ax+3,553,ax+9,559,Cyan,1.5f);Wire(ax+3,565,ax+9,559,Cyan,1.5f);
    }
    private void Row(int i,string desc,float energy)
    {
        var n=i+2; var y=Regions[n].Position.Y;var on=Enabled(i);Marks((Port)n);
        Text(41,y+24,$"0{i+1}",10,Muted); Text(65,y+26,Names[n],23,Ink);Text(66,y+50,i==0&&_hard?"硬核模式强制开启":desc,13,Muted,true);
        Text(266,y+29,on?"ON":"OFF",15,on?Cyan:Muted);Rect(307,y+12,10,33,Deep);Rect(307,y+12,10,33,Etch,false);
        for(int j=0;j<6;j++) { var power=Mathf.Clamp(energy*6f-(5-j),0,1);Rect(309,y+14+5*j,6,3,Rule.Lerp(Cyan,power)); }
        if (i==0 && _hard)
        {
            DrawArc(new Vector2(242,y+20)*S,4*S,Mathf.Pi,Mathf.Tau,12,Cyan,1.5f*S,true);
            Wire(238,y+19,238,y+22,Cyan);Wire(246,y+19,246,y+22,Cyan);Rect(235,y+21,14,9,Cyan,false);Wire(242,y+24,242,y+27,Cyan);
        }
        if (i==1)
        {
            var a=Mathf.Lerp(228,245,_mirrorTravel);var b=Mathf.Lerp(245,228,_mirrorTravel);var col=Etch.Lerp(Cyan,_mirrorTravel);
            Wire(236.5f,y+12,236.5f,y+35,Rule);Wire(a,y+17,a,y+25,col,2);Wire(a,y+17,a+4,y+17,col);Wire(b,y+23,b,y+31,col,2);Wire(b-4,y+31,b,y+31,col);
        }
        if(i==2 && on) { Rect(216,y+14,42,19,new Color(Cyan,.085f));Text(221,y+28,"DEMO",11,Cyan); }
        if(i<2)Wire(40,y+68,320,y+68,Rule);
    }
}
