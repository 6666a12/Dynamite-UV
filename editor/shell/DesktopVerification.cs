using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

internal static class DesktopVerification
{
    public static int Run(string outDir, bool emptyDraftOnly = false)
    {
        Directory.CreateDirectory(outDir);
        var result = 99;
        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        App.VerificationOwnsWindow = true;
        AppBuilder.Configure<App>().UsePlatformDetect()
            .With(new Win32PlatformOptions { CompositionMode = new[] { Win32CompositionMode.RedirectionSurface } })
            .AfterSetup(_ => result = -1).SetupWithLifetime(lifetime);
        var window = new MainWindow(Path.Combine(Path.GetFullPath(outDir), "user-data-" + Guid.NewGuid().ToString("N")));
        lifetime.MainWindow = window;
        window.Topmost = true;
        window.Position = new PixelPoint(0, 0);
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        ActivateWindow(window);
        SetForegroundWindow(window.TryGetPlatformHandle()?.Handle ?? 0);
        var lines = new List<string>();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                for (var i = 0; i < 1250 && !window.Ready; i++)
                {
                    if (HasVisibleTopLevelWindow(window.StageProcessId))
                        throw new InvalidOperationException("Godot exposed a separate visible window during startup");
                    await Task.Delay(20);
                }
                await Task.Delay(1200);
                if (!window.Ready || !window.Stage.Host.Diagnostics().Embedded)
                    throw new InvalidOperationException("Initial native embedding failed");
                lines.Add("PASS Godot startup stays hidden until native embedding");
                if (Environment.GetEnvironmentVariable("DYNA_VERIFY_ACTIVATION_ONLY") == "1")
                {
                    await VerifyActivation(window, lines.Add);
                    result = 0;
                    return;
                }
                if (Environment.GetEnvironmentVariable("DYNA_VERIFY_WORKSPACE_ONLY") == "1")
                {
                    await WorkspaceDesktopVerification.Run(window, ClickNative, lines.Add);
                    await Capture(window, outDir, "workspace.png");
                    result = 0;
                    return;
                }
                var source = Path.Combine(outDir, "fixture-source");
                if (Directory.Exists(source)) Directory.Delete(source, true);
                Verification.CreatePackage(source);
                if (Environment.GetEnvironmentVariable("DYNA_VERIFY_CHART_SWITCH_ONLY") == "1")
                {
                    await VerifyEmptyChartSwitch(window, outDir, lines.Add);
                    result = 0;
                    return;
                }
                if (emptyDraftOnly)
                {
                    var draft = Path.Combine(outDir, "empty-draft");
                    if (Directory.Exists(draft)) Directory.Delete(draft, true);
                    await window.CreatePackageForVerification(new EditorCreateForm("empty-draft", "Empty Draft",
                        "Community", Path.Combine(outDir, "fixture.wav"), "main", 2, "", 12, false,
                        "Verification", 150, 0, null));
                    await Task.Delay(700);
                    lines.Add("empty draft " + window.LastStageInput);
                    if (!window.LastStageInput.Contains("loaded chart=main notes=0 audio=True"))
                        throw new InvalidOperationException("The empty draft did not reach the live Godot stage: " + window.StatusMessage);
                    window.ConfigurePlacementForVerification(V2NoteType.Tap, EditorTrack.Center, 1);
                    await ClickNative(window, 830, 714);
                    if (window.CurrentDocument?.SelectedChart.Notes.Count != 1)
                        throw new InvalidOperationException("The first Note could not be placed in an empty draft: " + window.StatusMessage);
                    await window.SavePackageToForVerification(draft);
                    if (window.CurrentDocument?.HasUnsavedChanges != false)
                        throw new InvalidOperationException("The first Note did not make the new draft saveable: " + window.StatusMessage);
                    lines.Add("PASS empty draft live stage / first native Note placement / first save");
                    await Capture(window, outDir, "empty-first-note.png");
                    result = 0;
                    return;
                }
                await window.OpenPackageForVerification(source);
                window.SeekForVerification(2); // Fixture coordinates target bar 2; D now moves seconds, not bars.
                await Task.Delay(700);
                lines.Add("stage " + window.LastStageInput);
                if (!window.LastStageInput.Contains("loaded chart=main notes=1 audio=True"))
                    throw new InvalidOperationException("The real chart and audio were not loaded by Godot");
                if (Environment.GetEnvironmentVariable("DYNA_VERIFY_CONTEXT_ONLY") == "1")
                {
                    await Task.Run(() =>
                    {
                        SetCursorPos(800, 450);
                        mouse_event(1, 0, 0, 0, 0);
                        Thread.Sleep(150);
                        mouse_event(8, 0, 0, 0, 0);
                        Thread.Sleep(60);
                        mouse_event(16, 0, 0, 0, 0);
                    });
                    await Task.Delay(700);
                    lines.Add("context " + window.LastStageInput);
                    await Capture(window, outDir, "context-en.png");
                    WorkbenchLocalizer.Shared.SetLanguage("zh-CN");
                    await Task.Delay(500);
                    await Capture(window, outDir, "context-zh.png");
                    window.Stage.Host.FocusNative();
                    var coords = window.LastStageInput["context button=".Length..].Split(',')
                        .Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    var click = window.Stage.Host.PointToScreen(new Point(
                        coords[0] * window.Stage.Host.Bounds.Width, coords[1] * window.Stage.Host.Bounds.Height));
                    await ClickNative(window, click.X, click.Y);
                    await Task.Delay(400);
                    if (window.PlacementTrackForVerification != EditorTrack.Left)
                        throw new InvalidOperationException("Context side request did not synchronize Tools");
                    await PressKey(0x09); await PressKey(0x09); await PressKey(0x09);
                    await PressKey(0x0D); // First Note button: Tap.
                    await Task.Delay(400);
                    if (window.CurrentTool != "PLACE" || window.PlacementTypeForVerification != V2NoteType.Tap)
                        throw new InvalidOperationException("Context Note request did not enter placement");
                    await PressKey(0x1B);
                    lines.Add("PASS real context menu open / language / side and Note sync / Escape");
                    result = 0;
                    return;
                }
                if (Environment.GetEnvironmentVariable("DYNA_VERIFY_MARQUEE_ONLY") == "1")
                {
                    window.StageEventForVerification("place", "{\"type\":0,\"track\":1,\"bar\":2,\"center\":3.5,\"width\":0.5,\"tailBar\":2}");
                    window.SelectToolForVerification("SELECT");
                    await Task.Delay(500);
                    var host = window.Stage.Host;
                    var scale = Math.Min(host.Bounds.Width / 1920d, host.Bounds.Height / 1080d);
                    var origin = new Point((host.Bounds.Width - 1920 * scale) / 2,
                        (host.Bounds.Height - 1080 * scale) / 2);
                    var from = host.PointToScreen(origin + new Vector(650 * scale, 760 * scale));
                    var to = host.PointToScreen(origin + new Vector(1350 * scale, 960 * scale));
                    await DragNative(window, from.X, from.Y, to.X, to.Y);
                    if (window.SelectedNoteIds.Count != 2)
                        throw new InvalidOperationException($"Native marquee selected {window.SelectedNoteIds.Count}, expected two");
                    await Capture(window, outDir, "marquee-selected.png");
                    await PressKey(0x2E);
                    if (window.CurrentDocument!.SelectedChart.Notes.Count != 0)
                        throw new InvalidOperationException("Marquee bulk delete failed");
                    await PressCtrlKey(0x5A);
                    if (window.CurrentDocument.SelectedChart.Notes.Count != 2)
                        throw new InvalidOperationException("Marquee deletion undo failed");
                    lines.Add("PASS real mouse marquee selects two Notes / bulk delete / one undo");
                    window.StageEventForVerification("place", "{\"type\":0,\"track\":1,\"bar\":4,\"center\":3,\"width\":0.5,\"tailBar\":4}");
                    window.SeekForVerification(1);
                    await Task.Delay(350);
                    // Keep the mouse held while the real audio clock moves several screens.
                    try
                    {
                        await Task.Run(() =>
                        {
                            SetCursorPos(from.X, to.Y);
                            mouse_event(1, 0, 0, 0, 0);
                            Thread.Sleep(150);
                            mouse_event(2, 0, 0, 0, 0);
                            SetCursorPos(to.X, from.Y);
                            mouse_event(1, 0, 0, 0, 0);
                        });
                        await Task.Delay(150);
                        await PressKey(0x20);
                        var deadline = DateTime.UtcNow.AddSeconds(12);
                        while (window.CurrentSecond < 7.5 && DateTime.UtcNow < deadline) await Task.Delay(100);
                        if (!window.Playing || window.CurrentSecond < 7.5)
                            throw new InvalidOperationException("Held marquee did not allow real playback");
                    }
                    finally { mouse_event(4, 0, 0, 0, 0); }
                    await Task.Delay(350);
                    if (window.SelectedNoteIds.Count != 3)
                        throw new InvalidOperationException($"Held playback marquee lost off-screen Notes: {window.SelectedNoteIds.Count}");
                    await PressKey(0x20);
                    await PressKey(0x2E);
                    if (window.CurrentDocument.SelectedChart.Notes.Count != 0)
                        throw new InvalidOperationException("Off-screen selected Notes were not deleted");
                    await PressCtrlKey(0x5A);
                    if (window.CurrentDocument.SelectedChart.Notes.Count != 3)
                        throw new InvalidOperationException("Off-screen batch undo failed");
                    lines.Add("PASS held mouse + real music playback across screens / 3 off-screen Notes / delete / undo");
                    result = 0;
                    return;
                }
                window.SendInvalidAuthoringForVerification();
                await Task.Delay(350);
                if (!window.Ready || !window.StatusMessage.Contains("Stage error", StringComparison.Ordinal))
                    throw new InvalidOperationException("An invalid IPC command did not produce a recoverable stage error");
                lines.Add("PASS invalid IPC command keeps the native stage alive");
                await Capture(window, outDir, "01-package.png");
                await ClickNative(window,830, 714);
                lines.Add($"selection={window.SelectedNoteId}");
                if (window.SelectedNoteId != "fixture-note")
                    throw new InvalidOperationException("Real Godot Note click did not select the document Note");
                await DragNative(window, 760, 714, 760, 586);
                var dragged = window.CurrentDocument?.SelectedChart.Notes.Single(note => note.Id == "fixture-note");
                // Purely vertical centre-track drag: the Note time must change while its center
                // stays fixed; a center-only change leaves a zero base delta and invalidates the
                // later per-Note speed comparison.
                if (dragged is null || dragged.Time == ExactBarTime.FromFraction(2, 1) || dragged.Center != 2.5)
                    throw new InvalidOperationException("Native SELECT drag did not move the document Note time without shifting its center: " + window.StatusMessage);
                var baseDragDelta = dragged.Time.ToDouble() - 2;
                await PressCtrlKey(0x5A);
                await Task.Delay(350);
                var restored = window.CurrentDocument?.SelectedChart.Notes.Single(note => note.Id == "fixture-note");
                if (restored is null || restored.Time != ExactBarTime.FromFraction(2, 1) || restored.Center != 2.5)
                    throw new InvalidOperationException("Undo did not restore the native Note drag");
                await DragNative(window, 827, 714, 890, 714);
                var resized = window.CurrentDocument?.SelectedChart.Notes.Single(note => note.Id == "fixture-note");
                if (resized is null || resized.Width <= 1 || resized.Time != ExactBarTime.FromFraction(2, 1) ||
                    Math.Abs(resized.Center - resized.Width / 2d - 2d) > 1e-9)
                    throw new InvalidOperationException($"Native edge drag did not resize the document Note: time={resized?.Time.ToDouble():0.###} center={resized?.Center:0.###} width={resized?.Width:0.###}; {window.StatusMessage}");
                await PressCtrlKey(0x5A);
                await Task.Delay(350);
                // One edge resize changed Center and Width together, so undo must restore the
                // complete original geometry, not only the width.
                var restoredResize = window.CurrentDocument?.SelectedChart.Notes.Single(note => note.Id == "fixture-note");
                if (restoredResize is null || restoredResize.Time != ExactBarTime.FromFraction(2, 1) ||
                    restoredResize.Center != 2.5 || restoredResize.Width != 1)
                    throw new InvalidOperationException("Undo did not restore the native Note resize geometry");
                lines.Add("PASS native SELECT Note drag / edge resize / one-step undo");
                window.ApplySpeedForVerification(1.35);
                await Task.Delay(500);
                if (window.CurrentDocument?.SelectedChart.Notes[0].ScrollSpeedMultiplier != 1.35 ||
                    Math.Abs(window.CurrentBar - 2) > .02)
                    throw new InvalidOperationException("Document speed command or playhead preservation failed");
                await PressKey(0x51);
                if (window.PlayerSpeed != .9) throw new InvalidOperationException("Q did not reduce player speed");
                await PressKey(0x45);
                if (window.PlayerSpeed != 1.0) throw new InvalidOperationException("E did not increase player speed");
                await PressKey(0x57);
                if (window.PlaybackRate != 1.1) throw new InvalidOperationException("W did not increase playback rate");
                await PressKey(0x53);
                if (window.PlaybackRate != 1.0) throw new InvalidOperationException("S did not reduce playback rate");
                var before = window.CurrentSecond;
                lines.Add($"before playback: second={before:0.000} rate={window.PlaybackRate:0.0}");
                await PressKey(0x20);
                await Task.Delay(650);
                lines.Add($"before incremental edit: playing={window.Playing} second={window.CurrentSecond:0.000}");
                if (!window.Playing) throw new InvalidOperationException("Playback did not start before the incremental edit");
                var updateRevision = window.StageRevision;
                var updateSecond = window.CurrentSecond;
                var beforeEditAudio = await window.StageServiceForVerification.AudioSnapshotAsync();
                window.ApplySpeedForVerification(1.4);
                await Task.Delay(500);
                if (!window.LastStageUpdateIncremental || window.StageRevision <= updateRevision ||
                    !window.LastStageInput.Contains("updated chart=main", StringComparison.Ordinal) ||
                    !window.LastStageInput.Contains("playing=True", StringComparison.Ordinal) ||
                    !window.LastStageInput.Contains("selection=1", StringComparison.Ordinal) ||
                    window.CurrentSecond <= updateSecond || window.SelectedNoteId != "fixture-note")
                    throw new InvalidOperationException("Incremental edit did not preserve playback, playhead and selection: " + window.LastStageInput);
                // Compare fresh request/response samples rather than a stale LastStageInput
                // string or assumed absolute counters (earlier seeks may legitimately restart).
                var afterEditAudio = await window.StageServiceForVerification.AudioSnapshotAsync();
                AssertAudioContinuous(beforeEditAudio, afterEditAudio);
                await Task.Delay(250);
                var liveAudio = await window.StageServiceForVerification.AudioSnapshotAsync();
                AssertAudioContinuous(afterEditAudio, liveAudio);
                await PressKey(0x20);
                if (window.CurrentSecond <= before) throw new InvalidOperationException("Space did not advance music time");
                lines.Add($"native shortcuts speed/rate/playback passed; time {before:0.000} -> {window.CurrentSecond:0.000}");
                lines.Add("PASS incremental chart edit preserves active audio, playhead and native selection");
                await PressCtrlKey(0x53);
                if (window.CurrentDocument?.HasUnsavedChanges != false ||
                    EditorPackageRepository.Open(source).SelectedChart.Notes[0].ScrollSpeedMultiplier != 1.4)
                    throw new InvalidOperationException("Ctrl+S did not save the document command");
                lines.Add("PASS real Ctrl+S persisted Note speed");
                window.SeekForVerification(2);
                await Task.Delay(400);
                await DragNative(window, 760, 714, 760, 586);
                var fastDragged = window.CurrentDocument?.SelectedChart.Notes.Single(note => note.Id == "fixture-note");
                var fastDragDelta = fastDragged?.Time.ToDouble() - 2;
                if (fastDragDelta is null || Math.Abs(fastDragDelta.Value - baseDragDelta / 1.4) > .07)
                    throw new InvalidOperationException($"Per-Note speed produced the wrong drag delta: base={baseDragDelta:0.####} fast={fastDragDelta:0.####}");
                await PressCtrlKey(0x5A);
                window.SeekForVerification(3);
                await Task.Delay(400);
                lines.Add("PASS native drag delta respects per-Note scroll speed");
                window.ConfigurePlacementForVerification(V2NoteType.Hold, EditorTrack.Center, 1);
                await HoverNative(window, 830, 714);
                await Capture(window, outDir, "05-placement-ghost.png");
                await ClickNative(window, 830, 714);
                if (window.CurrentDocument?.SelectedChart.Notes.Any(note => note.Type == V2NoteType.Hold) == true)
                    throw new InvalidOperationException("Hold was committed before its tail stage");
                await HoverNative(window, 830, 620);
                await Capture(window, outDir, "05-hold-tail-ghost.png");
                await ClickNative(window, 830, 620);
                var hold = window.CurrentDocument?.SelectedChart.Notes.SingleOrDefault(note => note.Type == V2NoteType.Hold);
                if (hold is null) throw new InvalidOperationException("Native two-stage placement did not add a Hold");
                var holdId = hold.Id;
                window.SelectToolForVerification("PATH");
                await Capture(window, outDir, "05-hold-before-path.png");
                await DoubleClickNative(window, 830, 650);
                var insertedHold = window.CurrentDocument!.SelectedChart.Notes.Single(note => note.Id == holdId);
                if (insertedHold.Nodes.Count != 2) throw new InvalidOperationException("Native path double-click did not insert a node");
                var oldCenter = insertedHold.Center;
                await DragNative(window, 830, 721, 880, 721);
                var movedHold = window.CurrentDocument!.SelectedChart.Notes.Single(note => note.Id == holdId);
                if (Math.Abs(movedHold.Center - oldCenter) < .001)
                    throw new InvalidOperationException($"Native path head drag did not move the Hold: before={oldCenter:0.###} after={movedHold.Center:0.###}; {window.StatusMessage}");
                lines.Add($"PASS native Hold placement / path insertion / path drag ({holdId}, nodes={movedHold.Nodes.Count})");
                await Capture(window, outDir, "05-authoring.png");
                window.SelectToolForVerification("PAN");
                var panBefore = window.CurrentBar;
                await DragNative(window, 700, 500, 700, 600);
                await Task.Delay(350);
                if (window.CurrentBar <= panBefore)
                    throw new InvalidOperationException("Native PAN drag did not advance the preview clock");
                await VerifyPanAudioSuspension(window);
                lines.Add("PASS native PAN holds audio across Space pause/resume and seek; release resumes playback");
                window.ShortcutForVerification(Avalonia.Input.Key.R);
                window.SelectToolForVerification("SELECT");
                lines.Add("PASS native PAN updates the preview clock without editing the document");
                window.SeekForVerification(1);
                await Task.Delay(400);
                window.ConfigurePlacementForVerification(V2NoteType.Tap, EditorTrack.Left, 1);
                await HoverNative(window, 400, 500);
                await Capture(window, outDir, "06-side-ghost.png");
                await ClickNative(window, 400, 500);
                var leftNote = window.CurrentDocument?.SelectedChart.Notes.SingleOrDefault(note =>
                    note.Track == EditorTrack.Left && note.Type == V2NoteType.Tap);
                if (leftNote is null) throw new InvalidOperationException("Native left-track placement failed");
                await Capture(window, outDir, "06-left-before-drag.png");
                window.SelectToolForVerification("SELECT");
                var leftTime = leftNote.Time;
                var leftCenter = leftNote.Center;
                await DragNative(window, 420, 500, 470, 550);
                leftNote = window.CurrentDocument?.SelectedChart.Notes.Single(note => note.Id == leftNote.Id);
                if (leftNote is null || leftNote.Time == leftTime && leftNote.Center == leftCenter)
                    throw new InvalidOperationException("Native left-track drag did not update the Note");
                await PressCtrlKey(0x5A);
                await DragNative(window, 420, 530, 420, 590);
                leftNote = window.CurrentDocument?.SelectedChart.Notes.Single(note => note.Id == leftNote.Id);
                if (leftNote is null || leftNote.Width <= 1)
                    throw new InvalidOperationException("Native left-track edge resize failed");
                await PressCtrlKey(0x5A);
                window.ConfigurePlacementForVerification(V2NoteType.Tap, EditorTrack.Right, 1);
                await ClickNative(window, 1120, 500);
                if (window.CurrentDocument?.SelectedChart.Notes.Any(note =>
                    note.Track == EditorTrack.Right && note.Type == V2NoteType.Tap) != true)
                    throw new InvalidOperationException("Native right-track placement failed");
                window.SelectToolForVerification("SELECT");
                await Capture(window, outDir, "06-side-authoring.png");
                lines.Add("PASS left/right placement, side ghost, left drag and edge resize");
                window.LayoutFactory.AddAnalysisPanel();
                window.LayoutFactory.FloatStage();
                await Task.Delay(1800);
                if (!window.Stage.Host.Diagnostics().Embedded || window.Stage.Host.Diagnostics().WindowTitle == window.Title)
                    throw new InvalidOperationException("Floating stage failed");
                await Capture(window, outDir, "02-floating.png");
                window.LayoutFactory.DockStage();
                await Task.Delay(1800);
                if (window.Stage.Host.Diagnostics().WindowTitle != window.Title)
                    throw new InvalidOperationException("Dock restore failed");
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    window.LayoutFactory.FloatStage();
                    await Task.Delay(1200);
                    var nativeBeforeClose = window.Stage.Host.Diagnostics();
                    var processBeforeClose = window.StageServiceForVerification.ProcessId;
                    var documentBeforeClose = window.CurrentDocument;
                    var floating = TopLevel.GetTopLevel(window.Stage) as Window;
                    if (floating is null || ReferenceEquals(floating, window))
                        throw new InvalidOperationException("Stage did not float for close regression");
                    floating.Close(); // Same Closing path as the floating window's X button.
                    await Task.Delay(1500);
                    var restoredStage = window.Stage.Host.Diagnostics();
                    if (!restoredStage.Embedded || restoredStage.WindowTitle != window.Title ||
                        restoredStage.Child != nativeBeforeClose.Child ||
                        !window.StageServiceForVerification.Running ||
                        window.StageServiceForVerification.ProcessId != processBeforeClose ||
                        !ReferenceEquals(window.CurrentDocument, documentBeforeClose) || floating.IsVisible)
                        throw new InvalidOperationException($"Floating close did not restore the same live stage: {restoredStage}");
                    var ticksBeforeClose = window.TickCount;
                    await Task.Delay(400);
                    if (window.TickCount <= ticksBeforeClose)
                        throw new InvalidOperationException("Stage IPC stopped after floating close");
                }
                lines.Add("PASS floating window close restores the same live stage twice without losing the document");
                window.FindControl<ComboBox>("LanguageChoice")!.SelectedIndex = 1;
                await Task.Delay(300);
                await Capture(window, outDir, "03-editor-zh.png");
                await ClickNative(window,830, 714);
                await PressKey(0x70);
                if (!window.ShortcutsVisible) throw new InvalidOperationException("F1 did not open help");
                await Capture(window, outDir, "04-shortcuts-zh.png");
                await PressKey(0x1B);
                if (window.ShortcutsVisible) throw new InvalidOperationException("Escape did not close help");
                await WorkspaceDesktopVerification.Run(window, ClickNative, lines.Add);
                lines.Add("PASS formal editor package / audio / commands / native shortcuts / Dock / i18n");
                result = 0;
            }
            catch (Exception ex)
            {
                lines.Add("FAIL " + ex);
                result = 1;
            }
            finally
            {
                File.WriteAllLines(Path.Combine(outDir, "desktop-results.txt"), lines);
                Console.WriteLine(string.Join(Environment.NewLine, lines));
                window.Close();
                lifetime.Shutdown(result);
            }
        });
        lifetime.Start(Array.Empty<string>());
        return result;
    }

    private static async Task VerifyEmptyChartSwitch(MainWindow window, string outDir, Action<string> report)
    {
        var form = new EditorCreateForm("switch-test", "Empty Chart Switch", "Community",
            Path.Combine(outDir, "fixture.wav"), "main", 2, "", 12, false, "Verification", 120, 0, null);
        await window.CreatePackageForVerification(form);
        await window.AddChartForVerification(form with { ChartId = "extra", DifficultyIndex = 3 });
        var baseline = await window.StageServiceForVerification.AudioSnapshotAsync();
        var choice = window.FindControl<ComboBox>("ChartChoice")!;
        var items = choice.Items.Cast<ComboBoxItem>().ToArray();
        var pid = window.StageProcessId;
        for (var cycle = 0; cycle < 12; cycle++)
        {
            var target = cycle % 2;
            var center = choice.PointToScreen(new Point(choice.Bounds.Width / 2, choice.Bounds.Height / 2));
            await ClickNative(window, center.X, center.Y);
            await Task.Delay(100);
            var item = items[target];
            var point = item.PointToScreen(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2));
            // Popup has its own HWND: do not reactivate the main window before clicking the item.
            await Task.Run(() =>
            {
                SetCursorPos(point.X, point.Y);
                mouse_event(2, 0, 0, 0, 0);
                Thread.Sleep(60);
                mouse_event(4, 0, 0, 0, 0);
            });
            await Task.Delay(150);
            var snapshot = await window.StageServiceForVerification.AudioSnapshotAsync();
            if (window.CurrentDocument?.SelectedChartId != (target == 0 ? "main" : "extra") ||
                window.CurrentDocument.SelectedChart.Notes.Count != 0 || snapshot.Loads != baseline.Loads ||
                snapshot.Playing || window.StageProcessId != pid || !choice.Items.Cast<ComboBoxItem>().SequenceEqual(items))
                throw new InvalidOperationException($"Empty chart switch {cycle} failed: {snapshot}");
        }
        report("PASS 12 real ComboBox mouse switches between empty charts; same audio stream and Godot process");
    }

    private static async Task VerifyActivation(MainWindow window, Action<string> report)
    {
        window.Topmost = false;
        var handle = window.TryGetPlatformHandle()!.Handle;
        var child = window.Stage.Host.Diagnostics().Child;
        var other = new Window { Title = "Activation regression", Width = 400, Height = 250 };
        try
        {
            other.Show();
            var otherHandle = other.TryGetPlatformHandle()!.Handle;
            for (var cycle = 0; cycle < 2; cycle++)
            {
                var point = other.PointToScreen(new Point(200, 100));
                await ClickNative(other, point.X, point.Y);
                await Task.Delay(200);
                if (GetForegroundWindow() != otherHandle)
                    throw new InvalidOperationException($"Could not establish other foreground: actual={GetForegroundWindow()} expected={otherHandle}");
                window.Width += cycle == 0 ? 10 : -10;
                window.Stage.Host.QueueSync();
                window.Stage.Host.FocusNative(); // Simulate delayed selection while another window is active.
                await Task.Delay(300);
                if (GetForegroundWindow() != otherHandle)
                    throw new InvalidOperationException($"Background layout/selection stole activation: actual={GetForegroundWindow()} expected={otherHandle} host={handle} child={child}");
                window.WindowState = WindowState.Minimized;
                await Task.Delay(150);
                window.WindowState = WindowState.Normal;
                ActivateWindow(window);
                await Task.Delay(350);
                var d = window.Stage.Host.Diagnostics();
                if (GetForegroundWindow() != handle || !d.Embedded || d.Child != child)
                    throw new InvalidOperationException("Host restore failed to retain foreground and child");
                var stagePoint = window.Stage.Host.PointToScreen(new Point(
                    window.Stage.Host.Bounds.Width / 2, window.Stage.Host.Bounds.Height / 2));
                await ClickNative(window, stagePoint.X, stagePoint.Y);
                if (GetForegroundWindow() != handle || GetWindow((nint)child, 4) != 0)
                    throw new InvalidOperationException("Stage click activated a child or retained a popup owner");
                await PressKey(0x42);
                if (!window.WheelReversed)
                    throw new InvalidOperationException("Native keyboard focus lost after activation");
                await PressKey(0x42);
                if (window.WheelReversed) throw new InvalidOperationException("Second native B did not restore direction");
            }
            report("PASS non-topmost restore, background layout/focus isolation and native B twice");
        }
        finally { other.Close(); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private static async Task VerifyPanAudioSuspension(MainWindow window)
    {
        // Begin paused so Space inside the held PAN must not start an audio stream at all.
        if (window.Playing) await PressKey(0x20);
        window.SeekForVerification(1);
        await Task.Delay(350);
        ActivateWindow(window);
        await Task.Delay(150);
        StageAudioSnapshot suspended;
        try
        {
            await Task.Run(() =>
            {
                SetCursorPos(700, 500);
                mouse_event(1, 0, 0, 0, 0);
                Thread.Sleep(150); // Let the embedded child receive enter/focus before button-down.
                mouse_event(2, 0, 0, 0, 0);
            });
            await Task.Delay(200);
            var pressed = await window.StageServiceForVerification.AudioSnapshotAsync();
            await PressKey(0x20);
            window.SeekForVerification(2);
            await Task.Delay(250);
            await PressKey(0x20);
            await PressKey(0x20);
            suspended = await window.StageServiceForVerification.AudioSnapshotAsync();
            await Task.Delay(300);
            var held = await window.StageServiceForVerification.AudioSnapshotAsync();
            // StreamPaused reflects a live playback instance, not the requested suspension:
            // after Stop Godot can report false. Here audio began stopped, so require the
            // logical PAN gate plus no actual playback/restarts/position advancement instead.
            if (!window.Playing || !pressed.PanSuspended || !suspended.PanSuspended || !held.PanSuspended ||
                pressed.Playing || suspended.Playing || held.Playing ||
                pressed.Loads != suspended.Loads || pressed.Restarts != suspended.Restarts ||
                suspended.Loads != held.Loads || suspended.Restarts != held.Restarts ||
                Math.Abs(pressed.AudioSecond - suspended.AudioSecond) > .02 ||
                Math.Abs(suspended.AudioSecond - held.AudioSecond) > .02 ||
                Math.Abs(suspended.Second - held.Second) > 1e-6 ||
                Math.Abs(suspended.Second - pressed.Second) < .1)
                throw new InvalidOperationException(
                    $"Held PAN did not suspend actual audio across Space/seek: {pressed} -> {suspended} -> {held}");
        }
        finally
        {
            // Never leave the native mouse captured if a shortcut, snapshot or assertion fails.
            await Task.Run(() => mouse_event(4, 0, 0, 0, 0));
        }
        await Task.Delay(250);
        var resumed = await window.StageServiceForVerification.AudioSnapshotAsync();
        if (!resumed.Playing || resumed.Paused || resumed.PanSuspended || resumed.Loads != suspended.Loads ||
            resumed.Restarts != suspended.Restarts + 1 || resumed.Second <= suspended.Second ||
            Math.Abs(resumed.AudioSecond - resumed.Second) > .35)
            throw new InvalidOperationException($"PAN release did not resume audio at the seek position: {suspended} -> {resumed}");
        await Task.Delay(300);
        AssertAudioContinuous(resumed, await window.StageServiceForVerification.AudioSnapshotAsync());
        await PressKey(0x20);
    }

    private static void AssertAudioContinuous(StageAudioSnapshot before, StageAudioSnapshot after)
    {
        if (!before.Playing || before.Paused || before.PanSuspended ||
            !after.Playing || after.Paused || after.PanSuspended ||
            before.Loads != after.Loads || before.Restarts != after.Restarts)
            throw new InvalidOperationException($"Incremental edit interrupted actual audio: {before} -> {after}");
        var clockDelta = after.Second - before.Second;
        var audioDelta = after.AudioSecond - before.AudioSecond;
        if (!double.IsFinite(after.AudioSecond) || clockDelta <= .05 || audioDelta <= .02 ||
            Math.Abs(audioDelta - clockDelta) > .25 || Math.Abs(after.AudioSecond - after.Second) > .35)
            throw new InvalidOperationException($"Actual audio did not advance in sync with the playhead: {before} -> {after}");
    }

    private static void ActivateWindow(Window window)
    {
        window.Activate();
        SetForegroundWindow(window.TryGetPlatformHandle()?.Handle ?? 0);
    }

    private static async Task Capture(Window window, string outDir, string name)
    {
        ActivateWindow(window);
        await Task.Delay(150);
        var script = Path.GetFullPath(Path.Combine(outDir, "../../.tmp/ui-research/capture_frame.py"));
        var start = new System.Diagnostics.ProcessStartInfo("python")
        {
            UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true,
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(Path.GetFullPath(Path.Combine(outDir, name)));
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("DXGI capture failed: " + await process.StandardError.ReadToEndAsync());
    }

    private static async Task HoverNative(Window window, int x, int y)
    {
        ActivateWindow(window);
        await Task.Run(() =>
        {
            SetCursorPos(x, y);
            mouse_event(1, 0, 0, 0, 0);
        });
        await Task.Delay(350);
    }

    private static async Task ClickNative(Window window, int x, int y)
    {
        ActivateWindow(window);
        await Task.Delay(150);
        await Task.Run(() =>
        {
            SetCursorPos(x, y);
            mouse_event(1, 0, 0, 0, 0);
            Thread.Sleep(150);
            mouse_event(2, 0, 0, 0, 0);
            Thread.Sleep(60);
            mouse_event(4, 0, 0, 0, 0);
        });
        await Task.Delay(500);
    }

    private static async Task DoubleClickNative(Window window, int x, int y)
    {
        ActivateWindow(window);
        await Task.Delay(150);
        await Task.Run(() =>
        {
            SetCursorPos(x, y);
            for (var index = 0; index < 2; index++)
            {
                mouse_event(2, 0, 0, 0, 0);
                Thread.Sleep(55);
                mouse_event(4, 0, 0, 0, 0);
                Thread.Sleep(70);
            }
        });
        await Task.Delay(700);
    }

    private static async Task DragNative(Window window, int fromX, int fromY, int toX, int toY)
    {
        ActivateWindow(window);
        await Task.Delay(150);
        await Task.Run(() =>
        {
            SetCursorPos(fromX, fromY);
            mouse_event(2, 0, 0, 0, 0);
            Thread.Sleep(100);
            SetCursorPos(toX, toY);
            mouse_event(1, 0, 0, 0, 0);
            Thread.Sleep(120);
            mouse_event(4, 0, 0, 0, 0);
        });
        await Task.Delay(700);
    }

    private static async Task PressKey(byte virtualKey)
    {
        await Task.Run(() =>
        {
            keybd_event(virtualKey, 0, 0, 0);
            Thread.Sleep(60);
            keybd_event(virtualKey, 0, 2, 0);
        });
        await Task.Delay(400);
    }

    private static async Task PressCtrlKey(byte virtualKey)
    {
        const byte control = 0x11;
        await Task.Run(() =>
        {
            keybd_event(control, 0, 0, 0);
            keybd_event(virtualKey, 0, 0, 0);
            Thread.Sleep(60);
            keybd_event(virtualKey, 0, 2, 0);
            keybd_event(control, 0, 2, 0);
        });
        await Task.Delay(700);
    }

    private static bool HasVisibleTopLevelWindow(int processId)
    {
        if (processId == 0) return false;
        var found = false;
        EnumWindows((handle, _) =>
        {
            if (found || !IsWindowVisible(handle) || GetParent(handle) != 0) return true;
            GetWindowThreadProcessId(handle, out var owner);
            if (owner != processId) return true;
            found = true;
            return false;
        }, 0);
        return found;
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int processId);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nint extra);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nint extra);
}
