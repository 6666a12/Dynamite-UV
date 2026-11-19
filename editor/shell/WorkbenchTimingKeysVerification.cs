using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor.Shell;

internal static class WorkbenchTimingKeysVerification
{
    public static async Task Run(string root, Action<string, bool> check)
    {
        var pack = Path.Combine(root, "song");
        Verification.CreatePackage(pack);
        var w = new MainWindow(Path.Combine(root, "prefs"));
        try
        {
            w.Show(); await w.OpenPackageForVerification(pack); Dispatcher.UIThread.RunJobs();
            w.StageEventForVerification("ready", "{\"hwnd\":0}");
            var doc = w.CurrentDocument!;
            var initialVersion = doc.ChangeVersion;
            var loaded = w.StageRevision;
            void Stage(string key, bool pressed = true, bool echo = false, bool shift = false, long? load = null) =>
                w.StageEventForVerification("key", JsonSerializer.Serialize(new { key, pressed, echo, shift, ctrl = false, alt = false, loadRevision = load ?? loaded }));
            void Press(Key key, PhysicalKey physical, RawInputModifiers mods = RawInputModifiers.None)
            {
                w.KeyPress(key, mods, physical, null); w.KeyRelease(key, mods, physical, null);
                Dispatcher.UIThread.RunJobs();
            }
            Stage("Left"); Stage("Left", echo: true); Stage("Right"); Stage("Right"); Stage("Down");
            check("Guide keys cycle three sides independently without key-repeat, document edits or reload", w.GuideLevelForVerification(Key.Left)==1 &&
                w.GuideLevelForVerification(Key.Right)==2 && w.GuideLevelForVerification(Key.Down)==1 && !doc.HasUnsavedChanges &&
                doc.ChangeVersion == initialVersion && w.StageRevision == loaded);
            Stage("Right"); check("A third guide press switches that side off", w.GuideLevelForVerification(Key.Right)==0);
            var bar = doc.SelectedChart.Notes[0].Time;
            foreach (var key in new[] { "O", "O", "O", "P", "P", "P" }) { Stage(key); Stage(key, false); }
            var readout = w.Timing.FindControl<TextBlock>("OffsetReadout")!;
            var offsetBox = w.Timing.FindControl<TextBox>("OffsetMsInput")!;
            check("Six separate O/P presses cancel to exact zero in chart, readout and offset box",
                BitConverter.DoubleToInt64Bits(doc.SelectedChart.AudioOffsetSec) == 0 && offsetBox.Text == "0" && readout.Text!.EndsWith("0 ms"));
            await w.SavePackageForVerification();
            check("Cancelling O/P presses writes exact zero on strict save and reopen",
                BitConverter.DoubleToInt64Bits(EditorPackageRepository.Open(pack).SelectedChart.AudioOffsetSec) == 0);
            Stage("O"); Stage("O", echo:true); Stage("O", echo:true, shift:true); Stage("O",false);
            check("Native O repeat and Shift move only audio alignment (+21ms) and use incremental preview", Math.Abs(doc.SelectedChart.AudioOffsetSec-.021)<1e-12 &&
                doc.SelectedChart.Notes[0].Time == bar && w.LastStageUpdateIncremental);
            w.ShortcutForVerification(Key.Z,KeyModifiers.Control);
            check("One undo restores a whole O hold and the clean saved state", doc.SelectedChart.AudioOffsetSec==0 && !doc.HasUnsavedChanges);
            w.ShortcutForVerification(Key.Y,KeyModifiers.Control);
            // Focus an ordinary workbench button so the following strokes are delivered by Avalonia rather than IPC.
            w.FindControl<Button>("ProjectSave")!.Focus();
            Press(Key.P, PhysicalKey.P, RawInputModifiers.Shift); Press(Key.P, PhysicalKey.P);
            check("Shell Shift+P and P move notes earlier, and two taps keep separate undo entries", Math.Abs(doc.SelectedChart.AudioOffsetSec-.010)<1e-12);
            w.ShortcutForVerification(Key.Z,KeyModifiers.Control);
            check("Shell key release closes the previous offset gesture", Math.Abs(doc.SelectedChart.AudioOffsetSec-.020)<1e-12);
            w.CopyChartForVerification(); var copy = doc.SelectedChartId; var copiedOffset = doc.SelectedChart.AudioOffsetSec;
            var copyLoad = w.StageRevision; Stage("P",load:loaded); Stage("O",echo:true,load:copyLoad);
            check("Queued input from previous chart or orphan repeat cannot edit the new difficulty", doc.SelectedChart.AudioOffsetSec==copiedOffset);
            Stage("P",load:copyLoad); Stage("P",false,load:copyLoad);
            check("Only the active difficulty offset changes", copy!="main" && Math.Abs(doc.SelectedChart.AudioOffsetSec-.010)<1e-12 &&
                Math.Abs(doc.Charts.Single(c=>c.Id=="main").AudioOffsetSec-.020)<1e-12);
            w.SelectNoteForVerification(doc.SelectedChart.Notes[0].Id);
            var field = w.GetVisualDescendants().OfType<InspectorView>().Single().FindControl<TextBox>("BarTimeValue")!;
            w.UpdateLayout(); field.Focus(); var version=doc.ChangeVersion; Press(Key.P,PhysicalKey.P);
            check("Typing in a chart field cannot nudge offset", field.IsFocused && version==doc.ChangeVersion);
            var ms = w.Timing.FindControl<TextBox>("OffsetMsInput")!;
            var apply = w.Timing.FindControl<Button>("OffsetApplyButton")!;
            void Apply(string input) { ms.Text = input; apply.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); }
            Apply("1e15");
            w.ShortcutForVerification(Key.R);
            check("Huge finite TIMING offset survives apply and seek to bar zero without readout overflow",
                doc.SelectedChart.AudioOffsetSec == 1e12 && w.CurrentSecond == 1e12 && w.CurrentBar == 0 &&
                w.FindControl<TextBlock>("TimeLabel")!.Text!.Contains("1E+12 s"));
            w.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            w.ShortcutForVerification(Key.R);
            check("Undo and seek restore normal mm:ss.fff transport display",
                w.FindControl<TextBlock>("TimeLabel")!.Text!.Contains("00:00.010"));
            Apply("-3.469446951953614E-15");
            check("A preexisting or intentional tiny offset is not rounded to zero in either display",
                doc.SelectedChart.AudioOffsetSec != 0 && ms.Text == "-3.469446951953614E-15" && readout.Text!.Contains(ms.Text));
            Apply("0");
            check("Explicit zero cleans an old residual in the document and both displays", doc.SelectedChart.AudioOffsetSec == 0 && ms.Text == "0" && readout.Text!.EndsWith("0 ms"));
            w.ShortcutForVerification(Key.Z,KeyModifiers.Control); w.ShortcutForVerification(Key.Z,KeyModifiers.Control);
            Apply("-12.5");
            check("Timing panel accepts fractional milliseconds for the current difficulty", Math.Abs(doc.SelectedChart.AudioOffsetSec + .0125)<1e-12);
            version = doc.ChangeVersion; Apply("NaN"); w.Timing.SetCurrentBar(5); w.Timing.SetDocument(doc);
            check("Invalid offset does not edit chart; refresh does not replace typed input", doc.ChangeVersion == version && ms.Text == "NaN");
            w.ShortcutForVerification(Key.Z,KeyModifiers.Control);
            await w.SavePackageForVerification();
            check("Offsets survive strict save and reopen", Math.Abs(EditorPackageRepository.Open(pack).Charts.Single(c=>c.Id==copy).AudioOffsetSec-.010)<1e-12);
        }
        finally
        {
            w.ClosePromptForVerification=()=>Task.FromResult(UnsavedCloseChoice.Discard);
            w.Close(); await w.CloseDecisionForVerification; Dispatcher.UIThread.RunJobs();
        }
    }
}
