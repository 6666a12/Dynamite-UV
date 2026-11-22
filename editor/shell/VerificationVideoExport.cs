using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DynamiteUniverse.Editor.Shell.VideoExport;

namespace DynamiteUniverse.Editor.Shell;

internal static class VerificationVideoExport
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    internal static async Task Run(string root, Action<string, bool> check)
    {
        Directory.CreateDirectory(root);
        var localizer = WorkbenchLocalizer.Shared;
        var originalLanguage = localizer.CurrentLanguage;
        var owner = new Window { Width = 600, Height = 500 };
        owner.Show();
        try
        {
            localizer.SetLanguage("en");
            await Settings(owner, root, localizer, check);
            localizer.SetLanguage("zh-CN");
            await LimitedSettings(owner, root, localizer, check);
            await CancelProbe(owner, root, localizer, check);
            await CancelProgress(owner, localizer, check);
            await LowSpace(owner, root, localizer, check);
            await VerificationVideoExportBoundaries.Run(root, localizer, check);
            await VerificationFfmpegDiscovery.Run(root, check);
        }
        finally
        {
            owner.Close();
            localizer.SetLanguage(originalLanguage);
        }
    }

    private static async Task Settings(Window owner, string root, WorkbenchLocalizer text, Action<string, bool> check)
    {
        var existing = Path.Combine(root, "existing.mp4");
        var newOutput = Path.Combine(root, "edited.mp4");
        var ffmpeg = Path.Combine(root, "ffmpeg.exe");
        File.WriteAllText(existing, "unchanged");
        var probe = new TaskCompletionSource<VideoExportCapabilities>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? probedPath = null;
        var dialog = new VideoExportSettingsDialog(text,
            new("Synthetic Pulse", "HARD Lv.8", true, 5, existing), ffmpeg,
            (path, _) => { probedPath = path; return probe.Task; },
            (_, _) => throw new InvalidOperationException("Startup must use automatic detection."));
        var closed = dialog.ShowDialog<VideoExportOptions?>(owner);
        var start = Find<Button>(dialog, "StartButton");
        Dispatcher.UIThread.RunJobs();
        check("Video settings open in English and wait for FFmpeg capabilities", dialog.IsVisible &&
            dialog.Title == "Export Chart Video" && !start.IsEnabled && probedPath == ffmpeg &&
            Find<TextBlock>(dialog, "SourceText").Text == "Synthetic Pulse · HARD Lv.8" &&
            Find<TextBlock>(dialog, "UnsavedHint").IsVisible && Find<Button>(dialog, "LocateButton").Content is "Locate FFmpeg…");
        probe.SetResult(new(ffmpeg, ["h264", "hevc", "av1"]));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        var format = Find<ComboBox>(dialog, "CodecValue");
        var crf = Find<NumericUpDown>(dialog, "CrfValue");
        var availability = Find<TextBlock>(dialog, "EncoderStatus").Text;
        var defaults = new[] { 16m, 20m, 24m };
        var codecOptions = format.Items.Count == defaults.Length;
        for (var i = 0; i < defaults.Length; i++)
        {
            format.SelectedIndex = i;
            codecOptions &= start.IsEnabled && crf.Value == defaults[i];
        }
        check("All three probed video formats can be selected with independent CRF defaults", codecOptions &&
            availability is not null && new[] { "H.264", "HEVC", "AV1" }.All(s => availability.Contains(s, StringComparison.Ordinal)));
        var preview = Find<NumericUpDown>(dialog, "PreviewValue");
        var hold = Find<NumericUpDown>(dialog, "ResultHoldValue");
        check("Video timings default to two seconds and bound the preview to the effective source",
            preview.Value == 2m && preview.Minimum == 0m && preview.Maximum == 5m && hold.Value == 2m && hold.Minimum == 0m);
        var fallSpeed = Find<ComboBox>(dialog, "FallSpeedValue");
        check("Video note speed offers 0.1x through 2.0x and defaults to 1.0x",
            fallSpeed.Items.Count == 20 && fallSpeed.Items[0] is "0.1×" && fallSpeed.Items[19] is "2.0×" &&
            fallSpeed.SelectedItem is "1.0×");
        Click(start);
        check("Video settings refuse to overwrite an existing destination", !closed.IsCompleted && dialog.IsVisible &&
            File.ReadAllText(existing) == "unchanged" && Find<TextBlock>(dialog, "OutputHint").Text == text["VideoExport.OutputExists"]);
        Find<TextBox>(dialog, "OutputValue").Text = newOutput;
        preview.Value = 0;
        hold.Value = 0;
        Find<ComboBox>(dialog, "PresetValue").SelectedIndex = 2;
        crf.Value = 25;
        fallSpeed.SelectedIndex = 16;
        Click(start);
        var selected = await closed.WaitAsync(Timeout);
        check("Video settings preserve 1.7x note speed independently of encode speed and clip times", selected ==
            new VideoExportOptions(newOutput, ffmpeg, "av1", "high", 25, 0, 0, 17) && !File.Exists(newOutput));
    }

    private static async Task LimitedSettings(Window owner, string root, WorkbenchLocalizer text, Action<string, bool> check)
    {
        var dialog = new VideoExportSettingsDialog(text,
            new("演示谱", "普通", false, 0.75, Path.Combine(root, "无预热.mp4"), 5), null,
            (_, _) => Task.FromResult(new VideoExportCapabilities(Path.Combine(root, "ffmpeg.exe"), ["h264"])),
            (_, _) => throw new InvalidOperationException("Startup must use automatic detection."));
        var closed = dialog.ShowDialog<VideoExportOptions?>(owner);
        Dispatcher.UIThread.RunJobs();
        var start = Find<Button>(dialog, "StartButton");
        var preview = Find<NumericUpDown>(dialog, "PreviewValue");
        check("Video settings open in Chinese and clamp the default to a short preview", dialog.Title == "导出谱面视频" &&
            start.Content is "开始导出" && !Find<TextBlock>(dialog, "UnsavedHint").IsVisible &&
            preview.Value == .75m && preview.Maximum == .75m && start.IsEnabled &&
            Find<ComboBox>(dialog, "FallSpeedValue").SelectedItem is "0.5×");
        Find<ComboBox>(dialog, "CodecValue").SelectedIndex = 2;
        check("A format missing in the selected FFmpeg build is not silently substituted", !start.IsEnabled &&
            Find<TextBlock>(dialog, "CodecHint").Text == text["VideoExport.CodecUnavailable"]);
        dialog.Close();
        check("Closing video settings does not create options", await closed.WaitAsync(Timeout) is null);
    }

    private static async Task CancelProbe(Window owner, string root, IWorkbenchLocalizer text, Action<string, bool> check)
    {
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new VideoExportSettingsDialog(text, new("fixture", "hard", false, 0, Path.Combine(root, "probe.mp4")),
            null, async (_, token) =>
            {
                try { await Task.Delay(System.Threading.Timeout.Infinite, token); }
                finally { canceled.TrySetResult(); await cleaned.Task; }
                throw new InvalidOperationException("The probe was not canceled.");
            }, (_, _) => throw new InvalidOperationException("Startup must use automatic detection."));
        var closed = dialog.ShowDialog<VideoExportOptions?>(owner);
        Dispatcher.UIThread.RunJobs();
        dialog.Close();
        try
        {
            await canceled.Task.WaitAsync(Timeout);
            check("Video settings closure exposes unfinished FFmpeg cleanup to its owner",
                await closed.WaitAsync(Timeout) is null && !dialog.ProbeCleanup.IsCompleted);
        }
        finally { cleaned.TrySetResult(); }
        await dialog.ProbeCleanup.WaitAsync(Timeout);
    }

    private static async Task CancelProgress(Window owner, IWorkbenchLocalizer text, Action<string, bool> check)
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new VideoExportProgressDialog(text, async (progress, cancellation) =>
        {
            progress.Report(new(VideoExportPhase.Render, 0.25, "fixture"));
            try { await Task.Delay(System.Threading.Timeout.Infinite, cancellation); }
            catch (OperationCanceledException)
            {
                observed.TrySetResult();
                await releaseCleanup.Task;
                throw;
            }
        });
        var closed = dialog.ShowDialog<bool>(owner);
        Dispatcher.UIThread.RunJobs();
        var action = Find<Button>(dialog, "ActionButton");
        check("Video progress renders a measured phase and names all four steps", 
            Find<ProgressBar>(dialog, "ProgressValue").Value == 25 &&
            new[] { "Prepare", "Render", "Encode", "Validate" }.All(phase =>
                Find<TextBlock>(dialog, phase + "Step").Text!.Contains(text[$"VideoExport.Phase.{phase}"], StringComparison.Ordinal)));
        try
        {
            Click(action);
            await observed.Task.WaitAsync(Timeout);
            check("Cancel reaches the export token and waits for cleanup without closing",
                !action.IsEnabled && dialog.IsVisible && !closed.IsCompleted &&
                Find<TextBlock>(dialog, "Status").Text == text["VideoExport.Canceling"]);
        }
        finally { releaseCleanup.TrySetResult(); }
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        // The cancellation observer may resume before the dialog's completion continuation.
        var deadline = DateTime.UtcNow + Timeout;
        while (!action.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(10);
        check("Video progress confirms cancellation after cleanup", action.IsEnabled &&
            Find<TextBlock>(dialog, "Status").Text == text["VideoExport.Canceled"]);
        Click(action);
        check("Canceled progress returns false to the workbench", !await closed.WaitAsync(Timeout));
    }

    private static async Task LowSpace(Window owner, string root, IWorkbenchLocalizer text, Action<string, bool> check)
    {
        var available = 1800 * VideoExportStorage.MiB;
        var guard = new VideoExportStorage(root, () => available);
        guard.CheckStarting();
        guard.CheckWriting();
        check("Video export starts below the old 2028 MiB preflight without pretending to reserve any bytes", true);

        available = VideoExportStorage.StartupFloor - 1;
        VideoExportLowSpaceException? rejected = null;
        try { guard.CheckStarting(); }
        catch (VideoExportLowSpaceException error) { rejected = error; }
        check("Video startup requires only the small safety floor", rejected is { FreeMiB: 63, ThresholdMiB: 64 });
        available = VideoExportStorage.WritingFloor;
        guard.CheckWriting();
        check("Streaming output validates exact frame counts without a tolerance",
            VideoExportStream.DecodedFrameCountMatches("frame=1\nprogress=continue\nframe=60\nprogress=end\n", 60) &&
            !VideoExportStream.DecodedFrameCountMatches("frame=59\nprogress=end\n", 60));

        var dialog = new VideoExportProgressDialog(text, (progress, _) =>
        {
            available -= VideoExportStorage.MiB;
            guard.CheckWriting();
            return Task.CompletedTask;
        });
        var result = dialog.ShowDialog<bool>(owner);
        Dispatcher.UIThread.RunJobs();
        check("Video writing reports real remaining space and the distinct writing threshold in Chinese",
            Find<SelectableTextBlock>(dialog, "Detail").Text == text.Format("VideoExport.LowSpace", root, 31, 32));
        Click(Find<Button>(dialog, "ActionButton"));
        check("Low disk space does not produce a success result", !await result.WaitAsync(Timeout));
    }

    private static T Find<T>(Window dialog, string name) where T : Control => dialog.FindControl<T>(name)!;
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
