using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Exercise the real form and session, including cancelled probes that finish late.</summary>
internal static class AddDifficultyFormVerification
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        Directory.CreateDirectory(root);
        var dir = Path.Combine(root, "saved");
        Verification.CreatePackage(dir);
        var session = new EditorDocumentSession();
        var d = session.Open(dir);
        var reference = d.SelectedChart;
        d.Execute(new AddBpmCommand(ExactBarTime.FromFraction(13, 3), 171.25));
        d.Execute(new EditChartOffsetCommand(.25));
        d.Execute(new AddScrollCommand(ExactBarTime.Zero, 1.5));
        d.Execute(new EditPreviewCommand(true, null, 1, 4));
        d.Execute(new EditPreviewCommand(false, reference.Id, 2, 3));
        var shortWav = Path.Combine(root, "short.wav");
        WriteShortWave(shortWav);
        var lang = WorkbenchLocalizer.Shared.CurrentLanguage;
        var window = new Window { Width = 520, Height = 390 };
        try
        {
            var form = new AddDifficultyForm(session, CancellationToken.None);
            window.Content = form;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            T Control<T>(string name) where T : Control => form.FindControl<T>(name)!;
            void Set(string name, string value) => Control<TextBox>(name).Text = value;
            var dismissals = 0;
            form.DismissRequested += () => dismissals++;
            var confirm = Control<Button>("AddButton");
            var cancel = Control<Button>("CancelButton");
            var feedback = Control<TextBlock>("Feedback");
            var feedbackViewport = Control<ScrollViewer>("FeedbackViewport");
            window.UpdateLayout();
            var confirmPosition = confirm.TranslatePoint(default, window);
            var cancelPosition = cancel.TranslatePoint(default, window);
            check("Difficulty sheet has no second card and keeps both footer actions in a 390 DIP window", form.Content is Grid &&
                confirmPosition is { } p && cancelPosition is { } q && p.Y >= 0 && p.Y + confirm.Bounds.Height <= window.Bounds.Height + 1 &&
                Math.Abs(p.Y - q.Y) < 1 && Math.Abs(p.X - q.X - cancel.Bounds.Width - 8) < 1 &&
                confirm.TranslatePoint(default, form) is { } local && Math.Abs(local.X + confirm.Bounds.Width - form.Bounds.Width) < 1 &&
                confirm.Bounds.Height == 36 && cancel.Bounds.Height == 36 && confirm.Classes.Contains("accent") && !cancel.Classes.Contains("accent"));
            check("Difficulty identity inputs use the same 32 DIP height", Control<TextBox>("ChartIdValue").Bounds.Height == 32 &&
                Control<ComboBox>("DifficultyChoice").Bounds.Height == 32 && Control<NumericUpDown>("LevelValue").Bounds.Height == 32);
            feedback.Text = string.Join("\n", Enumerable.Repeat("Difficulty form feedback", 12));
            window.UpdateLayout();
            check("Long difficulty feedback scrolls without shifting the action row", feedbackViewport.Bounds.Height == 32 &&
                feedbackViewport.Extent.Height > feedbackViewport.Viewport.Height && confirmPosition is not null &&
                confirmPosition == confirm.TranslatePoint(default, window) && cancelPosition == cancel.TranslatePoint(default, window));
            feedback.Text = "";
            check("Difficulty sheet defaults to current timing and chart preview without Scroll", Control<ComboBox>("TimingChoice").SelectedIndex == 1 &&
                Control<CheckBox>("CopyScroll").IsChecked == false && Control<CheckBox>("PreviewOverride").IsChecked == true && Control<TextBox>("PreviewLength").Text == "3");
            WorkbenchLocalizer.Shared.SetLanguage("zh-CN");
            var text = WorkbenchLocalizer.Shared;
            check("Difficulty sheet localizes the reference without echoing valid custom preview fields", text.CurrentLanguage == "zh-CN" &&
                Control<TextBlock>("ReferenceInfo").Text == text.Format("AddForm.Reference", reference.Id, reference.Bpms.Count, "0.25") &&
                Control<TextBox>("PreviewStart").Text == "2" && Control<TextBox>("PreviewLength").Text == "3" &&
                !Control<TextBlock>("PreviewInfo").IsVisible);
            Set("PreviewLength", "0");
            Dispatcher.UIThread.RunJobs();
            check("Invalid custom preview still displays an inline error", Control<TextBlock>("PreviewInfo").IsVisible &&
                Control<TextBlock>("PreviewInfo").Text == text["AddForm.BadPreview"]);
            Set("PreviewLength", "3");
            Set("ChartIdValue", "fresh");
            Control<ComboBox>("DifficultyChoice").SelectedIndex = (int)V2Difficulty.Custom;
            Set("CustomKeyValue", "Alt cut");
            Control<ComboBox>("AudioChoice").SelectedIndex = (int)DifficultyAudioSource.External;
            Set("AudioPathValue", shortWav);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var path = Control<TextBox>("AudioPathValue");
            var browse = Control<Grid>("FileField").Children.OfType<Button>().Single();
            check("External audio path and its inline button use the same 32 DIP height", path.Bounds.Height == 32 &&
                browse.Bounds.Height == 32 && Math.Abs(path.Bounds.Y - browse.Bounds.Y) < 1);
            await form.SubmitAsync();
            check("New recording requires a timing choice before probing", !form.Added && dismissals == 0 &&
                Control<ComboBox>("TimingChoice").SelectedIndex == 0 && Control<TextBlock>("Feedback").Text!.Contains("对轨"));
            Control<ComboBox>("TimingChoice").SelectedIndex = 2;
            Set("BpmValue", "98.5"); Set("OffsetValue", "-0.125");
            await form.SubmitAsync();
            check("Short recording and inherited excerpt keep the form open with inputs intact", d.Charts.Count == 1 && dismissals == 0 &&
                Control<TextBox>("ChartIdValue").Text == "fresh" && Control<TextBlock>("Feedback").Text!.Contains("试听"));
            Control<CheckBox>("PreviewOverride").IsChecked = true;
            Set("PreviewStart", "0"); Set("PreviewLength", "0.75");
            await form.SubmitAsync();
            check("A new excerpt fixes preflight in the same sheet; only one difficulty is added", form.Added && dismissals == 1 &&
                d.Charts.Count == 2 && d.SelectedChart.Id == "fresh" && d.SelectedChart.DifficultyKey == "Alt cut" &&
                d.SelectedChart.Preview == new V2Preview(0, .75) && d.SelectedChart.Bpms.Single().Bpm == 98.5 && d.SelectedChart.AudioOffsetSec == -.125);
            await form.SubmitAsync();
            check("A closed sheet cannot submit twice", dismissals == 1 && d.Charts.Count == 2);
            // A different package recording needs an explicit timing choice even without a browser dialog.
            form = new AddDifficultyForm(session, CancellationToken.None);
            window.Content = form;
            Control<ComboBox>("AudioChoice").SelectedIndex = (int)DifficultyAudioSource.PackageDefault;
            check("Package default differs from the reference override and uses the package excerpt", Control<ComboBox>("TimingChoice").SelectedIndex == 0 &&
                !Control<CheckBox>("PreviewOverride").IsChecked!.Value && Control<TextBlock>("PreviewInfo").Text!.Contains("4"));
            Set("CustomKeyValue", "Main recording");
            Control<ComboBox>("TimingChoice").SelectedIndex = 1;
            await form.SubmitAsync();
            check("Package source submits an inheritance reference, not a duplicate recording", form.Added &&
                d.SelectedChart.Audio is null && d.SelectedChart.Preview is null && d.SelectedChart.Bpms.Single().Bpm == 98.5);
            // v2 also allows a package with no default recording, if each chart has its own.
            var chartOnlySession = new EditorDocumentSession();
            var chartOnly = chartOnlySession.Open(dir);
            chartOnly.SelectedChart.Audio = chartOnly.Audio;
            chartOnly.Audio = null;
            form = new AddDifficultyForm(chartOnlySession, CancellationToken.None);
            window.Content = form;
            check("A package with only chart-level audio disables the absent pack default", !Control<ComboBoxItem>("PackAudioOption").IsEnabled);
            await form.SubmitAsync();
            check("Current recording still creates an empty difficulty without a pack default", form.Added &&
                chartOnly.SelectedChart.Audio is not null && chartOnly.SelectedChart.Notes.Count == 0);
            d.Undo();
            d.Undo();

            // Supply the validated plan despite cancellation, as a decoder that cannot stop mid-read might do.
            async Task TestLateCompletion(bool cancelViaHost)
            {
                using var lifetime = new CancellationTokenSource();
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                int attempts = 0, closed = 0;
                var cancellable = new AddDifficultyForm(session, lifetime.Token, async (request, ct) =>
                {
                    attempts++;
                    var validated = await session.PrepareDifficultyAsync(request, cancellationToken: ct);
                    ready.SetResult();
                    await resume.Task;
                    return validated;
                });
                cancellable.DismissRequested += () => closed++;
                window.Content = cancellable;
                var pending = cancellable.SubmitAsync();
                if (await Task.WhenAny(ready.Task, pending, Task.Delay(TimeSpan.FromSeconds(10))) != ready.Task)
                {
                    resume.TrySetResult();
                    throw new InvalidOperationException("Difficulty probe did not reach the controlled cancellation point: " +
                        cancellable.FindControl<TextBlock>("Feedback")!.Text);
                }
                await cancellable.SubmitAsync();
                if (cancelViaHost) { lifetime.Cancel(); window.Content = null; }
                else cancellable.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                resume.SetResult();
                await pending;
                check($"{(cancelViaHost ? "Host close" : "Cancel button")} rejects late completion and double submit", attempts == 1 &&
                    !cancellable.Added && d.Charts.Count == 1 && closed == (cancelViaHost ? 0 : 1));
            }
            await TestLateCompletion(false);
            await TestLateCompletion(true);
            form = new AddDifficultyForm(session, CancellationToken.None);
            int escapes = 0;
            form.DismissRequested += () => escapes++;
            window.Content = form;
            window.Activate();
            Control<TextBox>("ChartIdValue").Focus();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            await form.SubmitAsync();
            check("Escape from a text field cancels the sheet and blocks a later submit", escapes == 1 && !form.Added && d.Charts.Count == 1);
            form = new AddDifficultyForm(session, CancellationToken.None);
            window.Content = form;
            Control<CheckBox>("CopyScroll").IsChecked = true;
            await form.SubmitAsync();
            check("Default sheet copies complete timing and optional Scroll without notes", form.Added &&
                d.SelectedChart.Notes.Count == 0 && d.SelectedChart.Bpms.Count == 2 && d.SelectedChart.Bpms[1].Time == ExactBarTime.FromFraction(13, 3) &&
                d.SelectedChart.AudioOffsetSec == .25 && d.SelectedChart.ScrollSpeeds.Count == 1);
        }
        finally
        {
            window.Content = null;
            window.Close();
            WorkbenchLocalizer.Shared.SetLanguage(lang);
        }
    }

    private static void WriteShortWave(string path)
    {
        const int rate = 8000;
        using var output = new BinaryWriter(File.Create(path));
        output.Write("RIFF"u8); output.Write(36 + rate * 2); output.Write("WAVEfmt "u8); output.Write(16);
        output.Write((short)1); output.Write((short)1); output.Write(rate); output.Write(rate * 2);
        output.Write((short)2); output.Write((short)16); output.Write("data"u8); output.Write(rate * 2);
        output.Write(new byte[rate * 2]);
    }
}
