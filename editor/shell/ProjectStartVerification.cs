using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Enter via the real start-page actions, substituting only the OS folder picker and process launch.</summary>
internal static class ProjectStartVerification
{
    internal static async Task Run(string root, string source, Action<string, bool> check)
    {
        var launches = 0;
        var picks = 0;
        string? folder = null;
        var window = new MainWindow(Path.Combine(root, "open"), () => launches++);
        window.PickOpenFolderForVerification = () => { picks++; return Task.FromResult(folder); };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            bool AtStart() => window.CurrentDocument is null && Visible(window, "StartPage") && !Visible(window, "ProjectWorkspace");
            check("Start page offers open and new with no stage or attached editor layout", AtStart() &&
                Visible(window, "StartOpenButton") && Visible(window, "StartNewButton") && launches == 0 &&
                !window.Ready && window.StageProcessId == 0 && window.FindControl<Border>("ProjectDockHost")!.Child is null);
            var speed = window.PlayerSpeed;
            KeyStroke(window, Key.E, PhysicalKey.E); KeyStroke(window, Key.D2, PhysicalKey.Digit2);
            check("Start page ignores project editing shortcuts", AtStart() && window.PlayerSpeed == speed &&
                window.CurrentTool == "SELECT" && launches == 0);
            Click(window, "StartOpenButton");
            check("Canceling the actual open button picker keeps the stage cold", picks == 1 && AtStart() && launches == 0);
            Click(window, "StartNewButton");
            var form = window.OwnedWindows.OfType<CreateChartDialog>().Single();
            form.Close(); Dispatcher.UIThread.RunJobs();
            check("Canceling the actual new-package dialog returns to the same start page", AtStart() && launches == 0);
            folder = Path.Combine(root, "broken"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "meta.json"), "{");
            KeyStroke(window, Key.O, PhysicalKey.O, RawInputModifiers.Control);
            await Until(() => window.StatusMessage.Contains(WorkbenchLocalizer.Shared.Format("Message.FileError", ""), StringComparison.Ordinal), "failed open result");
            check("Ctrl+O routes to the picker; an unreadable package keeps start page and no stage", picks == 2 && AtStart() && launches == 0);
            folder = source;
            Click(window, "StartOpenButton");
            await Until(() => window.CurrentDocument is not null, "open valid package");
            check("Opening a valid package shows the editor and requests one stage", launches == 1 && !Visible(window, "StartPage") &&
                Visible(window, "ProjectWorkspace") && window.CurrentDocument?.Title == "Shell Fixture" && picks == 3);
            await window.OpenPackageForVerification(source);
            check("Replacing a clean document does not request another stage", launches == 1 && window.StageProcessId == 0);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }

        var attempts = 0;
        var draft = new MainWindow(Path.Combine(root, "new"), () => { if (++attempts == 1) throw new IOException("fixture launch failed"); });
        try
        {
            draft.Show(); Dispatcher.UIThread.RunJobs();
            KeyStroke(draft, Key.N, PhysicalKey.N, RawInputModifiers.Control);
            var form = draft.OwnedWindows.OfType<CreateChartDialog>().Single();
            var fields = new EditorCreateForm("new-chart", "Empty Chart", "Fixture", Path.Combine(Path.GetDirectoryName(source)!, "fixture.wav"),
                "main", 2, "", 12, false, "Fixture", 120, 0, null);
            form.SetFormForVerification(fields with { AudioPath = Path.Combine(root, "missing.wav") });
            Click(form, "ConfirmButton");
            check("Invalid new-package fields leave form open and do not launch the stage", form.IsVisible &&
                form.FindControl<TextBlock>("ErrorText")!.IsVisible && draft.CurrentDocument is null && attempts == 0);
            form.SetFormForVerification(fields); Click(form, "ConfirmButton");
            await Until(() => draft.CurrentDocument is not null, "adopt empty draft");
            var document = draft.CurrentDocument!;
            check("Ctrl+N creates an editable zero-note draft and failed stage launch does not discard it", document.IsUnpersisted &&
                document.SelectedChart.Notes.Count == 0 && Visible(draft, "ProjectWorkspace") && !Visible(draft, "StartPage") &&
                Visible(draft, "RetryStageButton") && attempts == 1);
            draft.SelectChartForVerification("main");
            check("Document refresh does not retry a failed process implicitly", attempts == 1);
            Click(draft, "RetryStageButton");
            draft.SelectChartForVerification("main");
            check("Explicit retry requests one replacement stage and keeps the draft", attempts == 2 &&
                ReferenceEquals(document, draft.CurrentDocument) && !Visible(draft, "RetryStageButton") && draft.StageProcessId == 0);
            var revision = draft.StageRevision;
            draft.StageEventForVerification("ready", "{\"hwnd\":0}");
            check("Ready IPC sends the adopted draft as the first full chart snapshot", draft.Ready &&
                draft.StageRevision == revision + 1 && !draft.LastStageUpdateIncremental);
            draft.StageEventForVerification("tick", """{"second":0,"bar":0,"playing":true,"playerSpeed":1,"playbackRate":1}""");
            var wasPlaying = draft.Playing;
            draft.StageEventForVerification("exited", "{}");
            check("Process-exit IPC clears playback and offers retry without discarding the draft", wasPlaying &&
                !draft.Playing && !draft.Ready && Visible(draft, "RetryStageButton") && ReferenceEquals(document, draft.CurrentDocument));
            Click(draft, "RetryStageButton");
            check("Retry after exit requests the stage once, with no additional process on refresh", attempts == 3 &&
                !Visible(draft, "RetryStageButton") && !draft.Playing);
        }
        finally
        {
            draft.ClosePromptForVerification = () => Task.FromResult(UnsavedCloseChoice.Discard);
            draft.Close(); await draft.CloseDecisionForVerification; Dispatcher.UIThread.RunJobs();
        }
    }

    private static bool Visible(Window owner, string name) => owner.FindControl<Control>(name)?.IsEffectivelyVisible == true;
    private static void Click(Window owner, string name)
    {
        var button = owner.FindControl<Button>(name) ?? throw new InvalidOperationException("Missing button: " + name);
        owner.UpdateLayout();
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), owner)!.Value;
        owner.MouseMove(point); owner.MouseDown(point, MouseButton.Left); owner.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
    private static void KeyStroke(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null); window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }
    private static async Task Until(Func<bool> condition, string action)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException(action);
            await Task.Delay(10);
        }
    }
}
