using Godot;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

internal static class EditorMagnetVerification
{
    public static void Run()
    {
        foreach (var track in Enum.GetValues<EditorTrack>())
        foreach (var scale in new[] { .5f, 1f, 1.5f })
        {
            var target = new V2BasicNote { Id = "target", Type = V2NoteType.Tap,
                Time = ExactBarTime.FromFraction(1, 4), Center = 2.537, Width = 1 };
            var chart = new V2Chart { ChartId = "magnet", AudioOffsetSec = 0,
                Bpms = [new V2BpmEvent(ExactBarTime.Zero, 120)], ScrollSpeeds = [],
                NotesLeft = track == EditorTrack.Left ? [target] : [],
                NotesCenter = track == EditorTrack.Center ? [target] : [],
                NotesRight = track == EditorTrack.Right ? [target] : [] };
            var adapted = V2RuntimeAdapter.Adapt(chart, requireMainJudgement: false);
            var notes = adapted.RuntimeChart.AllNotes.ToArray();
            var author = new EditorStageAuthoring();
            author.Configure("PLACE", V2NoteType.Tap, track, 1, 16);
            author.SetMagnet(true, scale, new Rect2(0, 0, 1920, 1080));
            var runtime = EditorStagePlacement.ToRuntimeTrack(track);
            var unit = runtime == Track.Center ? GameplayStageGeometry.CenterUnitPx : GameplayStageGeometry.SideUnitPx;
            Vector2 Point(double center)
            {
                var p = GameplayStageGeometry.PositionAt(runtime, center, (float)(.5 * GameplayStageGeometry.BaseFallSpeedPx));
                return new Vector2(p.X, p.Y);
            }
            using var motion = new InputEventMouseMotion();
            using var press = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
            using var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false };
            var point = Point(target.Center + 4 / (unit * scale));
            author.HandleInput(motion, point, notes, adapted.Metadata, 0, 1);
            Require(Math.Abs(author.PlacementPreview!.Center - target.Center) < 1e-5, "acquires off-grid center");
            author.HandleInput(motion, Point(target.Center + 8 / (unit * scale)), notes, adapted.Metadata, 0, 1);
            Require(Math.Abs(author.PlacementPreview!.Center - target.Center) < 1e-5, "holds beyond acquisition radius");
            author.HandleInput(motion, Point(target.Center + 12 / (unit * scale)), notes, adapted.Metadata, 0, 1);
            Require(author.MagnetGuide is null, "releases beyond ten pixels");
            StagePlacementRequest? committed = null;
            author.PlaceRequested += r => committed = r;
            author.HandleInput(press, point, notes, adapted.Metadata, 0, 1);
            author.HandleInput(release, point, notes, adapted.Metadata, 0, 1);
            Require(committed is not null && Math.Abs(committed.Center - target.Center) < 1e-5, "release retains magnetic center");
            author.Cancel();
            author.HandleInput(motion, point, notes, adapted.Metadata, 0, 1, new HashSet<int> { notes[0].Id });
            Require(author.MagnetGuide is null, "selected target excluded");
            author.SetMagnet(false, scale, new Rect2(0, 0, 1920, 1080));
            author.HandleInput(motion, point, notes, adapted.Metadata, 0, 1);
            Require(author.MagnetGuide is null, "disabled magnet keeps grid behavior");
            author.SetMagnet(true, scale, new Rect2(0, 0, 1920, 1080));
            var moving = target with { Id = "moving", Center = 1.5 };
            var moveChart = chart with
            {
                NotesLeft = track == EditorTrack.Left ? [target, moving] : [],
                NotesCenter = track == EditorTrack.Center ? [target, moving] : [],
                NotesRight = track == EditorTrack.Right ? [target, moving] : [],
            };
            var movingAdapted = V2RuntimeAdapter.Adapt(moveChart, requireMainJudgement: false);
            var movingNotes = movingAdapted.RuntimeChart.AllNotes.ToArray();
            var movingId = movingAdapted.RuntimeIdsBySourceId["moving"];
            author.Configure("SELECT", V2NoteType.Tap, track, 1, 16);
            StageNoteMoveRequest? moved = null;
            author.NoteMoveRequested += request => moved = request;
            var selected = new HashSet<int> { movingId };
            author.HandleInput(press, Point(1.5), movingNotes, movingAdapted.Metadata, 0, 1, selected);
            author.HandleInput(motion, point, movingNotes, movingAdapted.Metadata, 0, 1, selected);
            author.TryGetSelectPreview(movingAdapted.Metadata, 0, 1, out _, out _, out var preview, out _);
            Require(preview is not null && preview.PositionStep == 0 && Math.Abs(1.5 + preview.EndCenter - preview.StartCenter - target.Center) < 1e-5,
                "SELECT preview bypasses position grid only when latched");
            author.HandleInput(release, point, movingNotes, movingAdapted.Metadata, 0, 1, selected);
            Require(moved == preview, "SELECT release exactly matches magnetic preview");
            author.Cancel();
            var movingNote = movingNotes.Single(n => n.Id == movingId);
            var visualSize = DynamiteUniverse.Game.GameplayVisualMapper.SizeFor(movingNote);
            var half = runtime == Track.Center ? visualSize.X / 2 : visualSize.Y / 2;
            var startEdgePoint = Point(1.5) + (runtime == Track.Center ? new Vector2(half, 0) : new Vector2(0, -half));
            // Width 1: move end edge to same rendered end as target, opposite edge fixed.
            var screenDelta = (float)((target.Center - 1.5) * unit - 4 / scale);
            var endEdgePoint = startEdgePoint + (runtime == Track.Center ? new Vector2(screenDelta, 0) : new Vector2(0, -screenDelta));
            StageNoteResizeRequest? resized = null;
            author.NoteResizeRequested += r => resized = r;
            author.HandleInput(press, startEdgePoint, movingNotes, movingAdapted.Metadata, 0, 1, selected);
            author.HandleInput(motion, endEdgePoint, movingNotes, movingAdapted.Metadata, 0, 1, selected);
            author.TryGetSelectPreview(movingAdapted.Metadata, 0, 1, out _, out _, out _, out var resizePreview);
            Require(resizePreview is not null && resizePreview.WidthStep == 0, "resize magnet overrides width grid");
            author.HandleInput(release, endEdgePoint, movingNotes, movingAdapted.Metadata, 0, 1, selected);
            Require(resized == resizePreview, "resize release matches preview");
            var span = EditorStagePlacement.ResizeSnappedFromEdge(1.5, 1, resized!.Edge, false, resized.WidthStep);
            Require(Math.Abs((span.Center - target.Center) * unit + (span.Width - 1) * (runtime == Track.Center ? visualSize.X : visualSize.Y) / 2) < .001,
                "resized rendered end aligns exactly");
            author.Cancel();
            author.Configure("PLACE", V2NoteType.Tap, track, 1, 16);
            using var altDown = new InputEventKey { Keycode = Key.Alt, Pressed = true };
            using var altUp = new InputEventKey { Keycode = Key.Alt, Pressed = false };
            Input.ParseInputEvent(altDown); Input.FlushBufferedEvents();
            author.HandleInput(motion, point, notes, adapted.Metadata, 0, 1);
            Require(author.MagnetGuide is null, "Alt temporarily bypasses magnet");
            Input.ParseInputEvent(altUp); Input.FlushBufferedEvents();
            author.Cancel();
            author.Configure("PLACE", V2NoteType.Tap, track, 1, 16, positionSnap: false);
            author.HandleInput(motion, point, notes, adapted.Metadata, 0, 1);
            Require(Math.Abs(author.PlacementPreview!.Center - target.Center) < 1e-5, "free mode retains independent magnet");
            author.SetMagnet(true, scale, new Rect2(-1000, -1000, 1, 1));
            author.HandleInput(motion, point, notes, adapted.Metadata, 0, 1);
            Require(author.MagnetGuide is null, "offscreen targets are excluded");
            author.SetMagnet(true, scale, new Rect2(0, 0, 1920, 1080));
            var distant = point + (runtime == Track.Center ? new Vector2(0, 110 / scale) : new Vector2(110 / scale, 0));
            author.HandleInput(motion, distant, notes, adapted.Metadata, 0, 1);
            Require(author.MagnetGuide is null, "distant timeline neighbours do not attract");
            GD.Print($"PASS magnet placement/latch/release/exclusion/move/resize/Alt/free/visibility track={track} scale={scale}");
        }
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException("Magnet: " + message); }
}
