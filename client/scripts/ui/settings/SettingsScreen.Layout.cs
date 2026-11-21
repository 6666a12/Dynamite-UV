using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Ui;

public partial class SettingsScreen
{
    // Native 1920×1080 adaptation of the shallow-arc station. Content and footer never bend.
    private const float ColumnLeft=519, ColumnRight=1842, End=1829, RowHeight=127.5f;
    private static float RowY(int index) => 434 + index*RowHeight;
    private static Color Teal => SettingsSectionRail.Teal;
    private static Color Rule => SettingsSectionRail.Rule;

    private void BuildTopBar()
    {
        var top = new Control { Name="SettingsHeader", Size=new(1920,120), MouseFilter=Control.MouseFilterEnum.Ignore };
        AddChild(top); _enterItems.Add(top);
        var back = new CutButton { Name="SettingsBack", Position=new(60,26), Size=new(87,60), Cut=8,
            LowBorder=true, Text="←", FontSize=29, Accent=Teal, AccessibilityName="返回首页" };
        back.Pressed += Back; top.AddChild(back);
        Text(top,"Dynamite Universe",173,56,330,52,35,SettingsSectionRail.Ink,true);
        Line(top,510,39,2,37,Rule); Text(top,"设置",536,56,155,38,21,SettingsSectionRail.Muted);
        Line(top,60,110,1800,2,Rule);
    }

    private void BuildTabs()
    {
        var pane = new Control { Name="SettingsNavigation", Size=new(444,750), MouseFilter=Control.MouseFilterEnum.Ignore };
        AddChild(pane); _enterItems.Add(pane);
        Text(pane,"系统设置",72,214,370,71,48,SettingsSectionRail.Ink);
        Line(pane,72,285,366,2,Rule);
        _rail = new SettingsSectionRail { Name="SettingsSectionRail", Position=new(72,305), Size=new(368,424),
            CanChoose=()=>_built && !_localMotionBusy && !TransitionDirector.IsBusy };
        _rail.Chosen += n => SelectTab((SettingsTab)n, true);
        pane.AddChild(_rail);
        _sectionSignal = new SignalReceipt { Name="SettingsSectionReceipt", ZIndex=2 }; AddChild(_sectionSignal);
    }

    private void BuildContent()
    {
        _monitor = new SettingsChannelMonitor { Name="SettingsChannelMonitor" }; AddChild(_monitor);
        for (var i=0;i<_tabRoots.Length;i++)
        {
            var root=new Control { Name="SettingsContent"+SettingsSectionRail.Sections[i], Size=UiLayout.DesignSize,
                Visible=false, MouseFilter=Control.MouseFilterEnum.Ignore };
            AddChild(root); _tabRoots[i]=root;
            var head = new Control { Position=new(ColumnLeft,176), Size=new(ColumnRight-ColumnLeft,147), MouseFilter=Control.MouseFilterEnum.Ignore };
            root.AddChild(head); _enterItems.Add(head);
            Text(head,SettingsSectionRail.Titles[i],0,53,1150,81,52,SettingsSectionRail.Ink);
            Line(head,0,144,ColumnRight-ColumnLeft,2,Rule);
            Line(head,0,144,198,3,i==3?SettingsSectionRail.Violet:Teal);
        }
        BuildGameplayTab(_tabRoots[0]); BuildAudioTab(_tabRoots[1]); BuildDisplayTab(_tabRoots[2]); BuildEffectsTab(_tabRoots[3]);
    }

    private void BuildGameplayTab(Control root)
    {
        var s=GameSession.Settings;
        AddStepper(Row(root,0,"判定偏移"),()=> $"{s.TimingOffsetMs:+0;-0;+0} ms",
            delta=>s.TimingOffsetMs=Math.Clamp(s.TimingOffsetMs+delta*TimingStepMs,-300,300),"JudgeOffset");
        AddStepper(Row(root,1,"音符落速"),()=> $"{s.FallSpeedMultiplier:F1}×",
            delta=>s.FallSpeedLevel=Math.Clamp(s.FallSpeedLevel+delta,1,20),"FallSpeed");
        AddToggle(Row(root,2,"判定浮字"),()=>s.JudgeTextEnabled,()=>s.JudgeTextEnabled=!s.JudgeTextEnabled);
        AddSegments(Row(root,3,"判定模式"),ModeNames,()=> (int)s.GameplayMode,
            n=>s.GameplayMode=n==1?GameplayMode.Hardcore:GameplayMode.Standard,"Mode");
        Footer(root,"节拍校准   →",UiRoutes.Calibration);
    }
    private void BuildAudioTab(Control root)
    {
        var s=GameSession.Settings;
        AddSlider(Row(root,0,"曲目音量"),Teal,0,100,()=>s.MusicVolume,v=>s.MusicVolume=v,"Music");
        AddSlider(Row(root,1,"打击音量"),Teal,0,100,()=>s.HitVolume,v=>s.HitVolume=v,"Hit");
        AddSlider(Row(root,2,"界面音量"),Teal,0,100,()=>s.UiVolume,v=>s.UiVolume=v,"UI");
        Footer(root,"节拍校准   →",UiRoutes.Calibration);
    }
    private void BuildDisplayTab(Control root)
    {
        var s=GameSession.Settings;
        // The production settings model has a UI motion preference, not the lab's geometry dials.
        AddSegments(Row(root,0,"界面动效"),MotionNames,()=> (int)s.MotionMode,
            n=>s.MotionMode=n switch {1=>UiMotionMode.Reduced,2=>UiMotionMode.Off,_=>UiMotionMode.Full},"Motion");
        Footer(root);
    }
    private void BuildEffectsTab(Control root)
    {
        var s=GameSession.Settings;
        AddSegments(Row(root,0,"命中反馈"),EffectLevelNames,()=> (int)s.HitEffectLevel,n=>s.HitEffectLevel=(HitEffectLevel)n,"HitEffects");
        AddSlider(Row(root,1,"音符亮度"),Teal,80,140,()=>s.NoteBrightnessPercent,v=>s.NoteBrightnessPercent=v);
        AddSlider(Row(root,2,"闪光强度"),Teal,60,150,()=>s.FlashIntensityPercent,v=>s.FlashIntensityPercent=v);
        AddSegments(Row(root,3,"命中残影"),ResidualNames,()=>ResidualIndex(s.HitResidualMs),n=>s.HitResidualMs=ResidualValuesMs[n],"Residual");
        Footer(root,"音符配色   →",UiRoutes.NoteColors);
    }
    private static int ResidualIndex(int ms) => Array.IndexOf(ResidualValuesMs,ms) is var n && n>=0 ? n : 2;
    internal static string GameplayModeTag(GameplayMode mode) => mode==GameplayMode.Hardcore ? "HARD · 硬核判定（窗口 ×0.5）" : "Normal 判定窗口";

    private void Footer(Control root,string? text=null,string? target=null)
    {
        var reset = new FlatTextButton { Name="ResetDefaults", Position=new(ColumnLeft,948), Size=new(226,76), Text="恢复默认设置" };
        reset.Pressed+=()=>{ GameSession.Settings.ResetToDefaults(); Commit(); };
        root.AddChild(reset); _enterItems.Add(reset);
        if (text==null || target==null)return;
        var entry = KeyButton(root, target==UiRoutes.Calibration?"SettingsCalibrationEntry":"SettingsColorsEntry",
            text,1545,948,282,76,() =>
            {
                if (_localMotionBusy || TransitionDirector.IsBusy)return;
                _monitor.Stop(); GameSession.Settings.Save();
                TransitionDirector.Navigate(target,TransitionKind.Standard,"OPEN CHANNEL",target==UiRoutes.Calibration?"CALIBRATION":"NOTE COLORS");
            });
        _enterItems.Add(entry);
        Line(entry,1,11,3,54,Teal);
    }

    private Control Row(Control root,int index,string name)
    {
        var row=new Control { Name="SettingRow"+index, Position=new(ColumnLeft,RowY(index)-RowHeight*.5f),
            Size=new(ColumnRight-ColumnLeft,RowHeight), MouseFilter=Control.MouseFilterEnum.Ignore };
        root.AddChild(row); _enterItems.Add(row);
        Text(row,name,3,RowHeight*.5f,495,58,30,SettingsSectionRail.Ink);
        Line(row,0,RowHeight-1,ColumnRight-ColumnLeft,1.5f,Rule);
        return row;
    }
    private static void Line(Control parent,float x,float y,float w,float h,Color color) =>
        parent.AddChild(new ColorRect { Position=new(x,y),Size=new(w,h),Color=color,MouseFilter=Control.MouseFilterEnum.Ignore });
    private static Label Text(Control parent,string value,float x,float centerY,float w,float h,int px,Color color,
        bool bold=false,HorizontalAlignment align=HorizontalAlignment.Left)
    {
        var l=new Label { Position=new(x,centerY-h*.5f),Size=new(w,h),Text=value,HorizontalAlignment=align,
            VerticalAlignment=VerticalAlignment.Center,MouseFilter=Control.MouseFilterEnum.Ignore };
        UiLabels.Apply(l,value.Any(ch=>ch>127)?UiFonts.Cjk:bold?UiFonts.TechBold:UiFonts.Tech,px,color);
        parent.AddChild(l);return l;
    }
    private CutButton KeyButton(Control parent,string id,string title,float x,float y,float w,float h,Action act)
    {
        var b=new CutButton { Name=id,Position=new(x,y),Size=new(w,h),LowBorder=true,Cut=7,Text=title,
            FontSize=w<85?31:25,Accent=Teal,FocusMode=Control.FocusModeEnum.All,AccessibilityName=title };
        b.Pressed+=()=>{if(!_localMotionBusy && !TransitionDirector.IsBusy)act();};
        b.GuiInput+=e=>{if(b.HasFocus() && e is InputEventKey {Pressed:true,Echo:false,Keycode:Key.Enter or Key.KpEnter or Key.Space})
            {b.AcceptEvent();if(!_localMotionBusy && !TransitionDirector.IsBusy)act();}};
        parent.AddChild(b);return b;
    }
    private void AddStepper(Control row,Func<string> read,Action<int> change,string id)
    {
        const float w=74,h=68;var y=(RowHeight-h)*.5f;
        void Adjust(int sign){change(sign);Commit();}
        KeyButton(row,id+"Minus","−",1443-ColumnLeft,y,w,h,()=>Adjust(-1));
        KeyButton(row,id+"Plus","+",1755-ColumnLeft,y,w,h,()=>Adjust(1));
        var label=Text(row,read(),1530-ColumnLeft,RowHeight*.5f,204,60,34,Teal,true,HorizontalAlignment.Center);
        _valueBindings.Add(new ValueBinding(label,read,label.Position,label.Text));
    }
    private void AddToggle(Control row,Func<bool> read,Action toggle)
    {
        var control = new SettingsToggle { Name="JudgeText",MotionProfileProvider=()=>UiMotionProfile.For(GameSession.Settings.MotionMode),
            Position=new(1634-ColumnLeft,(RowHeight-68)*.5f),Size=new(195,68) };
        control.SetOn(read(),false);control.Pressed+=()=>{toggle();Commit();};row.AddChild(control);
        _stateRefreshers.Add(()=>control.SetOn(read(),true));
    }
    private void AddSlider(Control row,Color accent,int min,int max,Func<int> read,Action<int> write,string? bus=null)
    {
        var centerY=RowHeight*.5f;
        var control=new SettingsSlider { Name=(bus??"Effect")+"Slider",Position=new(1168-ColumnLeft,0),
            Size=new(SettingsSlider.TrackWidth+SettingsSlider.TrackHandle,RowHeight),Accent=accent,Min=min,Max=max };
        var percent=Text(row,$"{read()}%",1720-ColumnLeft,centerY,109,59,32,accent,true,HorizontalAlignment.Right);
        control.SetValue(read());row.AddChild(control);
        var receipt = new SignalReceipt { Name="ChannelReceipt",Position=control.Position };row.AddChild(receipt);
        void Light(int v)
        {
            var x=SettingsSlider.TrackHandle*.5f+SettingsSlider.TrackWidth*((v-min)/(float)Math.Max(1,max-min));
            var y=centerY+SettingsSlider.TrackHandle*.5f+7;
            receipt.Emit(accent,new Vector2(SettingsSlider.TrackHandle*.5f,y+8),new Vector2(Mathf.Max(SettingsSlider.TrackHandle*.5f,x-10),y+8),new Vector2(x,y));
        }
        control.Changed+=v=>{percent.Text=$"{v}%";write(v);Commit();Light(v);};
        _stateRefreshers.Add(()=>{var v=read();control.SetValue(v);percent.Text=$"{v}%";});
        if(bus!=null)KeyButton(row,"Monitor"+bus,"试听",1000-ColumnLeft,centerY-31,130,62,()=>{_monitor.Listen(bus);Light(read());});
    }
    private void AddSegments(Control row,string[] labels,Func<int> read,Action<int> write,string id)
    {
        var w = labels.Length==2 ? 196f : labels.Length==4 ? 138f : 162f;
        var width=labels.Length*w+(labels.Length-1)*9;
        var control=new SettingsSegments { Name=id,CellWidth=w,Position=new(End-ColumnLeft-width,(RowHeight-68)*.5f),Size=new(width,68) };
        control.Configure(labels,read());control.Chosen+=n=>{write(n);Commit();};row.AddChild(control);
        _stateRefreshers.Add(()=>control.SetSelected(read()));
    }
}
