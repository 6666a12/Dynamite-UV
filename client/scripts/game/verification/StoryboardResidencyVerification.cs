using System.Diagnostics;
using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Verification;

/// <summary>Isolated original-fixture verification; can exercise the actual GPU fence or the headless scheduler.</summary>
public partial class StoryboardResidencyVerification : Node2D
{
    private GameplayStageRenderer? _stage;
    private GameplayStoryboardSession? _performance;
    private ChartPack? _pack;
    private int _peakSlots, _peakRetiring;
    private long _peakBytes;
    private int _tier;

    public override async void _Ready()
    {
        try
        {
            Require(ChartPack.IsEditorOrInternal, "verification is Editor/Internal only");
            var path = System.Environment.GetEnvironmentVariable("DUV_VERIFY_PACK");
            Require(path is not null && Path.IsPathFullyQualified(path), "absolute DUV_VERIFY_PACK is required");
            _pack = ChartPack.Load(path!) ?? throw new InvalidOperationException("fixture pack could not be loaded");
            _tier = System.Environment.GetEnvironmentVariable("DUV_STORYBOARD_MEMORY_MIB") == "128" ? 128 : 256;

            Mount("stream-handoff-128");
            await Until(() => _performance!.Prime(2), "prewarm of 128 tiny textures");
            Require(Count(2.2) == 128, "all 128 initial textures are ready");
            Require(Count(7.8) == 128, "prefetch must not evict current pinned textures");
            Require(Count(8.1) == 0, "the opaque fallback replaces incomplete cuts, not a partially rendered bank");
            await Until(() => Count(8.1) == 8, "eight textures after retirement");
            _performance!.TransportJump();
            Count(12.1); // Begin work for one bank, then invalidate that generation before publishing.
            _performance.TransportJump();
            await Until(() => Count(8.2) == 8, "backward seek retaining the still-needed bank");
            _performance.Sample(20, true);
            Require(_performance.ActiveItems == 0, "no performance after natural audio end");
            _performance.TransportJump();
            await Until(() => Count(8.2) == 8, "seek back from the audio tail before final teardown");
            _performance.MemoryPressure();
            Require(Count(8.3) == 8, "memory pressure must keep pinned images");
            await Drain();

            Mount("stream-overflow-129");
            await Until(() => _performance!.Prime(.5), "two initial textures");
            Require(Count(.6) == 2, "initial bank visible");
            await Until(() => { Count(4.1); return StoryboardResidencyLedger.Snapshot().Slots == 128; }, "oversubscribed bank reaches slot cap");
            Require(Count(4.2) == 0, "129 required physical textures use the static fallback");
            await Until(() => Count(8.1) == 2, "recovery after the oversubscribed interval");
            await Drain();

            Mount("stream-alias-crop");
            await Until(() => _performance!.Prime(4.2), "two crop textures plus the prepared gradient page");
            Require(Count(4.3) == 4, "three cropped draws and a gradient");
            Require(StoryboardResidencyLedger.Snapshot().Slots == 3, "same source and crop aliases share one slot");
            await Drain();

            Mount("stream-many-1024");
            await Until(() => _performance!.Prime(0), "1024 declarations with a small initial working set");
            Require(Count(.1) == 8 && StoryboardResidencyLedger.Snapshot().Slots == 8, "unused declarations must not allocate a texture slot");
            _performance!.TransportJump();
            await Until(() => Count(9) == 8, "last bank in the 1024-declaration chart");
            await Drain();

            if (_pack.ChartOf("stream-byte-pressure") is not null)
            {
                Mount("stream-byte-pressure");
                if (_tier == 256)
                {
                    await Until(() => _performance!.Prime(2.2), "all eight 16 MiB textures under the 256 MiB envelope");
                    Require(Count(2.3) == 8, "the standard tier covers the large working set");
                }
                else
                {
                    await Until(() =>
                    {
                        _performance!.Prime(2.2);
                        return StoryboardResidencyLedger.Snapshot() is { Slots: >= 5, Worker: false };
                    }, "the low tier gets close to capacity");
                    Require(Count(2.3) == 0, "the 128 MiB tier reserves decode/upload memory and falls back for this cut");
                }
                await Until(() => Count(8.2) == 2, "the tiny bank recovers after byte pressure");
                await Drain();
            }
            var headless = DisplayServer.GetName() == "headless";
            Require(_peakSlots == 128 && (headless || _peakRetiring > 0), "the slot cap and deferred GPU retirement should be exercised");
            GD.Print($"STORYBOARD_RESIDENCY_VERIFY pass tier_mib={_tier} headless={headless} peak_slots={_peakSlots} peak_retiring_slots={_peakRetiring} peak_bytes={_peakBytes}");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"STORYBOARD_RESIDENCY_VERIFY failure: {ex}");
            _performance?.Stop();
            GetTree().Quit(1);
        }
    }

    private void Mount(string id)
    {
        var diff = _pack!.ChartOf(id) ?? throw new InvalidOperationException($"missing fixture chart: {id}");
        _stage = new GameplayStageRenderer();
        AddChild(_stage);
        _stage.BuildCommunityStageChrome(40, gameplayHud: true);
        _performance = GameplayStoryboardSession.Mount(_pack, _pack.LoadChart(diff), _stage, 40, 20,
            new GameSettings(), verificationSkip: false, batchSprites: true, verificationBrightness: null,
            tierMiB: _tier, verificationLookahead: 1);
        Require(_performance is not null, $"attachment was not mounted for {id}");
    }

    private int Count(double time)
    {
        _performance!.Sample(time, false);
        Observe();
        return _performance.ActiveItems;
    }

    private async Task Until(Func<bool> ready, string description)
    {
        var start = Stopwatch.GetTimestamp();
        while (!ready())
        {
            Observe();
            Require(Stopwatch.GetElapsedTime(start).TotalSeconds < 12, $"timeout: {description}");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Observe();
    }

    private async Task Drain()
    {
        _performance!.Stop();
        Observe();
        _stage!.QueueFree();
        _stage = null;
        _performance = null;
        await Until(() => StoryboardResidencyLedger.Snapshot() is { Slots: 0, Bytes: 0, Worker: false }, "lease drain on scene exit");
    }

    private void Observe()
    {
        var snapshot = StoryboardResidencyLedger.Snapshot();
        _peakSlots = Math.Max(_peakSlots, snapshot.Slots);
        _peakBytes = Math.Max(_peakBytes, snapshot.Bytes);
        _peakRetiring = Math.Max(_peakRetiring, snapshot.RetiringSlots);
        Require(snapshot.Slots is >= 0 and <= StoryboardResidencyLedger.MaxSlots &&
            snapshot.Bytes >= 0 && snapshot.Bytes <= _tier * StoryboardTextureStore.MiB,
            $"budget exceeded: {snapshot}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
