using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Covers the real sidebar command, Dock host, close paths and simulated stage IPC without Godot.</summary>
internal static class CoverStudioModalVerification
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        await CheckModalFailure(check);
        var pack = Path.Combine(root, "pack");
        Verification.CreatePackage(pack);
        var owner = new MainWindow(Path.Combine(root, "preferences"), () => { });
        try
        {
            owner.Show();
            await owner.OpenPackageForVerification(pack);
            owner.StageEventForVerification("ready", """{"hwnd":0}""");
            var document = owner.CurrentDocument!;
            var version = document.ChangeVersion;
            owner.LayoutFactory.FloatStage();
            Dispatcher.UIThread.RunJobs();
            if (TopLevel.GetTopLevel(owner.Stage) is not Window floating || floating == owner)
                throw new InvalidOperationException("Cover modal fixture: stage is not floating.");
            var body = owner.FindControl<Grid>("WorkbenchBody")!;
            var layer = owner.FindControl<Border>("WorkbenchModalLayer")!;
            var entry = owner.GetVisualDescendants().OfType<ToolsView>().Single().FindControl<Button>("CoverStudioButton")!;
            void ClickEntry() => entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, entry));
            CoverStudioWindow Open()
            {
                ClickEntry();
                Dispatcher.UIThread.RunJobs();
                return owner.OwnedWindows.OfType<CoverStudioWindow>().Single();
            }
            void Place(long epoch) => owner.StageEventForVerification("place",
                $$"""{"type":0,"track":1,"bar":2,"center":2.5,"width":1,"tailBar":2,"workbenchEpoch":{{epoch}}}""");
            void Frame(int second) => owner.StageEventForVerification("tick",
                $$"""{"second":{{second}},"bar":1,"playing":true,"playerSpeed":1,"playbackRate":1,"revision":{{owner.StageRevision}}}""");

            Frame(1);
            floating.Close(); // Queues a restore; opening the modal before the dispatcher runs must invalidate it.
            var cover = Open();
            ClickEntry();
            check("Cover modal locks the floating stage and admits only one independently owned window",
                cover.IsVisible && !body.IsEnabled && !floating.IsEnabled && !layer.IsVisible &&
                owner.OwnedWindows.OfType<CoverStudioWindow>().Single() == cover);
            floating.Close();
            Dispatcher.UIThread.RunJobs();
            check("Cover modal prevents closing or re-docking the floating stage behind it", floating.IsVisible &&
                TopLevel.GetTopLevel(owner.Stage) == floating);
            Place(0);
            Place(1);
            owner.StageEventForVerification("key", """{"key":"E","ctrl":false,"shift":false,"alt":false,"workbenchEpoch":1}""");
            owner.StageEventForVerification("contextAction", """{"kind":"track","value":"Right","workbenchEpoch":1}""");
            Frame(3);
            check("Cover modal rejects authoring IPC but continues to accept the running preview clock",
                document.ChangeVersion == version && owner.PlayerSpeed == 1 && owner.PlacementTrackForVerification.ToString() == "Center" &&
                owner.Playing && owner.CurrentSecond == 3);
            void CancelClose(object? _, WindowClosingEventArgs e) => e.Cancel = true;
            cover.Closing += CancelClose;
            owner.Close();
            Dispatcher.UIThread.RunJobs();
            check("Canceled cover closure keeps the owner and floating stage locked", owner.IsVisible &&
                cover.IsVisible && !body.IsEnabled && !floating.IsEnabled);
            cover.Closing -= CancelClose;
            owner.Close();
            Dispatcher.UIThread.RunJobs();
            check("Owner close dismisses only the cover modal and restores the floating stage", owner.IsVisible &&
                !cover.IsVisible && body.IsEnabled && floating.IsEnabled && TopLevel.GetTopLevel(owner.Stage) == floating);
            Place(0);
            Place(1);
            owner.StageEventForVerification("place", """{"type":0,"track":1,"bar":2,"center":2.5,"width":1,"tailBar":2}""",
                stampCurrentEpoch: false);
            check("Cover modal drops pre-dialog, mid-dialog and unversioned authoring IPC after release", document.ChangeVersion == version);
            Place(2);
            check("Cover modal accepts new authoring IPC once the matching epoch arrives", document.ChangeVersion > version);

            floating.IsEnabled = false;
            Open().Close();
            Dispatcher.UIThread.RunJobs();
            check("Direct cover close restores the floating stage's previously disabled state",
                owner.IsVisible && body.IsEnabled && floating.IsVisible && !floating.IsEnabled);
            floating.IsEnabled = true;
            owner.LayoutFactory.DockStage();
        }
        finally
        {
            foreach (var cover in owner.OwnedWindows.OfType<CoverStudioWindow>().ToArray()) cover.Close();
            Dispatcher.UIThread.RunJobs();
            owner.ClosePromptForVerification = () => Task.FromResult(UnsavedCloseChoice.Discard);
            owner.Close();
            await owner.CloseDecisionForVerification;
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task CheckModalFailure(Action<string, bool> check)
    {
        var background = new Border { IsEnabled = false };
        var owner = new Window { Content = background };
        var alreadyClosed = new Window();
        owner.Show();
        alreadyClosed.Show();
        alreadyClosed.Close();
        try
        {
            var commands = new List<(bool Blocked, long Epoch, bool PausePlayback)>();
            var modal = new WorkbenchModalHost(owner, background, new Border(), new ContentControl(), () => [],
                (blocked, epoch, pausePlayback) => commands.Add((blocked, epoch, pausePlayback)));
            var rejected = false;
            try { await modal.ShowDialog(alreadyClosed); }
            catch (InvalidOperationException) { rejected = true; }
            check("Failed cover dialog startup releases the modal and preserves the original enabled state",
                rejected && !modal.IsOpen && !background.IsEnabled && commands.SequenceEqual([(true, 1L, false), (false, 2L, false)]));
            commands.Clear();
            modal.Show((_, _) => new Border());
            modal.Close();
            check("Existing sheet modal keeps its pause policy and shares the monotonic epoch",
                commands.SequenceEqual([(true, 3L, true), (false, 4L, false)]));
        }
        finally { owner.Close(); }
    }
}
