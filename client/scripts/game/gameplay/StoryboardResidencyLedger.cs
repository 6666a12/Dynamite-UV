using Godot;

namespace DynamiteUniverse.Game;

/// <summary>Across stage changes, owns one decode lane and all outstanding bytes/slots until settled.</summary>
internal static class StoryboardResidencyLedger
{
    internal const int MaxSlots = 128;
    private static readonly object Gate = new();
    private static long _bytes, _retiringBytes;
    private static int _slots, _retiringSlots;
    private static bool _worker;
    // Main-thread only. No new graphics operations are started from scene-tree destruction.
    private static readonly List<Action> PendingGpuAcknowledgements = [];
    private static SceneTree? _queuedTree;

    internal static bool RendererSupported => DisplayServer.GetName() == "headless" || RenderingServer.GetRenderingDevice() is not null;

    internal static (long Bytes, int Slots, long RetiringBytes, int RetiringSlots, bool Worker) Snapshot()
    {
        lock (Gate) return (_bytes, _slots, _retiringBytes, _retiringSlots, _worker);
    }

    internal static bool TryReserve(long budget, long texture, long scratch)
    {
        lock (Gate)
        {
            if (_worker || _slots >= MaxSlots || _bytes + texture + scratch > budget) return false;
            _bytes += texture + scratch;
            ++_slots;
            _worker = true;
            return true;
        }
    }

    internal static void CancelJob(long texture, long scratch)
    {
        lock (Gate)
        {
            _bytes -= texture + scratch;
            --_slots;
            _worker = false;
        }
    }

    // The render command queue can retain the source Image after ImageTexture.CreateFromImage returns.
    internal static void SubmitJob(long releaseOnCpu, long uploadScratch)
    {
        lock (Gate)
        {
            _bytes -= releaseOnCpu;
            _worker = false;
        }
        if (uploadScratch > 0) AfterGpu(() => { lock (Gate) _bytes -= uploadScratch; });
    }

    internal static void Retire(long bytes, int slots)
    {
        if (slots == 0) return;
        lock (Gate)
        {
            _retiringBytes += bytes;
            _retiringSlots += slots;
        }
        AfterGpu(() =>
        {
            lock (Gate)
            {
                _bytes -= bytes;
                _slots -= slots;
                _retiringBytes -= bytes;
                _retiringSlots -= slots;
            }
        });
    }

    // Godot 4.7 RD delivers BufferGetDataAsync from _stall_for_frame AFTER fence_wait;
    // _begin_frame then frees pending RD resources for that frame. Enqueue before the next
    // render submission, after all clears and Texture2D frees; defer accounting to main.
    // This is an actual queue completion notification, not a hardcoded N-frame delay.
    // Unavailable or failed fences conservatively leave the reservation charged.
    private static void AfterGpu(Action completed)
    {
        if (DisplayServer.GetName() == "headless")
        {
            completed();
            return;
        }
        PendingGpuAcknowledgements.Add(completed);
        if (_queuedTree is not null || Engine.GetMainLoop() is not SceneTree tree) return;
        _queuedTree = tree;
        tree.ProcessFrame += FlushGpuBatch;
    }

    private static void FlushGpuBatch()
    {
        if (_queuedTree is { } tree) tree.ProcessFrame -= FlushGpuBatch;
        _queuedTree = null;
        var batch = PendingGpuAcknowledgements.ToArray();
        PendingGpuAcknowledgements.Clear();
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            var device = RenderingServer.GetRenderingDevice();
            if (device is null) return;
            var marker = device.StorageBufferCreate(4, new byte[4]);
            if (!marker.IsValid) return;
            var result = device.BufferGetDataAsync(marker, Callable.From<byte[]>(data =>
            {
                // The data has no payload value. Callback arrival is the fence signal.
                Callable.From(() => { foreach (var complete in batch) complete(); }).CallDeferred();
            }), 0, 4);
            device.FreeRid(marker);
            if (result != Error.Ok) GD.PushWarning("Storyboard GPU budget remains reserved: retirement fence was not accepted");
        }));
    }
}
