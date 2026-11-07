using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

/// <summary>Real shared renderer/effect objects; does not claim GPU pixel or audio verification.</summary>
internal static class EditorAutoVerification
{
    public static void Run(Node host)
    {
        foreach (var track in Enum.GetValues<Track>())
        {
            var notes = new List<V2Note>
            {
                new V2BasicNote { Id = "tap", Type = V2NoteType.Tap, Time = Time(1), Center = 2, Width = 1 },
                new V2BasicNote { Id = "mine", Type = V2NoteType.Mine, Time = Time(2), Center = 2, Width = 1 },
                Path("hold", V2NoteType.Hold, 3, 5),
                Path("mixer", V2NoteType.Mixer, 6, 8),
                new V2BasicNote { Id = "drag", Type = V2NoteType.Drag, Time = Time(9), Center = 2, Width = 1, ScrollSpeedMultiplier = 1.4 },
                new V2BasicNote { Id = "extap", Type = V2NoteType.ExTap, Time = Time(10), Center = 2, Width = 1 },
                new V2BasicNote { Id = "barline", Type = V2NoteType.BarLine, Time = Time(11), Center = 2, Width = 1 },
            };
            var chart = new V2Chart
            {
                ChartId = "auto-test", AudioOffsetSec = 0,
                Bpms = [new(ExactBarTime.Zero, 240)], ScrollSpeeds = [],
                NotesLeft = track == Track.Left ? notes : [],
                NotesCenter = track == Track.Center ? notes : [],
                NotesRight = track == Track.Right ? notes : [],
            };
            var adapted = V2RuntimeAdapter.Adapt(chart);
            var root = new Node2D();
            host.AddChild(root);
            var preview = new EditorAutoPreview(root);
            preview.Reset(adapted, 0, 1);
            preview.Advance(1, 1);
            Check(root.GetChildren().OfType<NoteView>().Any(view => view.Visible && view.IsResidual), "hit residual");
            Check(Blooms(root) == 1, "one hit bloom");
            preview.Advance(1, 0);
            Check(Blooms(root) == 1, "same-time does not replay hit");
            preview.Reset(adapted, 1.5, 1);
            Check(Blooms(root) == 0 && !preview.ActiveViews.Any(view => view.Model.V2Metadata?.SourceId == "tap"), "seek consumes history silently");
            preview.Advance(2, .5);
            Check(Blooms(root) == 0, "Auto avoids Mine without burst");
            preview.Reset(adapted, 4, 1);
            var hold = preview.ActiveViews.Single(view => view.Model.Type == NoteType.HoldHead);
            var path = new DynamiteUniverse.ChartEditor.Core.EditorAutoTimeline(adapted.RuntimeChart).Plan.Sustains[hold.Model.Id];
            var bounds = path.BoundsAtRuntime(4);
            var expected = new GameplayRenderGeometry().TrackPositionAtLine(track, bounds.Center);
            Check(hold.IsLineAnchored && hold.Position.DistanceTo(expected) < .001, "seek restores moving Hold head");
            Check(root.GetChildren().OfType<GameplaySustainEffect>().Count(effect => effect.Visible) == 1, "continuous Hold contact");
            preview.SetPaused(true);
            Check(root.ProcessMode == Node.ProcessModeEnum.Disabled && root.GetChildren()
                .SelectMany(node => node.GetChildren()).OfType<GpuParticles2D>().All(particle => particle.SpeedScale == 0), "CPU and GPU pause");
            preview.SetPaused(false);
            preview.Advance(5, 1);
            Check(Blooms(root) == 1 && !root.GetChildren().OfType<GameplaySustainEffect>().Any(effect => effect.Visible), "Hold tail burst and contact release");
            preview.Reset(adapted, 7, 1);
            Check(root.GetChildren().OfType<NoteView>().Any(view => view.Visible && view.Name.ToString().StartsWith("MixerSlider_")), "seek restores Mixer receiver");
            Check(Blooms(root) == 0, "Mixer restoration has no historical bursts");
            preview.Advance(8, 1);
            Check(!root.GetChildren().OfType<NoteView>().Any(view => view.Visible && view.Name.ToString().StartsWith("MixerSlider_")), "Mixer ends without tail judgement");
            preview.Reset(adapted, 0, .1);
            preview.Advance(1, 1);
            Check(Blooms(root) == 1, "backward seek allows hit again at slow speed");
            preview.Reset(adapted, 4, 2);
            preview.Reset(V2RuntimeAdapter.Adapt(chart), 4, 2);
            Check(Blooms(root) == 0 && preview.ActiveViews.Any(view => view.Model.Type == NoteType.HoldHead && view.IsLineAnchored), "playing edit restores new chart state without hit replay");
            preview.Reset(adapted, 8.5, 1);
            preview.Advance(9, .5);
            Check(Blooms(root) == 1 && root.GetChildren().OfType<NoteView>().Any(view => view.Visible && view.IsResidual && view.Model.Type == NoteType.Drag), "differential-speed Drag uses hit and residual");
            preview.Reset(adapted, 9.5, 1);
            preview.Advance(10, .5);
            Check(Blooms(root) == 1, "ExTap burst");
            preview.Reset(adapted, 10.5, 1);
            preview.Advance(11, .5);
            Check(Blooms(root) == 0 && !preview.ActiveViews.Any(view => view.Model.Type == NoteType.BarLine), "BarLine disappears without judgement");
            VerifyStage(host, adapted);
            VerifyEffectLevels(host, adapted);
            root.Free();
            GD.Print($"PASS Auto preview renderer lifecycle track={track}");
        }
    }

    private static void VerifyStage(Node host, V2RuntimeAdapterResult chart)
    {
        var stage = new EditorStagePreview { Size = new Vector2(1920, 1080) };
        host.AddChild(stage);
        stage.SetChart(chart);
        var tap = chart.RuntimeChart.AllNotes.Single(note => note.V2Metadata?.SourceId == "tap");
        var hold = chart.RuntimeChart.AllNotes.Single(note => note.V2Metadata?.SourceId == "hold");
        var mixer = chart.RuntimeChart.AllNotes.Single(note => note.V2Metadata?.SourceId == "mixer");
        stage.SetSelection(new HashSet<int> { tap.Id, hold.Id, mixer.Id });
        stage.SetPlaying(true);
        stage.AdvancePlayback(.9, .9);
        Check(stage.SelectionBounds(tap) is not null, "selected Tap frame exists before hit");
        stage.AdvancePlayback(1, .1);
        Check(stage.SelectionBounds(tap) is null, "selected Tap frame disappears with body, not residual");
        var root = stage.GetNode<Node2D>("GameplayStage/AutoPlayback");
        Check(root.Visible && Blooms(root) == 1, "stage dispatches playback tick rather than seek");
        var residual = root.GetChildren().OfType<NoteView>().Single(view => view.Visible && view.IsResidual);
        stage.SetPlaying(false);
        stage.AdvancePlayback(2, 1);
        Check(stage.CurrentBar == 1 && residual.Visible && root.ProcessMode == Node.ProcessModeEnum.Disabled,
            "stage pause preserves current Auto frame");
        stage.SetPlaying(true);
        Check(root.GetChildren().Contains(residual) && Blooms(root) == 1, "resume retains effects without rebuilding");
        stage.CurrentBar = 4;
        Check(Blooms(root) == 0, "playing seek clears transient effects");
        var liveHold = root.GetChildren().OfType<NoteView>().Single(view => view.Visible && view.Model.Id == hold.Id);
        Check(stage.SelectionBounds(hold) is { } holdRect && holdRect.GetCenter().DistanceTo(liveHold.Position) < .001 &&
            holdRect.Size.DistanceTo(liveHold.BodySize + Vector2.One * 20) < .001,
            "Hold selection follows receiver position and changing width");
        stage.AdvancePlayback(5.4, 1.4);
        stage.AdvancePlayback(5.8, .4);
        Check(stage.SelectionBounds(hold) is null, "Hold selection disappears when tail body is recycled");
        stage.SetChart(chart, preserveCurrentBar: true, second: 7);
        Check(root.GetChildren().OfType<NoteView>().Any(view => view.Visible && view.Name.ToString().StartsWith("MixerSlider_")),
            "incremental chart uses preserved seconds, not old BPM bar");
        Check(stage.SelectionBounds(mixer) is not null, "Mixer selection follows live receiver");
        stage.AdvancePlayback(8, 1);
        Check(stage.SelectionBounds(mixer) is null, "Mixer selection disappears at path end");
        var retimed = V2RuntimeAdapter.Adapt(chart.Metadata.SourceChart with
        { Bpms = [new(ExactBarTime.Zero, 120)] });
        stage.SetChart(retimed, preserveCurrentBar: true, second: 7);
        Check(Math.Abs(stage.CurrentBar - 3.5) < 1e-9 && root.GetChildren().OfType<NoteView>()
            .Any(view => view.Visible && view.Model.Type == NoteType.HoldHead && view.IsLineAnchored),
            "playing BPM edit preserves seconds and rebuilds the newly active Hold");
        var empty = V2RuntimeAdapter.Adapt(chart.Metadata.SourceChart with
        { NotesLeft = [], NotesCenter = [], NotesRight = [] }, requireMainJudgement: false);
        stage.SetChart(empty, preserveCurrentBar: true, second: 7);
        Check(!root.GetChildren().OfType<NoteView>().Any(view => view.Visible) &&
            !root.GetChildren().OfType<GameplaySustainEffect>().Any(effect => effect.Visible),
            "playing delete clears receiver, contact, and old runtime IDs");
        stage.SetChart(chart, preserveCurrentBar: true, second: 7);
        stage.SuspendPlayback(true);
        Check(root.ProcessMode == Node.ProcessModeEnum.Disabled, "PAN suspends effects");
        stage.SuspendPlayback(false);
        stage.SetPlaying(false);
        stage.CurrentBar = .9;
        Check(stage.SelectionBounds(tap) is not null, "paused seek restores editable selection frame");
        stage.CurrentBar = 0;
        Check(!root.Visible, "paused seek restores editable notes");
        stage.SetPlaying(true);
        stage.AdvancePlayback(1, 1);
        Check(Blooms(root) == 1, "resume after paused seek re-arms hits");
        stage.Free();
    }

    private static void VerifyEffectLevels(Node host, V2RuntimeAdapterResult chart)
    {
        foreach (var level in Enum.GetValues<HitEffectLevel>())
        {
            var root = new Node2D();
            host.AddChild(root);
            var notes = chart.RuntimeChart.AllNotes.OrderBy(note => note.Second).ToArray();
            var plan = new DynamiteUniverse.ChartEditor.Core.EditorAutoTimeline(chart.RuntimeChart).Plan;
            var settings = new TestAppearance(level);
            var renderer = new GameplayNoteRenderer(root, chart.RuntimeChart, settings, chart.SyncAccentRuntimeIds,
                notes, notes.ToDictionary(note => note.Id), notes.ToDictionary(note => note.Id, note => note.Second),
                plan.Sustains, DynamiteUniverse.Shared.Judge.VisualPoolPlanning.Analyze(plan.Units, notes, plan.Sustains.Values), _ => null);
            renderer.SpawnNotes(1, 1);
            var tap = notes.Single(note => note.V2Metadata?.SourceId == "tap");
            renderer.ResolveNoteView(tap.Id, DynamiteUniverse.Shared.Judge.ScoreCategory.Tap, DynamiteUniverse.Shared.Judge.JudgeGrade.Perfect);
            Check(Blooms(root) == (level == HitEffectLevel.Off ? 0 : 1), "Full/Reduced/Off burst behavior retained");
            Check(root.GetChildren().OfType<NoteView>().Any(view => view.Visible && view.IsResidual), "all effect levels keep configured residual");
            if (level != HitEffectLevel.Off)
            {
                var particles = root.GetChildren().OfType<GameplayHitParticles>().Single(node => node.Visible);
                Check(particles.GetNode<GpuParticles2D>("EmberLayer").Visible == (level == HitEffectLevel.Full), "Reduced removes embers, Full retains embers");
                var bloom = root.GetChildren().OfType<GameplayHitBloom>().Single(node => node.Visible);
                Check(bloom.Position.DistanceTo(GameplayVisualMapper.PositionAt(tap, 0, true)) < .001, "mirrored hit location preserved");
            }
            root.Free();
        }
    }

    private sealed class TestAppearance(HitEffectLevel level) : IGameplayVisualSettings
    {
        public int TimingOffsetMs => 0;
        public double FallSpeedMultiplier => 1;
        public bool MirrorEnabled => true;
        public float NoteBrightnessScale => 1.3f;
        public float FlashIntensityScale => 1;
        public int HitResidualMs => 300;
        public float HitResidualSeconds => .3f;
        public HitEffectLevel HitEffectLevel => level;
        public DynamiteUniverse.Ui.UiMotionMode MotionMode => DynamiteUniverse.Ui.UiMotionMode.Full;
    }

    private static int Blooms(Node root) => root.GetChildren().OfType<GameplayHitBloom>().Count(bloom => bloom.Visible);
    private static ExactBarTime Time(int value) => ExactBarTime.FromFraction(value, 1);
    private static V2PathNote Path(string id, V2NoteType type, int start, int end) => new()
    {
        Id = id, Type = type, Time = Time(start), Center = 1, Width = 1,
        CurveToNext = V2PathCurve.EaseInQuad,
        Nodes = [new V2PathNode { Id = id + "-end", Time = Time(end), Center = 4, Width = 2,
            Judge = type == V2NoteType.Hold ? true : null, JudgeWasExplicit = type == V2NoteType.Hold }],
    };
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Auto preview: " + label);
    }
}
