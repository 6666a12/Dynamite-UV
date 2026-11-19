using System.Text.Json;
using Avalonia.Controls;

namespace DynamiteUniverse.Editor.Shell;

internal static class PreviewClockVerification
{
    internal static void Run(Action<string, bool> check)
    {
        var pending = new Queue<Action>(); var delivered = new List<StageMessage>();
        var dispatch = new StageFrameDispatch(pending.Enqueue, delivered.Add);
        StageMessage Tick(double seconds, double bar, long clockRequest, bool playing, bool frame = true, long revision = 1) =>
            new("tick", JsonSerializer.SerializeToElement(new { second = seconds, bar, clockRequest, playing, frame, revision, playerSpeed=1, playbackRate=1 }));
        for (var i = 0; i < 600; i++) dispatch.Receive(Tick(i/60d,i/120d,0,true));
        check("Clock: 600 pending display frames use one dispatcher callback", pending.Count == 1);
        var pause = Tick(10,5,1,false,false); var resume = Tick(10,5,2,true,false);
        dispatch.Receive(pause);
        dispatch.Receive(new("key", JsonSerializer.SerializeToElement(new { key="Space" })));
        dispatch.Receive(resume); dispatch.Receive(Tick(10.01,5.005,2,true)); dispatch.Receive(Tick(10.02,5.01,2,true));
        while (pending.TryDequeue(out var callback)) callback();
        check("Clock: coalescing preserves pause/input/resume barriers and latest frame on each side", delivered.Count == 5 &&
            delivered[0].Data.GetProperty("second").GetDouble()==599/60d && delivered[1] == pause && delivered[2].Event=="key" &&
            delivered[3]==resume && delivered[4].Data.GetProperty("second").GetDouble()==10.02);

        var timeline=new TimelineView(); var timing=new TimingView(); var display=new TextBlock();
        var clock=new WorkbenchPreviewClock(timeline,timing,display,WorkbenchLocalizer.Shared);
        bool Apply(StageMessage m)=>clock.Accept(m.Data,1);
        void SamePosition(string label,double s,double b)
        {
            var stamp=TimeSpan.FromSeconds(s).ToString(@"mm\:ss\.fff");
            check("Clock: "+label,clock.Second==s && clock.Bar==b && timeline.CurrentBar==b &&
                display.Text!.Contains(stamp) && timing.FindControl<TextBlock>("PlayheadCaption")!.Text!.Contains(b.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)));
        }
        Apply(Tick(8,4,0,true));
        var requests=new List<(bool Playing,long Id)>();
        bool Send(bool play,long id) {requests.Add((play,id));return true;}
        clock.Toggle(Send);
        check("Clock: frames already in flight at pause cannot advance the pending display", requests.SequenceEqual([(false,1L)]) && !Apply(Tick(8.1,4.05,0,true)) && clock.Second==8);
        Apply(Tick(8.125,4.0625,1,false,false)); SamePosition("authoritative pause aligns transport, bar ruler and TIMING",8.125,4.0625);
        check("Clock: a delayed pre-pause sample cannot reopen playback", !Apply(Tick(8,4,0,true)) && !clock.Playing);
        clock.Seek(1,2,(_,id)=>id==2); clock.Seek(.5,1,(_,id)=>id==3);
        check("Clock: rapid backwards seeks ignore earlier replies", !Apply(Tick(2,1,2,false,false)) && Apply(Tick(1,.5,3,false,false)));
        SamePosition("backwards seek is allowed while paused",1,.5);
        check("Clock: old chart revision cannot overwrite current transport", !Apply(Tick(60,30,3,true,revision:0)) && clock.Second==1);
        clock.Toggle(Send); clock.Toggle(Send);
        check("Clock: quick play/pause uses requested intent, not stale acknowledged state", requests.TakeLast(2).SequenceEqual([(true,4L),(false,5L)]) &&
            !Apply(Tick(1,.5,4,true,false)) && Apply(Tick(1,.5,5,false,false)) && !clock.Playing);
        clock.ResetSession(); Apply(Tick(0,0,0,false,false));
        SamePosition("replacement stage starts a new request sequence",0,0);
    }
}
