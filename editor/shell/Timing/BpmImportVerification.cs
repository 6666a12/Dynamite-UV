using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

internal static class BpmImportVerification
{
    private const string Osu = """
        osu file format v14
        [General]
        AudioFilename: smoke.ogg
        [TimingPoints]
        1000,333.3333333333333,4,0,0,100,1,0
        1100,-50,4,0,0,100,0,0
        2333,500,3,0,0,100,1,0
        """;

    public static async Task Run(string root, Action<string, bool> check)
    {
        var pack = Path.Combine(root, "pack");
        Verification.CreatePackage(pack);
        var window = new MainWindow(Path.Combine(root, "preferences"));
        try
        {
            window.Show();
            await window.OpenPackageForVerification(pack);
            var document = window.CurrentDocument!;
            var chart = document.SelectedChart;
            document.Execute(new AddScrollCommand(ExactBarTime.Zero, 1.5));
            var before = V2JsonEncoder.EncodeChart(document.BuildChartSnapshot(chart.Id));
            var formData = new EditorCreateForm("shell-fixture", "Shell Fixture", "Community",
                Path.Combine(root, "fixture.wav"), "other", 3, "", 14, false, "Verification", 150, 0, null);
            await window.AddChartForVerification(formData);
            document.Execute(new AddNoteCommand(new EditableNote
            {
                Id = "other-note", Type = V2NoteType.Tap, Track = EditorTrack.Center,
                Time = ExactBarTime.One, Center = 2.5, Width = 1,
            }));
            var other = V2JsonEncoder.EncodeChart(document.BuildChartSnapshot("other"));
            window.SelectChartForVerification(chart.Id);
            var initialVersion = document.ChangeVersion;
            var layer = window.FindControl<Border>("WorkbenchModalLayer")!;
            var slot = window.FindControl<ContentControl>("WorkbenchModalSlot")!;
            var plugin = window.FindControl<Button>("PluginsMenu")!;
            var item = ((MenuFlyout)plugin.Flyout!).Items.OfType<MenuItem>().Single();
            BpmImportForm Open()
            {
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                return (BpmImportForm)slot.Content!;
            }
            static Task Load(BpmImportForm form, string name, string text) =>
                form.LoadAsync(name, () => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(text))));
            static Button Apply(BpmImportForm form) => form.FindControl<Button>("ApplyButton")!;

            var form = Open();
            check("Plugins → Import BPM opens the localized workbench sheet", layer.IsVisible &&
                Equals(item.Header, WorkbenchLocalizer.Shared["BpmImport.Title"]) && !Apply(form).IsEnabled);
            var apply = Apply(form);
            var cancel = form.FindControl<Button>("CancelButton")!;
            var feedbackViewport = form.FindControl<ScrollViewer>("FeedbackViewport")!;
            window.UpdateLayout();
            var applyPosition = apply.TranslatePoint(default, window);
            var cancelPosition = cancel.TranslatePoint(default, window);
            check("BPM import uses the shared card and a right-aligned 36 DIP action row", layer.Child is Border { Child: ContentControl cardSlot } &&
                ReferenceEquals(cardSlot, slot) && form.Content is Grid && apply.TranslatePoint(default, form) is { } p &&
                Math.Abs(p.X + apply.Bounds.Width - form.Bounds.Width) < 1 && applyPosition is { } a && cancelPosition is { } c &&
                Math.Abs(a.Y - c.Y) < 1 && Math.Abs(a.X - c.X - cancel.Bounds.Width - 8) < 1 &&
                apply.Bounds.Height == 36 && cancel.Bounds.Height == 36 && apply.Classes.Contains("accent") && !cancel.Classes.Contains("accent") &&
                form.FindControl<Button>("BrowseButton")!.Bounds.Height == 32);
            await Load(form, "bad.osu", "not timing data");
            window.UpdateLayout();
            check("Invalid timing leaves the sheet open and the document untouched", layer.IsVisible &&
                !Apply(form).IsEnabled && !string.IsNullOrEmpty(form.FindControl<TextBlock>("Feedback")!.Text) &&
                document.ChangeVersion == initialVersion);
            check("The BPM import error appears in the fixed feedback slot without moving either action", feedbackViewport.Bounds.Height == 32 &&
                applyPosition is not null && applyPosition == apply.TranslatePoint(default, window) && cancelPosition == cancel.TranslatePoint(default, window));
            await Load(form, "timing.osu", Osu);
            var snap = form.FindControl<CheckBox>("Snap")!;
            var displayed = form.FindControl<ListBox>("Points")!;
            check("osu! previews snapped timing, signed movement, and non-4/4 warning by default", snap.IsChecked == true &&
                displayed.Items.OfType<BpmImportPreviewRow>().Last().Bar == "1" &&
                displayed.Items.OfType<BpmImportPreviewRow>().Last().SnapMs == "+0.333333" &&
                !string.IsNullOrEmpty(form.FindControl<TextBlock>("Warning")!.Text) && document.ChangeVersion == initialVersion);
            snap.IsChecked = false;
            check("Turning off snapping previews original timing without modifying the chart", Apply(form).IsEnabled &&
                displayed.Items.OfType<BpmImportPreviewRow>().Last().Bar != "1" && document.ChangeVersion == initialVersion);
            window.ShortcutForVerification(Key.Escape);
            check("Esc cancels a prepared timing import with no undo entry", !layer.IsVisible && document.ChangeVersion == initialVersion);

            form = Open();
            await Load(form, "timing.osu", Osu);
            Apply(form).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var imported = V2JsonEncoder.EncodeChart(document.BuildChartSnapshot(chart.Id));
            check("Apply replaces only the selected chart's BPM and offset in one edit", !layer.IsVisible &&
                document.ChangeVersion == initialVersion + 1 && chart.AudioOffsetSec == 1 &&
                chart.Bpms.Count == 2 && chart.Bpms[1].Time == ExactBarTime.One &&
                chart.Notes.Count == 1 && chart.Notes[0].Time == ExactBarTime.FromFraction(2, 1) &&
                chart.ScrollSpeeds.Count == 1 && chart.ScrollSpeeds[0].Value == 1.5 &&
                other.SequenceEqual(V2JsonEncoder.EncodeChart(document.BuildChartSnapshot("other"), requireMainJudgement: false)));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            check("One workbench undo restores the previous tempo and offset byte-for-byte",
                before.SequenceEqual(V2JsonEncoder.EncodeChart(document.BuildChartSnapshot(chart.Id))));
            window.ShortcutForVerification(Key.Y, KeyModifiers.Control);
            check("One workbench redo restores the imported tempo and offset",
                imported.SequenceEqual(V2JsonEncoder.EncodeChart(document.BuildChartSnapshot(chart.Id))));

            form = Open();
            await Load(form, "timing.mc", """
                {"time":[{"beat":[0,0,1],"bpm":111},{"beat":[1,1,3],"bpm":123}]}
                """);
            check("Malody without BGM cannot apply until an audio offset is explicitly chosen", !Apply(form).IsEnabled &&
                !form.FindControl<CheckBox>("Snap")!.IsVisible);
            var entry = form.FindControl<TextBox>("ManualOffset")!;
            var keep = form.FindControl<Grid>("ManualOffsetPanel")!.Children.OfType<Button>().Single();
            window.UpdateLayout();
            check("Manual offset and its inline button use the same 32 DIP height", entry.Bounds.Height == 32 &&
                keep.Bounds.Height == 32 && Math.Abs(entry.Bounds.Y - keep.Bounds.Y) < 1);
            entry.Text = "invalid";
            check("An invalid manual offset cannot apply", !Apply(form).IsEnabled);
            entry.Text = "1e300";
            Dispatcher.UIThread.RunJobs();
            var priorVersion = document.ChangeVersion;
            Apply(form).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("An offset that collapses playback timing is rejected before replacing the chart", layer.IsVisible &&
                document.ChangeVersion == priorVersion && chart.AudioOffsetSec == 1);
            entry.Text = "-125.25";
            Dispatcher.UIThread.RunJobs();
            Apply(form).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("Malody import applies exact fractional beats and the chosen millisecond offset", !layer.IsVisible &&
                chart.Bpms[1].Time == ExactBarTime.FromFraction(1, 3) && chart.AudioOffsetSec == -0.12525);
            var expected = V2JsonEncoder.EncodeChart(document.BuildChartSnapshot(chart.Id));
            await window.SavePackageForVerification();
            var reopened = await new EditorDocumentSession().OpenAsync(pack);
            if (WorkbenchLayoutVerification.PreviewRequested)
            {
                var text = WorkbenchLocalizer.Shared;
                var language = text.CurrentLanguage;
                try
                {
                    text.SetLanguage("zh-CN");
                    await Load(Open(), "timing.osu", Osu.Replace("333.3333333333333", "400", StringComparison.Ordinal));
                    WorkbenchLayoutVerification.Capture(window, (Border)layer.Child!, "bpm-import-layout.png");
                    window.ShortcutForVerification(Key.Escape);
                }
                finally { text.SetLanguage(language); }
            }
            check("Imported timing round-trips through a strict package without affecting other charts", !document.HasUnsavedChanges &&
                expected.SequenceEqual(V2JsonEncoder.EncodeChart(reopened.BuildChartSnapshot(chart.Id))) &&
                other.SequenceEqual(V2JsonEncoder.EncodeChart(reopened.BuildChartSnapshot("other"))));
        }
        finally
        {
            window.ClosePromptForVerification = () => Task.FromResult(UnsavedCloseChoice.Discard);
            window.Close();
            if (window.IsVisible) window.Close();
            await window.CloseDecisionForVerification;
            Dispatcher.UIThread.RunJobs();
        }
    }
}
