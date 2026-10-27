using DynamiteUniverse.Shared.IO;

namespace CoreTests;

internal static class LatestRequestLoaderTests
{
    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("  Latest-request cancellation, stale completion and disposal");
        using (var loader = new LatestRequestLoader<string, string>())
        using (var oldLoadStarted = new ManualResetEventSlim())
        using (var finishOldLoad = new ManualResetEventSlim())
        {
            var oldLoad = loader.Start("old", () =>
            {
                oldLoadStarted.Set();
                if (!finishOldLoad.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("old load was not released");
                return "obsolete";
            });
            check(oldLoadStarted.Wait(TimeSpan.FromSeconds(5)),
                "latest-request loader starts blocking work on a background worker");

            using var latestLoadStarted = new ManualResetEventSlim();
            var latestLoad = loader.Start("latest", () =>
            {
                latestLoadStarted.Set();
                return "current";
            });
            check(!latestLoadStarted.IsSet,
                "latest-request loader serializes blocking loads while the old task unwinds");

            finishOldLoad.Set();
            Task.WhenAll(oldLoad, latestLoad).GetAwaiter().GetResult();
            check(latestLoadStarted.IsSet && loader.TryTakeCompleted(out var result) &&
                  result.Key == "latest" && result.Value == "current" && result.Error is null,
                "latest-request loader publishes the newest result and discards stale completions");
            check(!loader.TryTakeCompleted(out _), "latest-request completion is consumed exactly once");
        }

        using (var loader = new LatestRequestLoader<string, string>())
        using (var running = new ManualResetEventSlim())
        using (var finish = new ManualResetEventSlim())
        {
            var inFlight = loader.Start("running", () =>
            {
                running.Set();
                if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                return "late";
            });
            check(running.Wait(TimeSpan.FromSeconds(5)), "dispose fixture starts its in-flight load");
            var queuedRan = false;
            var queued = loader.Start("queued", () => { queuedRan = true; return "queued"; });
            loader.Dispose();
            loader.Dispose();
            finish.Set();
            Task.WhenAll(inFlight, queued).GetAwaiter().GetResult();
            check(!queuedRan && !loader.TryTakeCompleted(out _),
                "disposing cancels queued work and drops late completion without disposing an in-use gate");
            try
            {
                loader.Start("after-dispose", () => "invalid");
                check(false, "disposed loader rejects a new request");
            }
            catch (ObjectDisposedException)
            {
                check(true, "disposed loader rejects a new request");
            }
        }

        using (var loader = new LatestRequestLoader<string, string>())
        {
            loader.Start("failure", () => throw new InvalidOperationException("fixture failure"))
                .GetAwaiter().GetResult();
            check(loader.TryTakeCompleted(out var failure) && failure.Key == "failure" &&
                  failure.Value is null && failure.Error is InvalidOperationException,
                "latest-request loader delivers worker errors as data for owner-thread handling");

            var cancelledLoad = loader.Start("cancelled", () => "must not publish");
            loader.Cancel();
            cancelledLoad.GetAwaiter().GetResult();
            check(!loader.TryTakeCompleted(out _), "cancelled latest-request results are suppressed");
        }
    }
}
