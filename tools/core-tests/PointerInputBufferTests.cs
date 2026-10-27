using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;

namespace CoreTests;

internal static class PointerInputBufferTests
{
    private readonly record struct Position(Track Track, double Value);

    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("  Pointer lifecycle, frame snapshots and short Hold replay");
        var buffer = new PointerInputBuffer<Position>(
            (target, id, position, phase) =>
                target.Add(new TouchSample(id, position.Track, position.Value, phase)),
            (id, position) => Project(id, position));

        buffer.Queue(new PointerInput<Position>(7,
            new Position(Track.Center, .25), 1.0, PointerAction.Press));
        buffer.Queue(new PointerInput<Position>(7,
            new Position(Track.Center, .75), 1.1, PointerAction.Move));
        buffer.Queue(new PointerInput<Position>(7,
            new Position(Track.Center, .75), 1.2, PointerAction.Release));
        buffer.CollectFrame();
        check(buffer.Inputs.Select(input => input.Action).SequenceEqual(
                new[] { PointerAction.Press, PointerAction.Move, PointerAction.Release }) &&
              buffer.Presses is [{ Touch.Id: 7, Touch.Position: .25, Time: 1.0 }],
            "pointer buffer preserves timestamped begin/move/release and judges the original press");
        check(buffer.Touches.Count == 0 && buffer.InitialTouches.Count == 0,
            "same-frame pointer release does not leave a stationary contact");
        var settings = JudgeSettings.ForPreset(JudgePreset.Hard);
        var shortHold = new SustainPath
        {
            Kind = SustainKind.Hold, HeadId = 1, Track = Track.Center,
            Nodes = [CoreTestSupport.TestNote(1, 2, NoteType.HoldHead, 0, 0, 1, 1),
                     CoreTestSupport.TestNote(2, -1, NoteType.HoldNode, .1, 0, 1, 1.15)],
        };
        check(!buffer.HoldContacts.Observe(shortHold, 1, 1.3, HoldContactState.Contact,
                settings, _ => 150, .4).HasLoss,
            "short Hold sees contact through its tail even after same-frame release");

        buffer.Queue(new PointerInput<Position>(9,
            new Position(Track.Left, 2), 2, PointerAction.Press));
        buffer.CollectFrame();
        buffer.CollectFrame();
        check(buffer.Touches is [{ Id: 9, Track: Track.Left, Phase: ContactPhase.Stationary }] &&
              buffer.InitialTouches is [{ Id: 9, Position: 2, Phase: ContactPhase.Stationary }],
            "pointer buffer seeds and retains untouched pointers as the next frame's initial snapshot");

        buffer.Queue(new PointerInput<Position>(9,
            new Position(Track.Right, 4), 2.2, PointerAction.Move));
        buffer.CollectFrame();
        check(buffer.InitialTouches is [{ Track: Track.Left, Position: 2 }] &&
              buffer.Touches is [{ Track: Track.Right, Position: 4, Phase: ContactPhase.Moved }],
            "pointer movement replaces its projected touch without altering the frame-start snapshot");

        buffer.Queue(new PointerInput<Position>(9,
            new Position(Track.Right, 4), 2.3, PointerAction.Release));
        buffer.Queue(new PointerInput<Position>(10,
            new Position(Track.Center, 1), 2.4, PointerAction.Press));
        buffer.CollectFrame();
        check(buffer.Inputs.Select(input => input.Id).SequenceEqual(new[] { 9, 10 }) &&
              buffer.Presses is [{ Touch.Id: 10 }] && buffer.Touches is [{ Id: 10 }],
            "pointer replacement keeps the release visible and records a same-frame finger handoff");

        buffer.Clear();
        check(buffer.Touches.Count == 0 && buffer.InitialTouches.Count == 0 &&
              buffer.Inputs.Count == 0 && buffer.Presses.Count == 0,
            "clearing pointer state removes active contacts and frame snapshots");
        check(buffer.HoldContacts.Observe(shortHold, 1, 1.3, HoldContactState.Contact,
                settings, _ => 150, .4).HasLoss,
            "clearing pointer state also removes stale Hold replay");

        buffer.Queue(new(99, new(Track.Left, 2), 3, PointerAction.Move));
        buffer.CollectFrame();
        check(buffer.Inputs.Count == 0 && buffer.Touches.Count == 0 && buffer.Presses.Count == 0,
            "unknown pointer move cannot resurrect a released or focus-cleared contact");
        for (var id = 0; id < 4; id++)
            buffer.Queue(new(id, new(Track.Center, id), 4, PointerAction.Press));
        buffer.CollectFrame();
        check(buffer.Touches.Count == 4 && buffer.Presses.Count == 4,
            "four simultaneous pointers remain independent");
        buffer.Queue(new(0, new(Track.Center, 0), 5, PointerAction.Release));
        buffer.DiscardPending();
        buffer.CollectFrame();
        check(buffer.Touches.Count == 4 && buffer.InitialTouches.Count == 4 && buffer.Inputs.Count == 0,
            "discarding pending events preserves active contacts for the next frame");

        var multiTrack = new PointerInputBuffer<double>((target, id, value, phase) =>
        {
            target.Add(new(id, Track.Center, value, phase));
            target.Add(new(id, Track.Right, 5 - value, phase));
        }, (id, value) => new(new(id, Track.Center, value, ContactPhase.Moved),
            null, new(id, Track.Right, 5 - value, ContactPhase.Moved)));
        multiTrack.Queue(new(1, 2, 1, PointerAction.Press));
        multiTrack.Queue(new(1, 3, 1.1, PointerAction.Move));
        multiTrack.CollectFrame();
        check(multiTrack.Presses.Count == 2 && multiTrack.Touches.Count == 2 &&
              multiTrack.Presses[1].Touch.Position == 3 && multiTrack.Touches[1].Position == 2 &&
              multiTrack.Touches.All(touch => touch.Phase == ContactPhase.Began),
            "overlapping tracks retain their adapter projection and same-frame Began phase");
    }

    private static HoldTouchProjection Project(int id, Position position)
    {
        var touch = new TouchSample(id, position.Track, position.Value, ContactPhase.Moved);
        return position.Track switch
        {
            Track.Center => new(touch, null, null),
            Track.Left => new(null, touch, null),
            _ => new(null, null, touch),
        };
    }
}
