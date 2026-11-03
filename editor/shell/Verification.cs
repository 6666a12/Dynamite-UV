using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
        var window = new MainWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Check("Real Dock layout", window.GetVisualDescendants().OfType<DockControl>()
                .Single().GetVisualDescendants().OfType<DocumentControl>().Any());
            Check("Ursa controls are templated", window.GetVisualDescendants().OfType<IconButton>()
                .All(control => control.GetVisualChildren().Any()));
            Check("Q/E changes preview speed", window.ShortcutForVerification(Key.E) && window.PlayerSpeed == 1.1);
            Check("W/S changes playback rate", window.ShortcutForVerification(Key.W) &&
                window.PlaybackRate == 1.1 && window.ShortcutForVerification(Key.S) && window.PlaybackRate == 1.0);
            Check("Number shortcut changes tool", window.ShortcutForVerification(Key.D2) && window.CurrentTool == "PLACE");
            Check("F1 opens and Escape closes help", window.ShortcutForVerification(Key.F1) &&
                window.ShortcutsVisible && window.ShortcutForVerification(Key.Escape) && !window.ShortcutsVisible);
            CreatePackage(source);
            await window.OpenPackageForVerification(source);
            Check("Shell opens a strict v2 package", window.CurrentDocument?.Title == "Shell Fixture" &&
                window.CurrentDocument.SelectedChart.Notes.Count == 1);
            window.SelectNoteForVerification("fixture-note");
            window.ApplySpeedForVerification(1.35);
            Check("Inspector applies a real document command", window.CurrentDocument?.HasUnsavedChanges == true &&
                window.CurrentDocument.SelectedChart.Notes[0].ScrollSpeedMultiplier == 1.35);
            window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            Check("Undo reverts Note speed", window.CurrentDocument?.SelectedChart.Notes[0].ScrollSpeedMultiplier == 1.0);
            window.ShortcutForVerification(Key.Y, KeyModifiers.Control);
            Check("Redo restores Note speed", window.CurrentDocument?.SelectedChart.Notes[0].ScrollSpeedMultiplier == 1.35);
            await window.SavePackageToForVerification(copy);
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
                window.GetVisualDescendants().OfType<ComboBox>().Any(choice => Equals(choice.SelectionBoxItem, "线性")));
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
            window.StageEventForVerification("place", "{\"type\":0,\"track\":1,\"bar\":1,\"center\":2.5,\"width\":1,\"tailBar\":1}");
            window.SelectChartForVerification("main");
            window.ConfigurePlacementForVerification(V2NoteType.Hold, EditorTrack.Center, 1);
            window.StageEventForVerification("place", "{\"type\":0,\"track\":1,\"bar\":0.5,\"center\":2.5,\"width\":1,\"tailBar\":0.5}");
            window.StageEventForVerification("place", "{\"type\":3,\"track\":1,\"bar\":1,\"center\":2.5,\"width\":1,\"tailBar\":2}");
            Check("Stage placement adds Tap and Hold through document commands",
                window.CurrentDocument?.SelectedChart.Notes.Count == 2 &&
                window.CurrentDocument.SelectedChart.Notes.Any(note => note.Type == V2NoteType.Hold && note.Nodes.Count == 1));
            var holdId = window.CurrentDocument!.SelectedChart.Notes.Single(note => note.Type == V2NoteType.Hold).Id;
            window.StageEventForVerification("pathAdd", $"{{\"noteId\":\"{holdId}\",\"bar\":1.5}}");
            Check("Path double-click event inserts a sampled node", window.CurrentDocument?.SelectedChart.Notes
                .Single(note => note.Id == holdId).Nodes.Count == 2);
            var nodeId = window.CurrentDocument!.SelectedChart.Notes.Single(note => note.Id == holdId).Nodes[0].Id;
            window.StageEventForVerification("pathMove",
                $"{{\"noteId\":\"{holdId}\",\"nodeId\":\"{nodeId}\",\"startBar\":1.5,\"endBar\":1.75,\"startCenter\":2.5,\"endCenter\":3.0}}");
            var movedNode = window.CurrentDocument?.SelectedChart.Notes.Single(note => note.Id == holdId).Nodes[0];
            Check("Path drag edits exact node time and center", movedNode?.Time == ExactBarTime.FromFraction(7, 4) &&
                movedNode.Center == 3.0);
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
        const int samples = rate * 4;
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
