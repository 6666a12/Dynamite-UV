using DynamiteUniverse.Ui;

namespace CoreTests;

/// <summary>详情请求：代际推进、开始作废旧请求、一次性消费、作废丢弃未消费完成、释放后拒新请求。</summary>
internal static class SongDetailRequestTests
{
    internal static void Run(Action<bool, string> check)
    {
        Console.WriteLine("  Detail request generation, stale cancellation and disposal");
        Generations(check);
        NewestRequestWins(check);
        InvalidateDropsUnconsumed(check);
        DisposeRejectsNewRequests(check);
        FailuresArriveAsData(check);
    }

    private static void Generations(Action<bool, string> check)
    {
        using var request = new SongDetailRequest<string>();
        check(request.CurrentGeneration == 0, "a fresh detail request starts at generation zero");
        var first = request.Begin();
        check(first == 1 && request.CurrentGeneration == first,
            "begin advances the generation and reports it");
        var second = request.Begin();
        check(second > first && request.CurrentGeneration == second,
            "every refresh starts a newer generation");
        var invalidated = request.Invalidate();
        check(invalidated > second && request.CurrentGeneration == invalidated,
            "invalidating advances the generation so stale completions fail the check");

        var stale = new SongDetailKey(first, "pack", "chart");
        check(!request.IsCurrentGeneration(stale.Generation) && !request.IsCurrent(stale, "pack", "chart"),
            "a key from an older generation is not current");
        var current = new SongDetailKey(request.CurrentGeneration, "pack", "chart");
        check(request.IsCurrent(current, "pack", "chart") &&
              !request.IsCurrent(current, "pack", "other") &&
              !request.IsCurrent(current, "other", "chart"),
            "identity checks compare generation, pack and chart exactly");
    }

    private static void NewestRequestWins(Action<bool, string> check)
    {
        using var request = new SongDetailRequest<string>();
        using var oldStarted = new ManualResetEventSlim();
        using var releaseOld = new ManualResetEventSlim();
        var oldGeneration = request.Begin();
        var oldLoad = request.Start(new SongDetailKey(oldGeneration, "pack", "old"), () =>
        {
            oldStarted.Set();
            if (!releaseOld.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("old detail load was not released");
            return "obsolete";
        });
        check(oldStarted.Wait(TimeSpan.FromSeconds(5)),
            "a detail request loads on a background worker");

        var newGeneration = request.Begin();
        var newKey = new SongDetailKey(newGeneration, "pack", "new");
        var newLoad = request.Start(newKey, () => "current");
        releaseOld.Set();
        Task.WhenAll(oldLoad, newLoad).GetAwaiter().GetResult();
        check(request.TryTakeCompleted(out var result) && result.Key == newKey &&
              result.Value == "current" && result.Error is null,
            "starting a new detail request cancels the older one and publishes only the newest");
        check(!request.TryTakeCompleted(out _), "a detail completion is consumed exactly once");
        check(!request.IsCurrentGeneration(oldGeneration) && request.IsCurrentGeneration(newGeneration),
            "only the newest generation stays current after a cancelled load");
    }

    private static void InvalidateDropsUnconsumed(Action<bool, string> check)
    {
        using var request = new SongDetailRequest<string>();
        var generation = request.Begin();
        request.Start(new SongDetailKey(generation, "pack", "chart"), () => "unconsumed")
            .GetAwaiter().GetResult();
        // 完成已入队，但主线程还没取走它。
        var invalidated = request.Invalidate();
        check(invalidated > generation && !request.TryTakeCompleted(out _),
            "invalidating drops a completion the owner thread has not consumed yet");
    }

    private static void DisposeRejectsNewRequests(Action<bool, string> check)
    {
        var request = new SongDetailRequest<string>();
        using var running = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var generation = request.Begin();
        var inFlight = request.Start(new SongDetailKey(generation, "pack", "chart"), () =>
        {
            running.Set();
            if (!finish.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("in-flight detail load was not released");
            return "late";
        });
        check(running.Wait(TimeSpan.FromSeconds(5)),
            "the dispose fixture starts its in-flight detail load");

        request.Dispose();
        request.Dispose();
        finish.Set();
        inFlight.GetAwaiter().GetResult();
        check(!request.TryTakeCompleted(out _), "disposing drops the late detail completion");
        check(!request.IsCurrentGeneration(generation),
            "a disposed detail request reports no current generation");
        try
        {
            request.Start(new SongDetailKey(generation, "pack", "chart"), () => "invalid");
            check(false, "a disposed detail request rejects new loads");
        }
        catch (ObjectDisposedException)
        {
            check(true, "a disposed detail request rejects new loads");
        }
    }

    private static void FailuresArriveAsData(Action<bool, string> check)
    {
        using var request = new SongDetailRequest<string>();
        var generation = request.Begin();
        var boom = new SongDetailKey(generation, "pack", "boom");
        request.Start(boom, () => throw new InvalidOperationException("fixture failure"))
            .GetAwaiter().GetResult();
        check(request.TryTakeCompleted(out var failure) && failure.Key == boom &&
              failure.Value is null && failure.Error is InvalidOperationException &&
              request.IsCurrent(failure.Key, boom.PackId, boom.ChartId),
            "detail load failures arrive as data and keep the request identity");
    }
}
