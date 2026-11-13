using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor.Shell;

internal static class ReplacementGuardVerification
{
    internal static async Task Run(string directory, Action<string, bool> check)
    {
        Verification.CreatePackage(directory);
        var session = new EditorDocumentSession();
        var original = session.Open(directory);
        var pending = session.OpenAsync(directory);
        original.Execute(new SetNoteScrollSpeedMultiplierCommand(original.SelectedChart.Notes[0].Id, 1.5));
        try { await pending; check("Concurrent edit rejects replacement", false); }
        catch (InvalidOperationException)
        {
            check("Concurrent edit rejects replacement", ReferenceEquals(original, session.Document) &&
                original.HasUnsavedChanges && original.SelectedChart.Notes[0].ScrollSpeedMultiplier == 1.5);
        }
        var replaced = await session.OpenAsync(directory);
        check("Uncontended load adopts prepared document", ReferenceEquals(replaced, session.Document) &&
            !ReferenceEquals(original, replaced));
        using var service = new GodotStageService();
        var errors = 0;
        service.RuntimeError += _ => errors++;
        service.ParseLine("DYNA_STAGE {\"event\":\"audioSnapshot\",\"requestId\":9223372036854775808}");
        service.ParseLine("DYNA_STAGE {\"event\":\"audioSnapshot\",\"requestId\":1,\"second\":0}");
        check("Malformed stage numbers and missing fields are isolated", errors == 2);
        var snapshots = (System.Collections.Concurrent.ConcurrentDictionary<long, TaskCompletionSource<StageAudioSnapshot>>)
            typeof(GodotStageService).GetField("_audioSnapshots", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(service)!;
        var pendingReply = new TaskCompletionSource<StageAudioSnapshot>();
        snapshots[7] = pendingReply;
        service.ParseLine("DYNA_STAGE {\"event\":\"audioSnapshot\",\"requestId\":7,\"second\":0,\"audioSecond\":0,\"audioPlaying\":false,\"audioPaused\":false,\"panSuspended\":false,\"audioLoads\":2147483648,\"audioRestarts\":0}");
        check("Bad correlated reply faults its waiter instead of timing out", pendingReply.Task.IsFaulted && !snapshots.ContainsKey(7));
        _ = pendingReply.Task.Exception;
        service.Dispose();
        service.Send(new { command = "play", playing = true });
        check("Disposed service ignores writes", !service.Running && service.ProcessId == 0);
    }
}
