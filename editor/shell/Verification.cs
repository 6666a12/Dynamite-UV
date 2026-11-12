using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using SukiUI.ControlsAnimation;
using SukiUI.Theme;
using Ursa.Controls;

namespace DynamiteUniverse.Editor.Shell;

internal static class Verification
{
    public static int RunHeadless()
    {
        Environment.SetEnvironmentVariable("DYNA_WORKBENCH_NO_GODOT", "1");
        AppBuilder.Configure<App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        var run = RunAsync();
        while (!run.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        return run.GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync()
    {
        var failed = false;
        void Check(string name, bool value)
        {
            Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}");
            failed |= !value;
        }
        var root = Path.Combine(Path.GetTempPath(), "dynamaker-editor-shell-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var copy = Path.Combine(root, "copy");
        var window = new MainWindow(Path.Combine(root, "user-data"));
        try
        {
            await SaveInteractionVerification.Run(Path.Combine(root, "save-interactions"), Check);
            StageLaunchVerification.Run(Path.Combine(root, "launch"), Check);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Check("Game preview UI entry is absent and starts disabled", !window.Stage.GamePreviewEnabled &&
                window.Stage.FindControl<CheckBox>("GamePreviewToggle") is null);
            Check("Real Dock layout", window.GetVisualDescendants().OfType<DockControl>()
                .Single().GetVisualDescendants().OfType<DocumentControl>().Any());
            Check("Ursa controls are templated", window.GetVisualDescendants().OfType<IconButton>()
                .All(control => control.GetVisualChildren().Any()));
            for (var cycle = 0; cycle < 2; cycle++)
            {
                window.LayoutFactory.FloatStage();
                Dispatcher.UIThread.RunJobs();
                var floating = TopLevel.GetTopLevel(window.Stage) as Window;
                Check($"Stage floats before close cycle {cycle + 1}", floating is not null && !ReferenceEquals(floating, window));
                if (floating is not null && !ReferenceEquals(floating, window)) floating.Close();
                Dispatcher.UIThread.RunJobs();
                Check($"Floating close restores stage cycle {cycle + 1}",
                    ReferenceEquals(TopLevel.GetTopLevel(window.Stage), window) && floating?.IsVisible == false);
                window.LayoutFactory.DockStage();
                Dispatcher.UIThread.RunJobs();
                Check($"Restore remains idempotent after close cycle {cycle + 1}",
                    ReferenceEquals(TopLevel.GetTopLevel(window.Stage), window));
            }
            WorkspaceVerification.Run(window, Check);
            WorkbenchPersistenceVerification.Run(Path.Combine(root, "persistence"), Check);
            Check("Q/E changes preview speed", window.ShortcutForVerification(Key.E) && window.PlayerSpeed == 1.1);
            Check("W/S changes playback rate", window.ShortcutForVerification(Key.W) &&
                window.PlaybackRate == 1.1 && window.ShortcutForVerification(Key.S) && window.PlaybackRate == 1.0);
            Check("1 selects Tap placement", window.ShortcutForVerification(Key.D1) && window.CurrentTool == "PLACE" && window.PlacementTypeForVerification == V2NoteType.Tap);
            Check("2 selects Drag placement", window.ShortcutForVerification(Key.D2) && window.CurrentTool == "PLACE" && window.PlacementTypeForVerification == V2NoteType.Drag);
            Check("3 selects Hold placement", window.ShortcutForVerification(Key.D3) && window.CurrentTool == "PLACE" && window.PlacementTypeForVerification == V2NoteType.Hold);
            Check("4 selects SELECT without assigning PATH or PAN a number", window.ShortcutForVerification(Key.D4) && window.CurrentTool == "SELECT");
            Check("5 selects ExTap placement", window.ShortcutForVerification(Key.D5) && window.PlacementTypeForVerification == V2NoteType.ExTap && window.CurrentTool == "PLACE");
            Check("6 selects Mixer placement", window.ShortcutForVerification(Key.D6) && window.PlacementTypeForVerification == V2NoteType.Mixer && window.CurrentTool == "PLACE");
            Check("7 selects Mine placement", window.ShortcutForVerification(Key.D7) && window.PlacementTypeForVerification == V2NoteType.Mine && window.CurrentTool == "PLACE");
            window.ShortcutForVerification(Key.D4);
            window.ShortcutForVerification(Key.Up);
            var leftTrack = window.PlacementTrackForVerification == EditorTrack.Left;
            window.ShortcutForVerification(Key.Up);
            var rightTrack = window.PlacementTrackForVerification == EditorTrack.Right;
            window.ShortcutForVerification(Key.Up);
            Check("Up cycles center-left-right-center", leftTrack && rightTrack && window.PlacementTrackForVerification == EditorTrack.Center);
            Check("Wheel direction defaults to existing navigation", !window.WheelReversed);
            Check("B reverses wheel direction", window.ShortcutForVerification(Key.B) && window.WheelReversed);
            Check("Modified B does not toggle wheel direction", !window.ShortcutForVerification(Key.B, KeyModifiers.Control) &&
                !window.ShortcutForVerification(Key.B, KeyModifiers.Shift) && window.WheelReversed);
            window.ShortcutForVerification(Key.B);
            Check("B restores wheel direction", !window.WheelReversed);
            window.StageEventForVerification("key", "{\"key\":\"B\",\"ctrl\":false,\"shift\":false,\"alt\":false}");
            Check("Native stage B uses the same wheel toggle", window.WheelReversed);
            window.ShortcutForVerification(Key.B);
            Check("F1 opens and Escape closes help", window.ShortcutForVerification(Key.F1) &&
                window.ShortcutsVisible && window.ShortcutForVerification(Key.Escape) && !window.ShortcutsVisible);
            CreatePackage(source);
            await window.OpenPackageForVerification(source);
            Check("Shell opens a strict v2 package", window.CurrentDocument?.Title == "Shell Fixture" &&
                window.CurrentDocument.SelectedChart.Notes.Count == 1);
            window.SelectNoteForVerification(window.CurrentDocument!.SelectedChart.Notes[0].Id);
            Check("Ctrl+C copies selected notes", window.ShortcutForVerification(Key.C, KeyModifiers.Control));
            window.SeekForVerification(2);
            Check("Ctrl+V pastes at playhead", window.ShortcutForVerification(Key.V, KeyModifiers.Control) &&
                window.CurrentDocument.SelectedChart.Notes.Count == 2 && window.SelectedNoteIds.Count == 1 &&
                window.CurrentDocument.SelectedChart.Notes.Single(n => n.Id == window.SelectedNoteIds[0]).Time == ExactBarTime.FromFraction(2, 1));
            SelectionInspectorVerification.Run(window, Check);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Paste undo removes copied group", window.CurrentDocument.SelectedChart.Notes.Count == 1);
            window.SeekForVerification(1);
            var beforeSeekSecond = window.CurrentSecond;
            window.ShortcutForVerification(Key.D);
            Check("D advances one second without grid snapping", Math.Abs(window.CurrentSecond - beforeSeekSecond - 1) < 1e-9);
            window.ShortcutForVerification(Key.A, KeyModifiers.Shift);
            Check("Shift A steps back exactly 0.01 seconds", Math.Abs(window.CurrentSecond - beforeSeekSecond - .99) < 1e-9);
            window.SeekForVerification(0);
            var timing = window.Timing;
            timing.FindControl<TextBox>("BpmTimeValue")!.Text = "1+1/2";
            timing.FindControl<Avalonia.Controls.NumericUpDown>("BpmValueInput")!.Value = 180;
            ClickControl(timing, "AddBpmButton");
            Check("Timing panel adds an exact BPM event", window.CurrentDocument?.SelectedChart.Bpms
                .Any(item => item.Time == ExactBarTime.FromFraction(3, 2) && item.Bpm == 180) == true);
            SelectTimingItem(timing, "BpmList", ExactBarTime.FromFraction(3, 2));
            timing.FindControl<Avalonia.Controls.NumericUpDown>("BpmValueInput")!.Value = 190;
            ClickControl(timing, "ApplyBpmButton");
            Check("Timing panel edits the selected BPM event", window.CurrentDocument?.SelectedChart.Bpms
                .Single(item => item.Time == ExactBarTime.FromFraction(3, 2)).Bpm == 190);
            SelectTimingItem(timing, "BpmList", ExactBarTime.Zero);
            timing.SetDocument(window.CurrentDocument);
            Check("Timing refresh restores the clicked row instead of the last commanded event",
                timing.FindControl<ListBox>("BpmList")!.SelectedItem is ListBoxItem { Tag: ExactBarTime clickedBpm } &&
                clickedBpm == ExactBarTime.Zero && window.CurrentDocument?.Selection is { Kind: EditorSelectionKind.Bpm });
            timing.FindControl<TextBox>("BpmTimeValue")!.Text = "1";
            ClickControl(timing, "ApplyBpmButton");
            Check("Editable base BPM time still rejects moving the required event",
                window.StatusMessage.Contains("cannot be moved", StringComparison.Ordinal) &&
                window.CurrentDocument!.SelectedChart.Bpms.Any(item => item.Time == ExactBarTime.Zero));
            SelectTimingItem(timing, "BpmList", ExactBarTime.FromFraction(3, 2));
            ClickControl(timing, "DeleteBpmButton");
            Check("Timing panel deletes a non-base BPM event", window.CurrentDocument?.SelectedChart.Bpms.Count == 1);
            for (var index = 0; index < 3; index++) window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            timing.FindControl<TextBox>("ScrollTimeValue")!.Text = "0";
            timing.FindControl<Avalonia.Controls.NumericUpDown>("ScrollValueInput")!.Value = 1;
            ClickControl(timing, "AddScrollButton");
            Check("Timing panel establishes the required bar-zero Scroll", window.CurrentDocument?.SelectedChart.ScrollSpeeds
                .SingleOrDefault(item => item.Time == ExactBarTime.Zero)?.Value == 1);
            timing.FindControl<TextBox>("ScrollTimeValue")!.Text = "2+1/2";
            timing.FindControl<Avalonia.Controls.NumericUpDown>("ScrollValueInput")!.Value = 1.5m;
            ClickControl(timing, "AddScrollButton");
            Check("Timing panel adds a tail Scroll without an illegal curve", window.CurrentDocument?.SelectedChart.ScrollSpeeds
                .Any(item => item.Time == ExactBarTime.FromFraction(5, 2) && item.Value == 1.5 && item.CurveToNext is null) == true);
            SelectTimingItem(timing, "ScrollList", ExactBarTime.FromFraction(5, 2));
            timing.FindControl<Avalonia.Controls.NumericUpDown>("ScrollValueInput")!.Value = 2;
            ClickControl(timing, "ApplyScrollButton");
            Check("Timing panel edits the selected Scroll event", window.CurrentDocument?.SelectedChart.ScrollSpeeds
                .Single(item => item.Time == ExactBarTime.FromFraction(5, 2)).Value == 2);
            ClickControl(timing, "DeleteScrollButton");
            Check("Timing panel deletes the selected Scroll event", window.CurrentDocument?.SelectedChart.ScrollSpeeds.Count == 1);
            SelectTimingItem(timing, "BpmList", ExactBarTime.Zero);
            SelectTimingItem(timing, "ScrollList", ExactBarTime.Zero);
            Check("Timing panel keeps a single exclusive timing selection", window.CurrentDocument?.Selection is { Kind: EditorSelectionKind.Scroll } &&
                timing.FindControl<ListBox>("BpmList")!.SelectedItem is null);
            for (var index = 0; index < 4; index++) window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Timing undo history restores both original timelines", window.CurrentDocument?.SelectedChart.Bpms.Count == 1 &&
                window.CurrentDocument.SelectedChart.ScrollSpeeds.Count == 0 && !window.CurrentDocument.HasUnsavedChanges);
            timing.FindControl<TextBox>("BpmTimeValue")!.Text = "1";
            timing.FindControl<Avalonia.Controls.NumericUpDown>("BpmValueInput")!.Value = 1200;
            ClickControl(timing, "AddBpmButton");
            Check("Timing panel accepts a BPM above the old 999 clamp", window.CurrentDocument?.SelectedChart.Bpms
                .Any(item => item.Time == ExactBarTime.FromFraction(1, 1) && item.Bpm == 1200) == true);
            SelectTimingItem(timing, "BpmList", ExactBarTime.FromFraction(1, 1));
            Check("Timing panel reloads a high BPM without clamping", timing.FindControl<Avalonia.Controls.NumericUpDown>("BpmValueInput")!.Value == 1200m);
            timing.FindControl<TextBox>("BpmTimeValue")!.Text = "2";
            ClickControl(timing, "ApplyBpmButton");
            Check("Timing time-only edit keeps a high BPM", window.CurrentDocument!.SelectedChart.Bpms
                .Any(item => item.Time == ExactBarTime.FromFraction(2, 1) && item.Bpm == 1200));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            SelectTimingItem(timing, "BpmList", ExactBarTime.FromFraction(1, 1));
            ClickControl(timing, "DeleteBpmButton");
            timing.FindControl<TextBox>("ScrollTimeValue")!.Text = "0";
            timing.FindControl<Avalonia.Controls.NumericUpDown>("ScrollValueInput")!.Value = 1;
            ClickControl(timing, "AddScrollButton");
            Check("Timing panel keeps the time field editable with the base event selected",
                !timing.FindControl<TextBox>("ScrollTimeValue")!.IsReadOnly);
            timing.FindControl<TextBox>("ScrollTimeValue")!.Text = "2";
            ClickControl(timing, "AddScrollButton");
            Check("Timing panel adds a Scroll while the base event is selected", window.CurrentDocument?.SelectedChart.ScrollSpeeds
                .Any(item => item.Time == ExactBarTime.FromFraction(2, 1)) == true);
            ClickControl(timing, "DeleteScrollButton");
            timing.FindControl<TextBox>("ScrollTimeValue")!.Text = "1";
            timing.FindControl<Avalonia.Controls.NumericUpDown>("ScrollValueInput")!.Value = 0.01m;
            ClickControl(timing, "AddScrollButton");
            Check("Timing panel accepts a Scroll below the old 0.05 clamp", window.CurrentDocument?.SelectedChart.ScrollSpeeds
                .Any(item => item.Time == ExactBarTime.FromFraction(1, 1) && item.Value == 0.01) == true);
            timing.FindControl<TextBox>("ScrollTimeValue")!.Text = "3";
            ClickControl(timing, "ApplyScrollButton");
            Check("Timing time-only edit keeps a small Scroll value", window.CurrentDocument!.SelectedChart.ScrollSpeeds
                .Any(item => item.Time == ExactBarTime.FromFraction(3, 1) && item.Value == 0.01));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            SelectTimingItem(timing, "ScrollList", ExactBarTime.FromFraction(1, 1));
            ClickControl(timing, "DeleteScrollButton");
            for (var index = 0; index < 7; index++) window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Timing regressions undo back to the clean timelines", window.CurrentDocument?.SelectedChart.Bpms.Count == 1 &&
                window.CurrentDocument.SelectedChart.ScrollSpeeds.Count == 0 && !window.CurrentDocument.HasUnsavedChanges);
            window.CurrentDocument!.Execute(new AddBpmCommand(ExactBarTime.FromFraction(1, 1), double.MaxValue));
            timing.SetDocument(window.CurrentDocument);
            Check("Timing loads a BPM beyond the decimal range without overflow",
                timing.FindControl<Avalonia.Controls.NumericUpDown>("BpmValueInput")!.Value == decimal.MaxValue);
            timing.FindControl<TextBox>("BpmTimeValue")!.Text = "2";
            ClickControl(timing, "ApplyBpmButton");
            Check("Timing time-only edit preserves a BPM beyond the decimal range",
                window.CurrentDocument.SelectedChart.Bpms.Any(item => item.Time == ExactBarTime.FromFraction(2, 1) && item.Bpm == double.MaxValue));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.CurrentDocument.Execute(new AddScrollCommand(ExactBarTime.Zero, 1));
            window.CurrentDocument.Execute(new AddScrollCommand(ExactBarTime.FromFraction(1, 1), double.Epsilon));
            timing.SetDocument(window.CurrentDocument);
            timing.FindControl<TextBox>("ScrollTimeValue")!.Text = "2";
            ClickControl(timing, "ApplyScrollButton");
            Check("Timing time-only edit preserves a positive Scroll below decimal precision",
                window.CurrentDocument.SelectedChart.ScrollSpeeds.Any(item => item.Time == ExactBarTime.FromFraction(2, 1) && item.Value == double.Epsilon));
            for (var index = 0; index < 3; index++) window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Extreme Timing regressions restore clean original timelines", !window.CurrentDocument.HasUnsavedChanges &&
                window.CurrentDocument.SelectedChart.Bpms.Count == 1 && window.CurrentDocument.SelectedChart.ScrollSpeeds.Count == 0);
            window.SelectNoteForVerification("fixture-note");
            window.ApplySpeedForVerification(1.35);
            Check("Inspector applies a real document command", window.CurrentDocument?.HasUnsavedChanges == true &&
                window.CurrentDocument.SelectedChart.Notes[0].ScrollSpeedMultiplier == 1.35);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Undo reverts Note speed", window.CurrentDocument?.SelectedChart.Notes[0].ScrollSpeedMultiplier == 1.0);
            window.ShortcutForVerification(Key.Y, KeyModifiers.Control);
            Check("Redo restores Note speed", window.CurrentDocument?.SelectedChart.Notes[0].ScrollSpeedMultiplier == 1.35);
            window.ApplyGeometryForVerification("2+1/2", 3.25, 1.5);
            Check("Inspector applies exact time, position and width", window.CurrentDocument?.SelectedChart.Notes[0] is { } edited &&
                edited.Time == ExactBarTime.FromFraction(5, 2) && edited.Center == 3.25 && edited.Width == 1.5);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Undo restores the complete geometry edit and selection", window.CurrentDocument?.SelectedChart.Notes[0] is { } restored &&
                restored.Time == ExactBarTime.FromFraction(2, 1) && restored.Center == 2.5 && restored.Width == 1 &&
                window.SelectedNoteId == "fixture-note");
            window.ApplyNoteKindForVerification(V2NoteType.Hold, EditorTrack.Left);
            Check("Inspector changes type and track as one command", window.CurrentDocument?.SelectedChart.Notes[0] is { } changed &&
                changed.Type == V2NoteType.Hold && changed.Track == EditorTrack.Left && changed.Nodes.Count == 1);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("One undo restores Note type, track and removed path", window.CurrentDocument?.SelectedChart.Notes[0] is { } original &&
                original.Type == V2NoteType.Tap && original.Track == EditorTrack.Center && original.Nodes.Count == 0);
            window.StageEventForVerification("noteMove",
                "{\"noteId\":\"fixture-note\",\"startBar\":2,\"endBar\":2.25,\"startCenter\":2.5,\"endCenter\":2.75}");
            Check("Stage Note drag applies exact document movement", window.CurrentDocument?.SelectedChart.Notes[0] is { } moved &&
                moved.Time == ExactBarTime.FromFraction(9, 4) && moved.Center == 2.75);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.StageEventForVerification("noteResize",
                "{\"noteId\":\"fixture-note\",\"moveStartEdge\":false,\"edge\":4}");
            Check("Stage edge drag resizes around the opposite edge", window.CurrentDocument?.SelectedChart.Notes[0] is { } resized &&
                resized.Center == 3 && resized.Width == 2 && resized.Time == ExactBarTime.FromFraction(2, 1));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.StageEventForVerification("noteResize",
                "{\"noteId\":\"fixture-note\",\"moveStartEdge\":false,\"edge\":3.12}");
            Check("Snapped edge resize keeps the original start edge fixed", window.CurrentDocument?.SelectedChart.Notes[0] is { } snappedResize &&
                Math.Abs(snappedResize.Center - snappedResize.Width / 2d - 2) < 1e-12 &&
                Math.Abs(snappedResize.Width - 1.1) < 1e-12);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.StageEventForVerification("place",
                "{\"type\":0,\"track\":1,\"bar\":1,\"center\":2.55555,\"width\":1.12345,\"tailBar\":1,\"snap\":false}");
            var precisionId = window.CurrentDocument!.SelectedChart.Notes.Single(note => note.Id != "fixture-note").Id;
            var inspector = window.GetVisualDescendants().OfType<InspectorView>().Single();
            Check("Inspector displays geometry with round-trip precision",
                inspector.FindControl<TextBox>("PositionValue")!.Text == "2.55555" &&
                inspector.FindControl<TextBox>("WidthValue")!.Text == "1.12345");
            ClickControl(inspector, "ApplyGeometryButton");
            Check("Inspector apply without edits keeps exact geometry",
                window.CurrentDocument.SelectedChart.Notes.Single(note => note.Id == precisionId) is { } precise &&
                precise.Center == 2.55555 && precise.Width == 1.12345);
            inspector.FindControl<TextBox>("BarTimeValue")!.Text = "1+1/3";
            ClickControl(inspector, "ApplyGeometryButton");
            Check("Inspector time-only edit preserves high-precision center and width",
                window.CurrentDocument.SelectedChart.Notes.Single(note => note.Id == precisionId) is { } preciseMoved &&
                preciseMoved.Time == ExactBarTime.FromFraction(4, 3) && preciseMoved.Center == 2.55555 && preciseMoved.Width == 1.12345);
            // A no-op Apply may or may not enter history. Stop at the placement rather than undoing
            // a fixed count and accidentally reverting the earlier fixture speed edit.
            for (var index = 0; index < 3 && window.CurrentDocument.SelectedChart.Notes.Any(note => note.Id == precisionId); index++)
                window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Precision regression cleanup preserves the original fixture", window.CurrentDocument.SelectedChart.Notes.Count == 1 &&
                window.CurrentDocument.SelectedChart.Notes.Single().ScrollSpeedMultiplier == 1.35);
            window.SelectNoteForVerification("fixture-note");
            window.ApplyGeometryForVerification("2+1/2", 2.5, 1);
            for (var index = 0; index < 13; index++) window.ShortcutForVerification(Key.C, KeyModifiers.Shift);
            window.StageEventForVerification("noteResize",
                "{\"noteId\":\"fixture-note\",\"moveStartEdge\":false,\"edge\":4}");
            Check("Resizing preserves an off-grid exact Note time", window.CurrentDocument?.SelectedChart.Notes[0] is { } exactResize &&
                exactResize.Time == ExactBarTime.FromFraction(5, 2) && exactResize.Width == 2);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            for (var index = 0; index < 13; index++) window.ShortcutForVerification(Key.V, KeyModifiers.Shift);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            SelectTimingItem(timing, "BpmList", ExactBarTime.Zero);
            Check("Timing list selection syncs into the document", window.CurrentDocument?.Selection is { Kind: EditorSelectionKind.Bpm });
            await window.SavePackageToForVerification(copy);
            Check("Timing list selection survives a full document refresh",
                timing.FindControl<ListBox>("BpmList")!.SelectedItem is ListBoxItem { Tag: ExactBarTime selectedTime } &&
                selectedTime == ExactBarTime.Zero && window.CurrentDocument?.Selection is { Kind: EditorSelectionKind.Bpm });
            var reopened = EditorPackageRepository.Open(copy);
            Check("Save as writes a valid package", reopened.SelectedChart.Notes[0].ScrollSpeedMultiplier == 1.35 &&
                window.CurrentDocument?.HasUnsavedChanges == false);
            window.SelectNoteForVerification("fixture-note");
            window.ShortcutForVerification(Key.Delete);
            Check("Delete executes a document command", window.SelectedNoteId is null &&
                window.CurrentDocument?.SelectedChart.Notes.Count == 0);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Undo restores deleted Note", window.CurrentDocument?.SelectedChart.Notes.Count == 1 &&
                window.CurrentDocument.SelectedChart.Notes[0].Id == "fixture-note");
            window.FindControl<ComboBox>("LanguageChoice")!.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Check("Chinese updates live controls", window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "文件") &&
                window.GetVisualDescendants().OfType<ComboBox>().Any(choice => choice.Classes.Contains("localized") &&
                    choice.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "线性")));
            window.FindControl<ComboBox>("LanguageChoice")!.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            Check("English restores live controls", window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "FILE"));
            window.FindControl<ComboBox>("MotionChoice")!.SelectedIndex = 1;
            Check("Motion profile remains compatible", SukiAnimation.GetProfile(window) == SukiAnimationProfile.Lite);

            var newForm = new EditorCreateForm("new-pack", "New Package", "Community Artist",
                Path.Combine(root, "fixture.wav"), "main", 2, "", 12, false, "Community", 120, 0, null);
            var createDialog = new CreateChartDialog(EditorCreateMode.NewPackage, null, 16);
            createDialog.Show();
            createDialog.SetFormForVerification(newForm);
            Check("New package form builds a typed draft request",
                createDialog.ConfirmForVerification()?.DraftRequest?.InitialBpm == 120);
            await window.CreatePackageForVerification(newForm);
            Check("New package creates an empty unsaved draft", window.CurrentDocument?.Title == "New Package" &&
                window.CurrentDocument.SelectedChart.Notes.Count == 0 && window.CurrentDocument.HasUnsavedChanges);
            var addForm = newForm with { ChartId = "extra", DifficultyIndex = 3, Level = 14 };
            await window.AddChartForVerification(addForm);
            Check("Add difficulty selects a real second chart", window.CurrentDocument?.Charts.Count == 2 &&
                window.CurrentDocument.SelectedChartId == "extra" && window.CurrentDocument.SelectedChart.Level == 14);
            var chartChoice = window.FindControl<ComboBox>("ChartChoice")!;
            var chartItems = chartChoice.Items.Cast<ComboBoxItem>().ToArray();
            var selectedEvents = 0;
            chartChoice.SelectionChanged += (_, _) => selectedEvents++;
            for (var cycle = 0; cycle < 20; cycle++)
            {
                chartChoice.SelectedIndex = cycle % 2;
                Dispatcher.UIThread.RunJobs();
                Check($"Empty difficulty selection keeps stable items {cycle}",
                    window.CurrentDocument!.SelectedChartId == (cycle % 2 == 0 ? "main" : "extra") &&
                    window.CurrentDocument.SelectedChart.Notes.Count == 0 &&
                    chartChoice.Items.Cast<ComboBoxItem>().SequenceEqual(chartItems));
            }
            Check("Difficulty changes raise one selection event each", selectedEvents == 20);
            window.StageEventForVerification("place", "{\"type\":0,\"track\":1,\"bar\":1,\"center\":2.5,\"width\":1,\"tailBar\":1}");
            window.SelectChartForVerification("main");
            window.ConfigurePlacementForVerification(V2NoteType.Hold, EditorTrack.Center, 1);
            window.StageEventForVerification("place", "{\"type\":0,\"track\":1,\"bar\":0.5,\"center\":2.5,\"width\":1,\"tailBar\":0.5}");
            window.StageEventForVerification("place", "{\"type\":3,\"track\":1,\"bar\":1,\"center\":2.5,\"width\":1,\"tailBar\":2}");
            Check("Stage placement adds Tap and Hold through document commands",
                window.CurrentDocument?.SelectedChart.Notes.Count == 2 &&
                window.CurrentDocument.SelectedChart.Notes.Any(note => note.Type == V2NoteType.Hold && note.Nodes.Count == 1));
            window.StageEventForVerification("place",
                "{\"type\":0,\"track\":1,\"bar\":1.23,\"center\":2.5,\"width\":1,\"tailBar\":1.23,\"snap\":false}");
            Check("Free placement preserves the exact unsnapped BarTime", window.CurrentDocument?.SelectedChart.Notes
                .Any(note => note.Time == ExactBarTime.FromFraction(123, 100)) == true);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            var savedGrid = window.CurrentDocument!.GridDivisor;
            foreach (var grid in new[] { 3, 5, 12 })
            {
                window.CurrentDocument.GridDivisor = grid;
                var bar = (1d / grid).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                window.StageEventForVerification("place",
                    $"{{\"type\":0,\"track\":1,\"bar\":{bar},\"center\":2.5,\"width\":1,\"tailBar\":{bar},\"snap\":true}}");
                var expected = ExactBarTime.FromFraction(1, grid);
                var snapshot = window.CurrentDocument.BuildChartSnapshot(window.CurrentDocument.SelectedChartId);
                var decoded = V2JsonDecoder.DecodeChart(V2JsonEncoder.EncodeChart(snapshot));
                var runtime = V2RuntimeAdapter.Adapt(decoded);
                Check($"Grid {grid} placement survives strict runtime roundtrip", runtime.RuntimeChart.AllNotes
                    .Any(note => note.V2Metadata?.ExactTime == expected && double.IsFinite(note.Second)));
                window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            }
            window.CurrentDocument.GridDivisor = savedGrid;
            var holdId = window.CurrentDocument!.SelectedChart.Notes.Single(note => note.Type == V2NoteType.Hold).Id;
            window.StageEventForVerification("pathAdd", $"{{\"noteId\":\"{holdId}\",\"bar\":1.5}}");
            Check("Path double-click event inserts a sampled node", window.CurrentDocument?.SelectedChart.Notes
                .Single(note => note.Id == holdId).Nodes.Count == 2);
            var nodeId = window.CurrentDocument!.SelectedChart.Notes.Single(note => note.Id == holdId).Nodes[0].Id;
            var originalPathHead = window.CurrentDocument.SelectedChart.Notes.Single(note => note.Id == holdId);
            var headTimeBeforeNodeDrag = originalPathHead.Time;
            var headCenterBeforeNodeDrag = originalPathHead.Center;
            var tailTimeBeforeNodeDrag = originalPathHead.Nodes[^1].Time;
            window.StageEventForVerification("pathMove",
                $"{{\"noteId\":\"{holdId}\",\"nodeId\":\"{nodeId}\",\"startBar\":1.5,\"endBar\":1.75,\"startCenter\":2.5,\"endCenter\":3.0}}");
            var movedNode = window.CurrentDocument?.SelectedChart.Notes.Single(note => note.Id == holdId).Nodes[0];
            Check("Path drag edits exact node time and center", movedNode?.Time == ExactBarTime.FromFraction(7, 4) &&
                movedNode.Center == 3.0);
            Check("Middle-node drag preserves the head and tail", originalPathHead.Time == headTimeBeforeNodeDrag &&
                originalPathHead.Center == headCenterBeforeNodeDrag && originalPathHead.Nodes[^1].Time == tailTimeBeforeNodeDrag);
            window.StageEventForVerification("pathMove",
                $"{{\"noteId\":\"{holdId}\",\"nodeId\":\"{nodeId}\",\"startBar\":1.75,\"endBar\":1.763,\"startCenter\":3,\"endCenter\":3,\"snap\":false}}");
            Check("Free node drag keeps off-grid time", movedNode?.Time == ExactBarTime.FromFraction(1763, 1000));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.ApplyCurveForVerification(V2PathCurve.EaseInOutCubic);
            Check("Inspector applies the selected node curve", movedNode?.CurveToNext == V2PathCurve.EaseInOutCubic);
            window.ShortcutForVerification(Key.Delete);
            Check("Delete removes the selected path node", window.CurrentDocument?.SelectedChart.Notes
                .Single(note => note.Id == holdId).Nodes.Count == 1);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Undo restores the path node", window.CurrentDocument?.SelectedChart.Notes
                .Single(note => note.Id == holdId).Nodes.Count == 2);
            var created = Path.Combine(root, "created");
            await window.SavePackageToForVerification(created);
            if (!Directory.Exists(created)) throw new InvalidOperationException("Draft save failed: " + window.StatusMessage);
            var createdPackage = EditorPackageRepository.Open(created);
            Check("New multi-chart draft saves and reopens as strict v2", createdPackage.Charts.Count == 2 &&
                createdPackage.SelectedChart.Notes.Count == 2 && File.Exists(Path.Combine(created, "audio.wav")) &&
                window.CurrentDocument?.HasUnsavedChanges == false);

            window.StageEventForVerification("selection",
                $"{{\"ids\":[\"{nodeId}\"],\"noteIds\":[\"{holdId}\"],\"parentId\":\"{holdId}\",\"nodeId\":\"{nodeId}\"}}");
            window.ApplyHoldJudgeForVerification(false);
            Check("Inspector edits Hold node judgement", window.CurrentDocument?.SelectedChart.Notes
                .Single(note => note.Id == holdId).Nodes.Single(node => node.Id == nodeId).Judge == false);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);

            var tapId = window.CurrentDocument!.SelectedChart.Notes.Single(note => note.Type == V2NoteType.Tap).Id;
            window.SelectNotesForVerification(tapId, holdId);
            window.ApplySpeedForVerification(1.6);
            Check("Multi-selection applies one speed command batch", window.CurrentDocument.SelectedChart.Notes
                .All(note => note.ScrollSpeedMultiplier == 1.6) && window.SelectedNoteIds.Count == 2);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Undo restores every Note in a speed batch", window.CurrentDocument.SelectedChart.Notes
                .All(note => note.ScrollSpeedMultiplier == 1.0));
            window.SelectNotesForVerification(tapId, holdId);
            var centers = window.CurrentDocument.SelectedChart.Notes.ToDictionary(note => note.Id, note => note.Center);
            window.NudgeSelectionForVerification(0, 1);
            Check("Multi-selection nudges positions together", window.CurrentDocument.SelectedChart.Notes
                .All(note => Math.Abs(note.Center - centers[note.Id] - .1) < .0001));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.SelectNotesForVerification(tapId, holdId);
            var moveTimes = window.CurrentDocument.SelectedChart.Notes.ToDictionary(note => note.Id, note => note.Time);
            var moveCenters = window.CurrentDocument.SelectedChart.Notes.ToDictionary(note => note.Id, note => note.Center);
            window.StageEventForVerification("noteMove",
                $"{{\"noteId\":\"{tapId}\",\"startBar\":0,\"endBar\":0.25,\"startCenter\":0,\"endCenter\":0.25}}");
            Check("Stage drag moves the whole mixed Note selection", window.CurrentDocument.SelectedChart.Notes.All(note =>
                note.Time == moveTimes[note.Id] + ExactBarTime.FromFraction(1, 4) &&
                Math.Abs(note.Center - moveCenters[note.Id] - .25) < .0001));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("One undo restores the whole dragged selection", window.CurrentDocument.SelectedChart.Notes.All(note =>
                note.Time == moveTimes[note.Id] && note.Center == moveCenters[note.Id]) && window.SelectedNoteIds.Count == 2);
            window.SelectNoteForVerification(tapId);
            window.ApplyGeometryForVerification("0.53", 4.45, 1);
            window.SelectNoteForVerification(holdId);
            window.ApplyGeometryForVerification("1.07", 2.5, 1);
            var offGridTimes = window.CurrentDocument.SelectedChart.Notes.ToDictionary(note => note.Id, note => note.Time);
            var boundaryCenters = window.CurrentDocument.SelectedChart.Notes.ToDictionary(note => note.Id, note => note.Center);
            window.SelectNotesForVerification(tapId, holdId);
            window.NudgeSelectionForVerification(1, 0);
            Check("Off-grid multi-selection time nudge uses one exact grid step", window.CurrentDocument.SelectedChart.Notes.All(note =>
                note.Time - offGridTimes[note.Id] == ExactBarTime.FromFraction(1, 16)));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.SelectNotesForVerification(tapId, holdId);
            window.StageEventForVerification("noteMove",
                $"{{\"noteId\":\"{tapId}\",\"startBar\":0.53,\"endBar\":0.66,\"startCenter\":0,\"endCenter\":0}}");
            Check("Off-grid group drag snaps the grabbed anchor once and shares its time delta",
                window.CurrentDocument.SelectedChart.Notes.Single(note => note.Id == tapId).Time == ExactBarTime.FromFraction(11, 16) &&
                window.CurrentDocument.SelectedChart.Notes.All(note => note.Time - offGridTimes[note.Id] == ExactBarTime.FromFraction(63, 400)));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.SelectNotesForVerification(tapId, holdId);
            window.StageEventForVerification("noteMove",
                $"{{\"noteId\":\"{tapId}\",\"startBar\":0.53,\"endBar\":0.66,\"startCenter\":0,\"endCenter\":0,\"snap\":false}}");
            Check("Free group drag shares the raw time delta without snapping", window.CurrentDocument.SelectedChart.Notes.All(note =>
                note.Time - offGridTimes[note.Id] == ExactBarTime.FromFraction(13, 100)));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.SelectNotesForVerification(tapId, holdId);
            window.StageEventForVerification("noteMove",
                $"{{\"noteId\":\"{tapId}\",\"startBar\":0.53,\"endBar\":0.53,\"startCenter\":0,\"endCenter\":0.01}}");
            Check("Position-only group drag does not snap off-grid times", window.CurrentDocument.SelectedChart.Notes.All(note =>
                note.Time == offGridTimes[note.Id] && Math.Abs(note.Center - boundaryCenters[note.Id] - .01) < 1e-12));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            foreach (var (step, expectedDelta) in new[] { (.1, -.15), (.01, -.12), (0d, -.123) })
            {
                window.SelectNotesForVerification(tapId, holdId);
                var stepText = step.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                window.StageEventForVerification("noteMove",
                    $"{{\"noteId\":\"{tapId}\",\"startBar\":0.53,\"endBar\":0.53,\"startCenter\":0,\"endCenter\":-0.123,\"positionStep\":{stepText}}}");
                Check($"Spatial step {step} uses one shared group delta", window.CurrentDocument.SelectedChart.Notes.All(note =>
                    note.Time == offGridTimes[note.Id] && Math.Abs(note.Center - boundaryCenters[note.Id] - expectedDelta) < 1e-12));
                window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            }
            window.SelectNotesForVerification(tapId, holdId);
            window.NudgeSelectionForVerification(0, 1);
            Check("Nudge crosses recommended boundary with one shared delta", window.CurrentDocument.SelectedChart.Notes.All(note =>
                Math.Abs(note.Center - boundaryCenters[note.Id] - .1) < 1e-12 && note.Time == offGridTimes[note.Id]));
            var nudgedCenters = window.CurrentDocument.SelectedChart.Notes.ToDictionary(note => note.Id, note => note.Center);
            window.NudgeSelectionForVerification(0, 1);
            Check("Repeated overscan nudge preserves relative centers", window.CurrentDocument.SelectedChart.Notes.All(note =>
                Math.Abs(note.Center - nudgedCenters[note.Id] - .1) < 1e-12));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("One undo restores the preceding overscan nudge", window.CurrentDocument.SelectedChart.Notes.All(note =>
                Math.Abs(note.Center - nudgedCenters[note.Id]) < 1e-12));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.SelectNotesForVerification(tapId, holdId);
            window.StageEventForVerification("noteMove",
                $"{{\"noteId\":\"{tapId}\",\"startBar\":0.53,\"endBar\":0.53,\"startCenter\":0,\"endCenter\":0.25}}");
            Check("Overscan group drag preserves relative centers", window.CurrentDocument.SelectedChart.Notes.All(note =>
                Math.Abs(note.Center - boundaryCenters[note.Id] - .25) < 1e-12 && note.Time == offGridTimes[note.Id]));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Off-grid group regressions restore all original geometry", window.CurrentDocument.SelectedChart.Notes.All(note =>
                note.Time == moveTimes[note.Id] && note.Center == moveCenters[note.Id]));
            // Off-screen objects remain recoverable from the inspector list and save without clipping.
            window.SelectNoteForVerification(tapId);
            window.ApplyGeometryForVerification("0.53", -20, 8);
            var overscanNote = window.CurrentDocument.SelectedChart.Notes.Single(n => n.Id == tapId);
            Check("Inspector accepts off-screen center and width above five", overscanNote.Center == -20 && overscanNote.Width == 8);
            var overscanInspector = window.GetVisualDescendants().OfType<InspectorView>().Single();
            Check("Three-layer risk warning and object locator are visible", overscanInspector.FindControl<TextBlock>("SpatialWarnings")!.Text!.Contains("3 ·") &&
                overscanInspector.FindControl<ComboBox>("ObjectChoice")!.ItemCount >= window.CurrentDocument.SelectedChart.Notes.Count);
            window.StageEventForVerification("noteMove", $"{{\"noteId\":\"{tapId}\",\"startBar\":0.53,\"endBar\":0.78,\"startCenter\":0,\"endCenter\":0}}");
            Check("Time-only drag preserves off-screen geometry", overscanNote.Center == -20 && overscanNote.Width == 8);
            var overscanSave = Path.Combine(root, "overscan-save");
            await window.SavePackageToForVerification(overscanSave);
            var reopenedOverscan = EditorPackageRepository.Open(overscanSave).SelectedChart.Notes.Single(n => n.Id == tapId);
            Check("Overscan save and reopen preserve center and width", reopenedOverscan.Center == -20 && reopenedOverscan.Width == 8);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.SelectNotesForVerification(tapId, holdId);
            window.ShortcutForVerification(Key.Delete);
            Check("Delete removes all selected Notes", window.CurrentDocument.SelectedChart.Notes.Count == 0);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("One undo restores a multi-Note deletion", window.CurrentDocument.SelectedChart.Notes.Count == 2);

            Check("Native box selection synchronizes all parents without editing", VerifyBoxSelection(window, tapId, holdId));
            window.CopyChartForVerification();
            Check("Copy difficulty clones authored Notes", window.CurrentDocument.Charts.Count == 3 &&
                window.CurrentDocument.SelectedChartId == "main-copy" && window.CurrentDocument.SelectedChart.Notes.Count == 2);
            window.MoveChartForVerification(-1);
            Check("Move difficulty updates package order", window.CurrentDocument.Charts[1].Id == "main-copy");
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            window.DeleteChartForVerification();
            Check("Delete difficulty keeps the package valid", window.CurrentDocument.Charts.Count == 2);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);

            var settings = new EditorPackageSettingsRequest("renamed-pack", "Renamed Package", "New Artist",
                "renamed", "charts/renamed.json", V2Difficulty.Giga, null, 15, false, ["Community", "Reviewer"], .25,
                new V2Preview(1, 8), new V2Preview(2, 4), null, null, null, false, false);
            window.ApplyPackageSettingsForVerification(settings);
            Check("Package settings apply metadata, identity, timing and previews", window.CurrentDocument.PackId == "renamed-pack" &&
                window.CurrentDocument.SelectedChartId == "renamed" && window.CurrentDocument.SelectedChart.Level == 15 &&
                window.CurrentDocument.SelectedChart.AudioOffsetSec == .25 && window.CurrentDocument.Preview == new V2Preview(1, 8) &&
                window.CurrentDocument.SelectedChart.Preview == new V2Preview(2, 4));
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("One undo restores the complete settings batch", window.CurrentDocument.PackId == "new-pack" &&
                window.CurrentDocument.SelectedChartId == "main-copy" && window.CurrentDocument.SelectedChart.AudioOffsetSec == 0 &&
                window.CurrentDocument.Preview is null && window.CurrentDocument.SelectedChart.Preview is null);
            window.ShortcutForVerification(Key.Y, KeyModifiers.Control);
            Check("Redo restores the complete settings batch", window.CurrentDocument.SelectedChartId == "renamed" &&
                window.CurrentDocument.SelectedChart.Difficulty == V2Difficulty.Giga);
            var settingsDialog = new PackageSettingsDialog(window.CurrentDocument);
            settingsDialog.Show();
            Dispatcher.UIThread.RunJobs();
            Check("Package settings dialog loads the live document", settingsDialog.FindControl<TextBox>("PackIdValue")?.Text == "renamed-pack" &&
                settingsDialog.FindControl<TextBox>("ChartIdValue")?.Text == "renamed" &&
                settingsDialog.FindControl<Avalonia.Controls.NumericUpDown>("OffsetValue")?.Value == .25m);
            settingsDialog.Close();
            var precisionNote = window.CurrentDocument.SelectedChart.Notes.First(n => n.Type == V2NoteType.Tap);
            window.SelectNoteForVerification(precisionNote.Id);
            window.ApplyGeometryForVerification("1", .6, .6);
            window.NudgeSelectionForVerification(0, 1);
            Check("Decimal nudge 0.6 + 0.1 is canonical 0.7", precisionNote.Center == .7);
            window.NudgeSelectionForVerification(0, -1);
            Check("Decimal reverse nudge returns exact 0.6", precisionNote.Center == .6);
            window.StageEventForVerification("noteMove",
                $"{{\"noteId\":\"{precisionNote.Id}\",\"startBar\":1,\"endBar\":1,\"startCenter\":0,\"endCenter\":0.3,\"positionStep\":0.1}}");
            Check("Grid drag commits canonical 0.9", precisionNote.Center == .9);
            window.StageEventForVerification("noteResize",
                $"{{\"noteId\":\"{precisionNote.Id}\",\"moveStartEdge\":false,\"edge\":1.3,\"widthStep\":0.05}}");
            Check("Resize preserves decimal edge with canonical geometry", precisionNote.Center == .95 && precisionNote.Width == .7);
            var preciseCenter = Math.BitIncrement(.6);
            window.ApplyGeometryForVerification("1", preciseCenter, Math.BitIncrement(.6));
            Check("Explicit high precision geometry remains untouched", precisionNote.Center == preciseCenter && precisionNote.Width == Math.BitIncrement(.6));
            window.NudgeSelectionForVerification(1, 0);
            Check("Time-only nudge preserves high precision geometry", precisionNote.Center == preciseCenter && precisionNote.Width == Math.BitIncrement(.6));
            var precisionPath = window.CurrentDocument.SelectedChart.Notes.First(n => n.Type == V2NoteType.Hold);
            var precisionNode = precisionPath.Nodes[0];
            window.CurrentDocument.Execute(new EditPathNodeCommand(precisionPath.Id, precisionNode.Id, precisionNode.Time, .6, .6));
            window.SelectNoteForVerification(precisionPath.Id);
            window.StageEventForVerification("pathMove",
                $"{{\"noteId\":\"{precisionPath.Id}\",\"nodeId\":\"{precisionNode.Id}\",\"startBar\":1,\"endBar\":1,\"startCenter\":0,\"endCenter\":0.3,\"positionStep\":0.1}}");
            Check("Path node drag commits canonical center", precisionNode.Center == .9 && precisionNode.Width == .6);
            window.SelectNoteForVerification(precisionNote.Id);
            window.ApplyGeometryForVerification("1", .6, .6);
            var precisionSave = Path.Combine(root, "precision-save");
            await window.SavePackageToForVerification(precisionSave);
            var precisionDocument = EditorPackageRepository.Open(precisionSave);
            precisionDocument.SelectChart(window.CurrentDocument.SelectedChartId);
            var preciseReload = precisionDocument.SelectedChart.Notes.Single(n => n.Id == precisionNote.Id);
            Check($"Decimal geometry survives save/reopen exactly (live={precisionNote.Center:R}/{precisionNote.Width:R}, saved={preciseReload.Center:R}/{preciseReload.Width:R})", preciseReload.Center == .6 && preciseReload.Width == .6);
            window.CurrentDocument.Execute(new SetNoteScrollSpeedMultiplierCommand(precisionNote.Id, preciseCenter));
            window.SelectNoteForVerification(precisionNote.Id);
            var precisionInspector = window.GetVisualDescendants().OfType<InspectorView>().Single();
            precisionInspector.SetSpeedForVerification(preciseCenter);
            Check("Unchanged speed Apply preserves loaded double", precisionNote.ScrollSpeedMultiplier == preciseCenter);
            window.ApplyPackageSettingsForVerification(settings with
            {
                AudioOffsetSec = preciseCenter,
                PackPreview = new V2Preview(preciseCenter, 8),
                ChartPreview = new V2Preview(preciseCenter, 4),
            });
            var precisionDialog = new PackageSettingsDialog(window.CurrentDocument);
            var precisionRequest = precisionDialog.RequestForVerification();
            Check("Unchanged settings preserve offset and both preview doubles",
                precisionRequest.AudioOffsetSec == preciseCenter &&
                precisionRequest.PackPreview?.StartSec == preciseCenter && precisionRequest.ChartPreview?.StartSec == preciseCenter);
            precisionDialog.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL " + ex);
            failed = true;
        }
        finally
        {
            window.Close();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        return failed ? 1 : 0;
    }

    private static bool VerifyBoxSelection(MainWindow window, string tap, string hold)
    {
        var document = window.CurrentDocument!;
        var before = document.BuildChartSnapshot(document.SelectedChartId);
        var revision = window.StageRevision;
        window.StageEventForVerification("selection", System.Text.Json.JsonSerializer.Serialize(new
        { ids = new[] { tap, hold }, primary = tap, parentId = tap, noteIds = new[] { tap, hold } }));
        var selected = window.SelectedNoteIds.Count == 2 && window.SelectedNoteId == tap;
        window.StageEventForVerification("selection", "{\"ids\":[],\"primary\":\"\",\"parentId\":\"\",\"noteIds\":[]}");
        var cleared = window.SelectedNoteIds.Count == 0 && window.SelectedNoteId is null;
        return selected && cleared && window.StageRevision == revision &&
            V2JsonEncoder.EncodeChart(before).SequenceEqual(V2JsonEncoder.EncodeChart(document.BuildChartSnapshot(document.SelectedChartId)));
    }

    private static void ClickControl(Control control, string name) =>
        control.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void SelectTimingItem(Control control, string name, ExactBarTime time)
    {
        var list = control.FindControl<ListBox>(name)!;
        list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(item => Equals(item.Tag, time));
        Dispatcher.UIThread.RunJobs();
    }

    internal static void CreatePackage(string destination)
    {
        var root = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(root);
        var audio = Path.Combine(root, "fixture.wav");
        WriteWave(audio);
        var session = new EditorDocumentSession();
        var document = session.CreateDraft(new EditorProjectDraftRequest(
            "shell-fixture", "Shell Fixture", "Community", "main", V2Difficulty.Hard,
            null, 12, false, "Verification", 150, 0, 16, audio, null));
        document.Execute(new AddNoteCommand(new EditableNote
        {
            Id = "fixture-note", Type = V2NoteType.Tap, Track = EditorTrack.Center,
            Time = ExactBarTime.FromFraction(2, 1), Center = 2.5, Width = 1,
        }));
        session.Save(destination);
    }

    private static void WriteWave(string path)
    {
        const int rate = 44100;
        const int samples = rate * 20;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(samples * 2);
        for (var index = 0; index < samples; index++) writer.Write((short)0);
    }
}
