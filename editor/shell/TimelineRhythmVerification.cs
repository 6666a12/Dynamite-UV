using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

internal static class TimelineRhythmVerification
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var chart = Example(); var view = new TimelineView { Height = 104 };
        var time = new WorkbenchTimeline(view); time.Refresh(chart); time.SetAudioDuration(100);
        var data = view.Rhythm!;
        var hold = data.ForTrack(EditorTrack.Center).Single(n => n.Type == V2NoteType.Hold);
        check("Rhythm: Hold spans head to tail and marks only authored inner judgements", hold.Start == 14 && hold.End == 16 && hold.Judgements.SequenceEqual([15d]));
        check("Rhythm: overview counts heads, not path turns or authored BarLine, and keeps simultaneous onsets", data.AllHits.Count == chart.Notes.Count - 1 && data.AllHits.Count(x => x == 12) == 2);
        check("Rhythm: different sides retain their independent patterns", data.ForTrack(EditorTrack.Left).Any(n => n.Start == 12.25) && data.ForTrack(EditorTrack.Center).All(n => n.Start != 12.25));
        var layout = new TimelineTrackLayout(data.ForTrack(EditorTrack.Center));
        var chord = layout.Lanes.Where(l => l.Heads.Contains(12)).ToArray();
        var body = layout.Lanes.Single(l => l.Type == V2NoteType.Hold).Bodies;
        check("Rhythm: simultaneous heads retain identical bar and distinct rows", chord.Length == 2 && chord[0].RelativeY != chord[1].RelativeY);
        check("Rhythm: a hold crossing the left edge is found even when its head is outside", body[TimelineTrackLayout.FirstBody(body,15)] == new TimelineSpan(14,16));
        check("Rhythm: entire uncharted recording maps through BPM changes and offset", view.MaximumBar == 88); // (100−2−20) seconds at 1 s/bar, starting at bar 10.
        view.Track = EditorTrack.Center; view.CurrentBar = 12;
        var local = view.DetailWindow;
        check("Rhythm: eight bars, follow at 1/4, clamp at head and tail", local.Start == 10 && local.End == 18 && local.FractionAt(12) == .25 &&
            TimelineBarWindow.Follow(0,88).Start == 0 && TimelineBarWindow.Follow(88,88).End == 88);
        var host = new Window { Width = 1040, Height = 104, Content = view, CanResize = false };
        host.Show(); host.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        try
        {
            var y = view.Bounds.Height * .65;
            var input = new Point(view.Bounds.Width * .62, y);
            var next = new Point(view.Bounds.Width * .81, y);
            var firstTarget = view.BarAtPoint(input); var secondTarget = view.BarAtPoint(next);
            var seeks = new List<double>(); view.SeekRequested += (_, bar) => { seeks.Add(bar); view.CurrentBar = bar; };
            host.MouseMove(input); host.MouseDown(input, MouseButton.Left); host.MouseMove(next);
            check("Rhythm: dragging below uses the original page even as seek changes the playhead", seeks.Count >= 2 && Math.Abs(seeks[0]-firstTarget)<1e-8 &&
                Math.Abs(seeks[^1]-secondTarget)<1e-8 && view.DetailWindow.Start == local.Start);
            host.MouseUp(next, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            check("Rhythm: releasing resumes follow", view.DetailWindow.Start != local.Start);
            var far = new Point(view.Bounds.Width * .80, view.OverviewHeight * .5);
            double expected = view.BarAtPoint(far); host.MouseMove(far); host.MouseDown(far,MouseButton.Left); host.MouseUp(far,MouseButton.Left);
            check("Rhythm: overview can jump beyond last authored note", expected > data.LastBar && Math.Abs(view.CurrentBar - expected)<1e-8);
            var wheel = 0d; view.WheelSteps += s => wheel += s;
            host.MouseWheel(next,new Vector(0,-1)); check("Rhythm: bottom wheel forwards signed stage navigation, not zoom", wheel == 1);
            if (Environment.GetEnvironmentVariable("DYNA_RHYTHM_PREVIEW_PNG") is { Length: > 0 } png)
            {
                view.CurrentBar = 12; view.InvalidateVisual(); host.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)view.Bounds.Width * 2,(int)view.Bounds.Height * 2),new Vector(192,192));
                bitmap.Render(view); bitmap.Save(png);
            }
        }
        finally { host.Close(); }
        var pack = Path.Combine(root, "song"); Verification.CreatePackage(pack);
        var window = new MainWindow(Path.Combine(root,"prefs"), () => { });
        try
        {
            window.Show(); await window.OpenPackageForVerification(pack); Dispatcher.UIThread.RunJobs();
            window.StageEventForVerification("ready", "{\"hwnd\":0}"); var revision = window.StageRevision;
            void Duration(long rev, double sec) => window.StageEventForVerification("timelineDuration",JsonSerializer.Serialize(new { revision=rev, seconds=sec }));
            var timeline = window.FindControl<TimelineView>("Timeline")!; Duration(revision, 160); Duration(revision-1, 2);
            check("Rhythm: shell accepts active recording length, discards stale duration", timeline.MaximumBar == 100);
            window.SeekForVerification(72); var document = window.CurrentDocument!; var version = document.ChangeVersion;
            window.StageEventForVerification("contextAction", "{\"kind\":\"track\",\"value\":\"Right\"}");
            check("Rhythm: native side menu switches bottom track without moving time or editing selection", timeline.Track == EditorTrack.Right &&
                timeline.Rhythm!.ForTrack(EditorTrack.Right).Count == 0 && window.CurrentBar == 72 && document.ChangeVersion == version);
            VerifyPlacedChord(window, timeline, check);
            window.ShortcutForVerification(Key.Up);
            check("Rhythm: keyboard side change follows the tool panel", timeline.Track == window.PlacementTrackForVerification && timeline.Track != EditorTrack.Right);
            VerifyExactTiming(window, document, check);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    private static void VerifyExactTiming(MainWindow window, EditorDocument document, Action<string, bool> check)
    {
        for (var i = 0; i < 3; i++) window.ShortcutForVerification(Key.C);
        var quarter = ExactBarTime.FromFraction(1, 4);
        window.SeekForVerification(.125);
        check("Rhythm: a half-grid seek and document snap both choose the later quarter", window.CurrentBar == .25 &&
            document.GridDivisor == 4 && document.Snap(ExactBarTime.FromFraction(1, 8)) == quarter);
        window.ShortcutForVerification(Key.C, KeyModifiers.Shift);
        var third = ExactBarTime.FromFraction(1, 3);
        window.SeekForVerification(.34);
        var timing = window.Timing;
        var chart = document.SelectedChart;
        var bpmTime = timing.FindControl<TextBox>("BpmTimeValue")!;
        var scrollTime = timing.FindControl<TextBox>("ScrollTimeValue")!;
        void Click(string name) => timing.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        timing.FindControl<NumericUpDown>("BpmValueInput")!.Value = 180;
        bpmTime.Text = "";
        Click("AddBpmButton");
        check("Rhythm: blank BPM time at a snapped playhead is exactly 1/3", document.GridDivisor == 3 &&
            chart.Bpms.Count == 2 && chart.Bpms[1].Time == third);
        bpmTime.Text = "1/3"; Click("AddBpmButton");
        check("Rhythm: explicit 1/3 cannot create a second BPM at the same beat", chart.Bpms.Count == 2);
        scrollTime.Text = "0"; Click("AddScrollButton");
        scrollTime.Text = ""; Click("AddScrollButton");
        check("Rhythm: blank Scroll time also preserves the exact grid point", chart.ScrollSpeeds.Any(s => s.Time == third));
        document.SnapEnabled = false;
        window.SeekForVerification(.2345673);
        bpmTime.Text = ""; Click("AddBpmButton");
        check("Rhythm: free seek remains continuous and only quantizes the committed timing event", window.CurrentBar == .2345673 &&
            chart.Bpms.Any(b => b.Time == ExactBarTime.FromFraction(234567, 1_000_000)));
        document.SnapEnabled = true;
        for (var i = 0; i < 4; i++) window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
    }

    private static void VerifyPlacedChord(MainWindow window, TimelineView view, Action<string, bool> check)
    {
        window.SeekForVerification(12);
        void Place(V2NoteType type, double bar, double end, double center) => window.StageEventForVerification("place",
            JsonSerializer.Serialize(new { type=(int)type, track=(int)EditorTrack.Right, bar, tailBar=end, center, width=.5, snap=true }));
        TimelineTrackLayout Layout() => new(view.Rhythm!.ForTrack(EditorTrack.Right));
        double Row(TimelineTrackLayout layout, V2NoteType type) => layout.Lanes.Single(l => l.Type == type && l.Heads.Contains(12)).RelativeY;
        Place(V2NoteType.Hold, 12, 14, 1); Place(V2NoteType.Drag, 12, 12, 2);
        var before = Layout(); var holdY = Row(before, V2NoteType.Hold); var dragY = Row(before, V2NoteType.Drag);
        Place(V2NoteType.Tap, 12, 12, 3);
        var after = Layout(); var ys = new[] { Row(after,V2NoteType.Hold), Row(after,V2NoteType.Drag), Row(after,V2NoteType.Tap) };
        check("Chord: actual third placement preserves all three starts, tail, scale and playhead", ys.Distinct().Count() == 3 &&
            view.Rhythm!.ForTrack(EditorTrack.Right).Single(n=>n.Type==V2NoteType.Hold).End == 14 && view.DetailWindow.Start == 10 && window.CurrentBar == 12);
        check("Chord: adding the third note does not displace the first two marks or sustain", ys[0] == holdY && ys[1] == dragY);
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)view.Bounds.Width,(int)view.Bounds.Height),new Vector(96,96));
        bitmap.Render(view);
        if (Environment.GetEnvironmentVariable("DYNA_CHORD_PREVIEW_PNG") is { Length: >0 } png) bitmap.Save(png);
        for (int i=0; i<3; i++) window.ShortcutForVerification(Key.Z,KeyModifiers.Control);

        // Check the glyphs for every combination of three drawable kinds, including both long types.
        // BarLine is intentionally not an onset. Use one actual control and invalidate it by editing its data.
        var kinds = Enum.GetValues<V2NoteType>().Where(t => t != V2NoteType.BarLine).ToArray();
        var trials = 0; var allVisible = true;
        for (int a=0; a<kinds.Length; a++)
        for (int b=a+1; b<kinds.Length; b++)
        for (int c=b+1; c<kinds.Length; c++)
        {
            foreach (var t in new[] { kinds[a], kinds[b], kinds[c] }) Place(t, 12, 13, 2.5);
            var positions = Layout().Lanes.Where(l => l.Heads.Contains(12)).Select(l => l.RelativeY).ToArray();
            allVisible &= positions.Length == 3 && positions.Distinct().Count() == 3 && positions.All(y => y >= 0 && y <= 1);
            bitmap.Render(view);
            for (int i=0; i<3; i++) window.ShortcutForVerification(Key.Z,KeyModifiers.Control);
            trials++;
        }
        check("Chord: all 20 triples render after actual place/undo at the same time and position", allVisible && trials == 20);
    }

    private static EditableChart Example()
    {
        var c = new EditableChart { Id="rhythm", File="charts/rhythm.json", Difficulty=V2Difficulty.Hard, AudioOffsetSec=2 };
        static ExactBarTime At(double x) => ExactBarTime.FromDouble(x);
        c.Bpms.Add(new() { Time=At(0), Bpm=120 }); c.Bpms.Add(new() { Time=At(10), Bpm=240 });
        EditableNote Note(double bar,V2NoteType type=V2NoteType.Tap,EditorTrack track=EditorTrack.Center)
        {
            var n = new EditableNote { Id="n"+c.Notes.Count, Time=At(bar), Type=type, Track=track, Center=2.5, Width=1 };
            c.Notes.Add(n); return n;
        }
        foreach (var b in new[]{2d,3.5,5,8,10,10.5,11,11.25,11.5,11.75,12,12.5,12.75,16.5,17,17.5,21,22,28,32,38,44}) Note(b);
        Note(12,V2NoteType.ExTap); Note(13,V2NoteType.Drag); Note(13.25,V2NoteType.Drag); Note(13.5,V2NoteType.Drag); Note(17.75,V2NoteType.Mine);
        var hold = Note(14,V2NoteType.Hold);
        foreach(var (b,j) in new[]{(14.5,false),(15d,true),(16d,true)})
            hold.Nodes.Add(new(){Id="p"+b, Time=At(b), Center=2.5, Width=1, Judge=j});
        Note(15.5,V2NoteType.BarLine); Note(12.25,V2NoteType.Tap,EditorTrack.Left);
        var mixer=Note(12.5,V2NoteType.Mixer,EditorTrack.Right);
        mixer.Nodes.Add(new(){Id="tail",Time=At(17),Center=2,Width=1});
        return c;
    }
}
