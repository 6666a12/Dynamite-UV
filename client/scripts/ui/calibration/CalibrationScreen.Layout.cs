using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Ui;

public partial class CalibrationScreen
{
    // The timing geometry below is shared with the original formal calibration. The strike
    // plate is an additional visual surface within that SAME hit region, not a smaller replacement.
    private const float StageX = 460f, StageWidth = 760f, LaneWidth = 274f, LaneTop = 140f, LaneBandHeight = 781f;
    private const float Horizon1Y = 300f, Horizon2Y = 640f, NoteHeight = CalibrationStage.NoteHeight;
    private static readonly float JudgeLineY = GameplayStageGeometry.CenterLineY;
    private static readonly float LaneX = StageX + (StageWidth-LaneWidth)*.5f;
    private static Rect2 TapZone => new(StageX,LaneTop,StageWidth,LaneBandHeight);
    private const float PhaseSegWidth=65f, PhaseSegCurrentWidth=92f;
    private const float PanelInnerX=1286f, PanelInnerWidth=550f;
    private const float ProgressTrackWidth=544f, ProgressTrackHeight=6f;
    private const float StripX=54f, StripY=948f, StripWidth=1812f, StripHeight=111f, StripCut=8f;
    private const float StripLabelX=66f, StripLabelY=34f, AxisX=336f, AxisWidth=1452f, AxisCenterY=45f, AxisLabelY=30f;
    private static readonly Color CalInk = new("e7f7fc"), CalMuted = new("98afbb"), CalTrace = new("293e4c"), CalTeal = new("75ebe7"), CalViolet = new("c2afff");

    private Node2D AddGroup(string name) { var group=new Node2D { Name=name };AddChild(group);return group; }
    private void BuildHeader()
    {
        var back=new CutButton { Name="CalibrationBack", Position=new(60,26), Size=new(87,60), Cut=8, LowBorder=true,
            Text="←",FontSize=29,Accent=CalTeal,AccessibilityName="返回设置" };
        back.Pressed+=NavigateBack;_headerRoot.AddChild(back);
        AddLabel(_headerRoot,173,28,331,54,"Dynamite Universe",35,CalInk,true);
        Rule(_headerRoot,510,39,2,37);AddLabel(_headerRoot,536,36,180,39,"校准",21,CalMuted);
        Rule(_headerRoot,60,110,1800,2);
        RegisterChildren(_headerRoot,EnterGroup.Header);
    }
    private void BuildPhaseBlock()
    {
        _phaseTag=AddLabel(_phaseRoot,64,159,355,27,"等待开始",18,CalMuted);_phaseTag.Name="CalibrationPhaseStatus";
        _phaseTitle=AddLabel(_phaseRoot,63,195,386,66,"节拍校准",48,CalInk);_phaseTitle.Name="CalibrationTitle";
        AddLabel(_phaseRoot,66,266,380,51,"120 BPM   ·   4/4",24,CalTeal,true);
        _phaseGuide=AddLabel(_phaseRoot,66,334,378,60,"每小节第一拍，音符压线时击打。",21,CalInk);
        AddLabel(_phaseRoot,66,401,375,38,"前两颗音符热身，不计入样本",19,CalMuted);
        for(var i=0;i<_phaseSegments.Length;i++)
            _phaseSegments[i]=Rule(_phaseRoot,66+i*102,475,PhaseSegWidth,4);
        AddLabel(_phaseRoot,66,544,355,43,"当前偏移",22,CalMuted);
        _currentOffsetValue=AddLabel(_phaseRoot,63,580,380,83,"+0 ms",49,CalInk,true);
        RegisterChildren(_phaseRoot,EnterGroup.PhaseBlock);
    }
    private void BuildStage()
    {
        _stage=new CalibrationStage { Name="CalibrationStraightLane",FallSpeedMultiplier=()=>GameSession.Settings.FallSpeedMultiplier,
            Position=Vector2.Zero,Size=UiLayout.DesignSize,LaneX=LaneX,LaneWidth=LaneWidth,LaneTop=LaneTop,LaneBandHeight=LaneBandHeight,
            JudgeY=JudgeLineY,Horizon1Y=Horizon1Y,Horizon2Y=Horizon2Y,StageX=StageX,StageWidth=StageWidth,MouseFilter=Control.MouseFilterEnum.Ignore };
        _stageRoot.AddChild(_stage);
        _strike=new CalibrationStrikePlate { Name="CalibrationStrikePlate",Position=new(653,JudgeLineY+14f),Size=new(374,44) };
        _stageRoot.AddChild(_strike);
        _pulseInstruction=AddLabel(_stageRoot,66,680,370,43,"击打试打区开始采样",19,CalMuted);
        _hitReadout=new CalibrationHitReadout { Name="CalibrationHitReadout",Size=UiLayout.DesignSize,MouseFilter=Control.MouseFilterEnum.Ignore };
        _stageRoot.AddChild(_hitReadout);
        AddLabel(_hitReadout,1022,736,182,38,"本次击打",19,CalMuted);
        _lastHitValue=AddLabel(_hitReadout,1017,775,137,68,"—",46,CalMuted,true);
        _lastHitUnit=AddLabel(_hitReadout,1158,802,61,38,"ms",21,CalMuted);
        AddLabel(_hitReadout,1022,874,170,38,"早 −   /   晚 +",16,CalMuted);
        _frozenTag=AddLabel(_stageRoot,1022,875,175,38,"采样已完成",18,CalViolet);
        _frozenTag.Visible=false;
        RegisterChildren(_stageRoot,EnterGroup.Stage);
    }
    private void BuildStatsPanel()
    {
        _statsContent=new Control { Name="CalibrationLivePanel",Size=UiLayout.DesignSize,MouseFilter=Control.MouseFilterEnum.Ignore };
        _panelRoot.AddChild(_statsContent);RegisterEntrance(_statsContent,EnterGroup.Panel);
        AddDivider(_statsContent,193);
        AddLabel(_statsContent,PanelInnerX,212,420,57,"有效样本",30,CalInk);
        _progressCount=AddLabel(_statsContent,PanelInnerX-2,269,126,117,"00",86,CalInk,true);_progressCount.Name="CalibrationSampleCount";
        _targetValue=AddLabel(_statsContent,PanelInnerX+132,309,197,64,"/ 16",38,CalMuted,true);_targetValue.Name="CalibrationSampleTarget";
        _targetMinus=Step(_statsContent,"CalibrationTargetMinus",1661,"−",-1);
        _targetPlus=Step(_statsContent,"CalibrationTargetPlus",1752,"+",1);
        Rule(_statsContent,PanelInnerX,387,ProgressTrackWidth,ProgressTrackHeight);
        _progressFill=Rule(_statsContent,PanelInnerX,387,0,ProgressTrackHeight,CalTeal);
        AddLabel(_statsContent,PanelInnerX,442,295,61,"偏差中位数",25,CalInk);
        _medianValue=AddLabel(_statsContent,1585,441,248,67,"—",36,CalTeal,true,HorizontalAlignment.Right);_medianValue.Name="CalibrationMedian";
        AddLabel(_statsContent,PanelInnerX,521,295,61,"标准差",25,CalInk);
        _spreadValue=AddLabel(_statsContent,1585,520,248,67,"—",36,CalInk,true,HorizontalAlignment.Right);_spreadValue.Name="CalibrationSpread";
        AddDivider(_statsContent,625);AddLabel(_statsContent,PanelInnerX,641,305,61,"建议偏移",22,CalMuted);
        _suggestedValue=AddLabel(_statsContent,1585,639,248,68,"—",34,CalTeal,true,HorizontalAlignment.Right);
        _startStop=new CutButton {Name="CalibrationStart",LowBorder=true,Position=new(PanelInnerX,750),Size=new(320,84),
            Cut=8,Text="开始采样",FontSize=27,Accent=CalTeal};
        _startStop.Pressed+=()=>
        {
            if(_navigating||_localTransitionBusy||TransitionDirector.IsBusy||_swapPhase!=SwapPhase.Idle)return;
            if(_sampling.Phase==SamplingPhase.Ready)RegisterHit(SongPositionSeconds());
            else if(_sampling.Phase==SamplingPhase.Sampling)RestartSampling();
        };
        _statsContent.AddChild(_startStop); Rule(_startStop,1,12,3,60,CalTeal);
    }
    private StepperButton Step(Control parent,string name,float x,string mark,int direction)
    {
        var b=new StepperButton {Name=name,Position=new(x,286),Size=new(72,68),Cut=8,Glyph=mark,
            MotionProfileProvider=()=>UiMotionProfile.For(GameSession.Settings.MotionMode)};
        b.Pressed+=()=>AdjustTargetSamples(direction);parent.AddChild(b);return b;
    }
    private void BuildResultPanel()
    {
        _resultContent=new Control {Name="CalibrationResultPanel",Size=UiLayout.DesignSize,MouseFilter=Control.MouseFilterEnum.Ignore,Visible=false};
        _panelRoot.AddChild(_resultContent);AddDivider(_resultContent,193);
        AddLabel(_resultContent,PanelInnerX,214,PanelInnerWidth,54,"有效样本",30,CalInk);
        _resultCount=AddLabel(_resultContent,PanelInnerX-2,269,PanelInnerWidth,117,"00",78,CalInk,true);_resultCount.Name="CalibrationFinalCount";
        Rule(_resultContent,PanelInnerX,387,ProgressTrackWidth,ProgressTrackHeight,CalViolet);
        _resultSuggestedSub=AddLabel(_resultContent,PanelInnerX,445,550,70,"—",25,CalInk);
        AddLabel(_resultContent,PanelInnerX,530,310,58,"建议偏移",26,CalMuted);
        _resultSuggestedValue=AddLabel(_resultContent,1532,535,301,76,"—",48,CalViolet,true,HorizontalAlignment.Right);
        _resultDelta=AddLabel(_resultContent,PanelInnerX,648,550,55,"—",20,CalMuted);
        _retryButton=ResultKey("CalibrationRetry","再测一次",1286,230,RestartSampling,false);
        _applyButton=ResultKey("CalibrationApply","应用偏移",1536,292,ApplySuggestedOffset,true);
        _discardLabel=new HoverLabel {Name="CalibrationDiscard",Position=new(PanelInnerX,852),Size=new(550,58),Text="放弃并返回",FontSize=20,
            MotionProfileProvider=()=>UiMotionProfile.For(GameSession.Settings.MotionMode),BaseColor=CalMuted,HoverColor=CalInk,Alignment=HorizontalAlignment.Left};
        _discardLabel.Pressed+=NavigateBack;_resultContent.AddChild(_discardLabel);
    }
    private CutButton ResultKey(string name,string label,float x,float w,Action act,bool apply)
    {
        var b=new CutButton { Name=name,Position=new(x,750),Size=new(w,84),LowBorder=true,Text=label,FontSize=26,Cut=8,
            StyleKind=apply?CutButton.ButtonStyle.Solid:CutButton.ButtonStyle.Outline,Accent=apply?CalViolet:CalTeal };
        b.Pressed+=()=>act();_resultContent.AddChild(b);return b;
    }
    private void RegisterChildren(Node parent,EnterGroup group)
    {foreach(var child in parent.GetChildren())if(child is Control control)RegisterEntrance(control,group);}
    private static void AddDivider(Node p,float y)=>Rule(p,PanelInnerX,y,PanelInnerWidth,2);
    private static ColorRect Rule(Node p,float x,float y,float w,float h,Color? color=null)
    {
        var q=new ColorRect {Position=new(x,y),Size=new(w,h),Color=color??CalTrace,MouseFilter=Control.MouseFilterEnum.Ignore};p.AddChild(q);return q;
    }
    private static Label AddLabel(Node p,float x,float y,float w,float h,string text,int px,Color color,bool bold=false,
        HorizontalAlignment align=HorizontalAlignment.Left)
    {
        var q=new Label {Position=new(x,y),Size=new(w,h),Text=text,HorizontalAlignment=align,
            VerticalAlignment=VerticalAlignment.Center,MouseFilter=Control.MouseFilterEnum.Ignore};
        UiLabels.Apply(q,text.Any(ch=>ch>0x2e80)?UiFonts.Cjk:bold?UiFonts.TechBold:UiFonts.Tech,px,color);
        p.AddChild(q);return q;
    }
}
