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
        AppBuilder.Configure<App>().UsePlatformDetect()
            .With(new Win32PlatformOptions { CompositionMode = new[] { Win32CompositionMode.RedirectionSurface } })
            .AfterSetup(_ => result = -1).SetupWithLifetime(lifetime);
        var window = new MainWindow();
        lifetime.MainWindow = window;
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
                var source = Path.Combine(outDir, "fixture-source");
                if (Directory.Exists(source)) Directory.Delete(source, true);
                Verification.CreatePackage(source);
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
                window.ShortcutForVerification(Avalonia.Input.Key.D);
                window.ShortcutForVerification(Avalonia.Input.Key.D);
                await Task.Delay(700);
                lines.Add("stage " + window.LastStageInput);
                if (!window.LastStageInput.Contains("loaded chart=main notes=1 audio=True"))
                    throw new InvalidOperationException("The real chart and audio were not loaded by Godot");
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
                await PressKey(0x20);
                await Task.Delay(650);
                await PressKey(0x20);
                if (window.CurrentSecond <= before) throw new InvalidOperationException("Space did not advance music time");
                lines.Add($"native shortcuts speed/rate/playback passed; time {before:0.000} -> {window.CurrentSecond:0.000}");
                await PressCtrlKey(0x53);
                if (window.CurrentDocument?.HasUnsavedChanges != false ||
                    EditorPackageRepository.Open(source).SelectedChart.Notes[0].ScrollSpeedMultiplier != 1.35)
                    throw new InvalidOperationException("Ctrl+S did not save the document command");
                lines.Add("PASS real Ctrl+S persisted Note speed");
                window.ConfigurePlacementForVerification(V2NoteType.Hold, EditorTrack.Center, 1);
                await ClickNative(window, 830, 714);
                var hold = window.CurrentDocument?.SelectedChart.Notes.SingleOrDefault(note => note.Type == V2NoteType.Hold);
                if (hold is null) throw new InvalidOperationException("Native placement click did not add a Hold");
                var holdId = hold.Id;
                window.ShortcutForVerification(Avalonia.Input.Key.D3);
                await DoubleClickNative(window, 830, 650);
                var insertedHold = window.CurrentDocument!.SelectedChart.Notes.Single(note => note.Id == holdId);
                if (insertedHold.Nodes.Count != 2) throw new InvalidOperationException("Native path double-click did not insert a node");
                var oldCenter = insertedHold.Center;
                await DragNative(window, 830, 714, 880, 714);
                var movedHold = window.CurrentDocument!.SelectedChart.Notes.Single(note => note.Id == holdId);
                if (Math.Abs(movedHold.Center - oldCenter) < .001) throw new InvalidOperationException("Native path head drag did not move the Hold");
                lines.Add($"PASS native Hold placement / path insertion / path drag ({holdId}, nodes={movedHold.Nodes.Count})");
                await Capture(window, outDir, "05-authoring.png");
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
                window.FindControl<ComboBox>("LanguageChoice")!.SelectedIndex = 1;
                await Task.Delay(300);
                await Capture(window, outDir, "03-editor-zh.png");
                await ClickNative(window,830, 714);
                await PressKey(0x70);
                if (!window.ShortcutsVisible) throw new InvalidOperationException("F1 did not open help");
                await Capture(window, outDir, "04-shortcuts-zh.png");
                await PressKey(0x1B);
                if (window.ShortcutsVisible) throw new InvalidOperationException("Escape did not close help");
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

    private static async Task ClickNative(Window window, int x, int y)
    {
        ActivateWindow(window);
        await Task.Delay(150);
        await Task.Run(() =>
        {
            SetCursorPos(x, y);
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
