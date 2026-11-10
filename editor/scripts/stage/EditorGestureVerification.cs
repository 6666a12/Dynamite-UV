using Godot;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

/// <summary>Runs actual Godot authoring and view construction, not injected shell events.</summary>
internal static class EditorGestureVerification
{
    public static void Run()
    {
        EditorMagnetVerification.Run();
        var chart = new V2Chart
        {
            ChartId = "gesture-test", AudioOffsetSec = 0,
            Bpms = [new V2BpmEvent(ExactBarTime.Zero, 120)], ScrollSpeeds = [],
            NotesLeft = [], NotesCenter = [], NotesRight = [],
        };
        var empty = V2RuntimeAdapter.Adapt(chart, requireMainJudgement: false);
        using (var stage = new EditorStagePreview())
        {
            stage.SetChart(empty);
            var wheelSteps = 0d;
            stage.WheelSteps += steps => wheelSteps += steps;
            using var down = new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1 };
            using var up = new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Factor = 1 };
            stage.HandlePointerInput(down);
            Check(wheelSteps == 1, "wheel down navigates forward");
            stage.HandlePointerInput(up);
            Check(wheelSteps == 0, "wheel up navigates backward");
            stage.WheelReversed = true;
            stage.HandlePointerInput(down);
            Check(wheelSteps == -1, "reversed wheel down navigates backward");
            stage.HandlePointerInput(up);
            Check(wheelSteps == 0, "reversed wheel up navigates forward");
            down.Factor = .25f;
            stage.HandlePointerInput(down);
            Check(wheelSteps == -.25, "reversed wheel preserves fractional factor");
            stage.SetChart(empty);
            Check(stage.WheelReversed, "chart reload preserves wheel preference");
            stage.WheelReversed = false;
            stage.HandlePointerInput(down);
            Check(wheelSteps == 0, "restored wheel preserves original direction");
            stage.Free();
            GD.Print("PASS Godot wheel navigation directions");
        }
        foreach (var track in Enum.GetValues<EditorTrack>())
        {
            var author = new EditorStageAuthoring();
            author.Configure("PLACE", V2NoteType.Tap, track, .6, 16);
            using var motion = new InputEventMouseMotion();
            author.HandleInput(motion, Point(track, .25, .6), [], empty.Metadata, 0, 1);
            Check(author.PlacementPreview!.Center == .6 && author.PlacementPreview.Width == .6,
                "decimal grid ghost has canonical 0.6 center and width");
            author.Configure("PLACE", V2NoteType.Tap, track, 1, 16);
            var point = Point(track, .25, 2.537);
            author.HandleInput(motion, point, [], empty.Metadata, 0, 1);
            Check(Math.Abs(author.PlacementPreview!.Center - 2.5) < 1e-6, "default grid position");
            using var fineDown = new InputEventKey { Keycode = Key.X, Pressed = true };
            Input.ParseInputEvent(fineDown);
            Input.FlushBufferedEvents();
            author.HandleInput(motion, point, [], empty.Metadata, 0, 1);
            Check(Math.Abs(author.PlacementPreview!.Center - 2.54) < 1e-6, "X fine position");
            using var fineUp = new InputEventKey { Keycode = Key.X, Pressed = false };
            Input.ParseInputEvent(fineUp);
            Input.FlushBufferedEvents();
            author.Configure("PLACE", V2NoteType.Tap, track, 1, 16, positionSnap: false);
            author.HandleInput(motion, point, [], empty.Metadata, 0, 1);
            Check(Math.Abs(author.PlacementPreview!.Center - 2.537) < 1e-5, "free position");
            GD.Print($"PASS Godot spatial grid / X fine / free track={track}");
        }
        foreach (var track in Enum.GetValues<EditorTrack>())
        {
            var author = new EditorStageAuthoring();
            author.Configure("PLACE", V2NoteType.Tap, track, 8, 16);
            using var move = new InputEventMouseMotion();
            using var press = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
            using var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false };
            var point = Point(track, .25, -.5);
            author.HandleInput(move, point, [], empty.Metadata, 0, 1);
            Check(Math.Abs(author.PlacementPreview!.Center + .5) < 1e-6 && author.PlacementPreview.Width == 8,
                "overscan hover preserves wide geometry");
            StagePlacementRequest? result = null;
            author.PlaceRequested += value => result = value;
            author.HandleInput(press, point, [], empty.Metadata, 0, 1);
            author.HandleInput(release, point, [], empty.Metadata, 0, 1);
            Check(result is not null && Math.Abs(result.Center + .5) < 1e-6 && result.Width == 8,
                "overscan release matches ghost");
            GD.Print($"PASS Godot overscan placement and width above five track={track}");
        }
        foreach (var divisor in new[] { 3, 5, 4, 12 })
        foreach (var track in Enum.GetValues<EditorTrack>())
        {
            var author = new EditorStageAuthoring();
            author.Configure("PLACE", V2NoteType.Hold, track, 1, divisor);
            var time = ExactBarTime.FromFraction(1, divisor);
            var point = Point(track, time.ToDouble());
            using var motion = new InputEventMouseMotion();
            using var press = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
            using var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false };
            author.HandleInput(motion, point, [], empty.Metadata, 0, 1);
            var visual = author.PlacementPreview!;
            Check(visual.Time == time && visual.HeadTimeText == ExactBarTimeText.Format(time), "exact hover label");
            using var overlay = new EditorPlacementOverlay();
            overlay.SetPlacement(visual, empty.Metadata, 0, 1, []);
            Check(overlay.GetNode<Node2D>("PlacementGhost").GetChildCount() > 0, "real fractional ghost views");
            author.HandleInput(press, point, [], empty.Metadata, 0, 1);
            author.HandleInput(release, point, [], empty.Metadata, 0, 1);
            author.HandleInput(motion, Point(track, 2d / divisor), [], empty.Metadata, 0, 1);
            visual = author.PlacementPreview!;
            Check(visual.Time == time && visual.TailTime == time + time &&
                visual.TailTimeText == ExactBarTimeText.Format(time + time), "exact head/tail draft labels");
            StagePlacementRequest? committed = null;
            author.PlaceRequested += request => committed = request;
            author.HandleInput(press, Point(track, 2d / divisor), [], empty.Metadata, 0, 1);
            Check(committed is not null && committed.Bar == time.ToDouble(), "fractional placement commit");
            overlay.Free();
            GD.Print($"PASS Godot placement ghost / exact labels / Hold draft / commit grid={divisor} track={track}");
        }
        foreach (var track in Enum.GetValues<EditorTrack>())
        foreach (var type in new[] { V2NoteType.Hold, V2NoteType.Mixer })
        {
            var path = new V2PathNote
            {
                Id = "path", Type = type, Time = ExactBarTime.FromFraction(1, 8), Center = 2.5, Width = 1,
                CurveToNext = V2PathCurve.EaseInCubic,
                Nodes = [
                    new V2PathNode { Id = "middle", Time = ExactBarTime.FromFraction(1, 4), Center = 3,
                        Width = 1, Judge = type == V2NoteType.Hold ? true : null, JudgeWasExplicit = type == V2NoteType.Hold },
                    new V2PathNode { Id = "tail", Time = ExactBarTime.One, Center = 2.5,
                        Width = 1, Judge = type == V2NoteType.Hold ? true : null, JudgeWasExplicit = type == V2NoteType.Hold }],
            };
            var adapted = V2RuntimeAdapter.Adapt(chart with
            {
                NotesLeft = track == EditorTrack.Left ? [path] : [],
                NotesCenter = track == EditorTrack.Center ? [path] : [],
                NotesRight = track == EditorTrack.Right ? [path] : [],
            }, requireMainJudgement: false);
            var notes = adapted.RuntimeChart.AllNotes.ToArray();
            foreach (var tool in new[] { "SELECT", "PATH" })
            {
                var author = new EditorStageAuthoring();
                author.Configure(tool, type, track, 1, 16);
                StagePathMoveRequest? moved = null;
                var wholeMoves = 0;
                var selected = -1;
                author.PathMoveRequested += request => moved = request;
                author.NoteMoveRequested += _ => wholeMoves++;
                author.PathNotePicked += id => selected = id;
                var start = Point(track, .25, 3);
                using var press = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
                using var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false };
                author.HandleInput(press, start, notes, adapted.Metadata, 0, 1);
                Check(selected == adapted.RuntimeIdsBySourceId["middle"], "press selects authored middle node");
                using var dragMotion = new InputEventMouseMotion();
                author.HandleInput(dragMotion, Point(track, .3125, 3.2), notes, adapted.Metadata, 0, 1);
                Check(author.PathPreview(adapted.Metadata, 0, 1)?.NodeId == "middle", "node preview retains identity");
                author.HandleInput(release, Point(track, .3125, 3.2), notes, adapted.Metadata, 0, 1);
                Check(moved?.NodeId == "middle" && moved.NoteId == "path" && wholeMoves == 0,
                    "middle drag never routes to parent move");
                var synthetic = notes.First(note => !adapted.RuntimeIdsBySourceId.ContainsKey(note.V2Metadata!.SourceId));
                Check(author.HitNote(Point(track, synthetic.BarTime, synthetic.Position + synthetic.Width / 2),
                    [synthetic], adapted.Metadata, 0, 1) == -1, "tessellation points cannot be selected");
                GD.Print($"PASS Godot {tool} {type} middle-node identity and synthetic-point exclusion track={track}");
            }
            using var renderer = new GameplayStageRenderer
            {
                NoteVisibilityFilter = note => adapted.RuntimeIdsBySourceId.ContainsKey(note.V2Metadata!.SourceId),
            };
            renderer.SetNotes(notes);
            Check(renderer.RenderedNoteCount == 3 && renderer.RenderedLinkCount == notes.Length - 1,
                "only authored handles visible, all curve links retained");
            renderer.Free();
        }
    }

    private static Vector2 Point(EditorTrack track, double bar, double center = 2.5)
    {
        var point = GameplayStageGeometry.PositionAt(EditorStagePlacement.ToRuntimeTrack(track), center,
            (float)(bar * 2 * GameplayStageGeometry.BaseFallSpeedPx));
        return new Vector2(point.X, point.Y);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}
