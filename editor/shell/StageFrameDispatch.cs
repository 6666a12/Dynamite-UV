using System.Text.Json;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Only consecutive display frames may share a dispatcher slot. An input event,
/// pause, seek or lifecycle message seals that slot: neither a frame nor a command can overtake it.</summary>
internal sealed class StageFrameDispatch(Action<Action> post, Action<StageMessage> deliver)
{
    private sealed class FrameSlot(StageMessage value) { public StageMessage Latest = value; }
    private readonly object _sync = new();
    private FrameSlot? _tail;

    internal void Receive(StageMessage message)
    {
        lock (_sync)
        {
            if (message.Event == "tick" && message.Data.TryGetProperty("frame", out var flag) && flag.ValueKind == JsonValueKind.True)
            {
                if (_tail is { } pending) { pending.Latest = message; return; }
                var slot = _tail = new FrameSlot(message);
                post(() => Drain(slot));
            }
            else
            {
                _tail = null;
                post(() => deliver(message));
            }
        }
    }
    private void Drain(FrameSlot slot)
    {
        StageMessage message;
        lock (_sync)
        {
            if (ReferenceEquals(_tail, slot)) _tail = null;
            message = slot.Latest;
        }
        deliver(message);
    }
}
