using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

internal static class CustomCurveVerification
{
    private const string Rebound = "t+4*t*(1-t)";
    private static ExactBarTime Bar(int n) => ExactBarTime.FromFraction(n, 1);
    internal static async Task Run(string root, Action<string, bool> check)
    {
        var pack = Path.Combine(root, "song"); var copy = Path.Combine(root, "copy");
        Verification.CreatePackage(pack);
        var setup = new EditorDocumentSession(); var source = setup.Open(pack);
        source.Execute(new AddScrollCommand(Bar(0), 1)); source.Execute(new AddScrollCommand(Bar(2), 2));
        source.Execute(new AddNoteCommand(new EditableNote { Id = "ribbon", Type = V2NoteType.Hold,
            Track = EditorTrack.Center, Time = Bar(1), Center = 1, Width = 1, CurveToNext = V2PathCurve.Smooth,
            Nodes = { new EditablePathNode { Id = "knee", Time = Bar(2), Center = 3, Width = .25, Judge = false },
                new EditablePathNode { Id = "tip", Time = Bar(3), Center = 2, Width = .25, Judge = true } } }));
        setup.Save(pack);
        var path = Path.Combine(pack, source.SelectedChart.File); var bytes = File.ReadAllBytes(path);
        var w = new MainWindow(Path.Combine(root, "preferences"));
        try
        {
            w.Show(); await w.OpenPackageForVerification(pack); Dispatcher.UIThread.RunJobs();
            var doc = w.CurrentDocument!; var version = doc.ChangeVersion;
            var timing = w.Timing; var inspector = w.GetVisualDescendants().OfType<InspectorView>().Single();
            var sc = timing.FindControl<ComboBox>("ScrollCurveChoice")!; var pc = inspector.FindControl<ComboBox>("CurveChoice")!;
            var scroll = timing.Formula; var ribbon = inspector.Formula;
            var expr = scroll.FindControl<TextBox>("ExpressionInput")!; var pathExpr = ribbon.FindControl<TextBox>("ExpressionInput")!;
            var plot = scroll.FindControl<CurvePlot>("PreviewPlot")!; var notice = scroll.FindControl<TextBlock>("FormulaNotice")!;
            Scroll(Bar(2)); check("Custom: tail Scroll has no outgoing editor", !sc.IsEnabled && !scroll.IsVisible);
            Scroll(Bar(0)); var originalPreset = Tag(sc); Choose(sc, "Custom"); expr.Text = "unknown(t)";
            Click(timing, "ApplyScrollButton");
            check("Custom: invalid text shows a compiler diagnostic and does not execute a command", doc.ChangeVersion == version && scroll.IsVisible && notice.IsVisible && plot.Curve is null);
            w.SelectNoteForVerification("ribbon");
            check("Custom: original Smooth stays selected; Custom follows Smooth in the path menu", Tag(pc) == "Smooth" && !ribbon.IsVisible && pc.Items.OfType<ComboBoxItem>().Last().Tag as string == "Custom");
            w.SelectNoteForVerification("fixture-note"); w.ApplySpeedForVerification(1.5);
            await w.SavePackageForVerification(); await w.SavePackageToForVerification(copy);
            check("Custom: hidden invalid draft blocks both save paths, leaving chart bytes unchanged", !Directory.Exists(copy) && File.ReadAllBytes(path).SequenceEqual(bytes) && w.StatusMessage.Contains("Scroll"));
            Scroll(Bar(0)); check("Custom: refresh/reselect restores invalid draft on its original event", Tag(sc) == "Custom" && expr.Text == "unknown(t)" && notice.IsVisible);
            Click(scroll, "DiscardFormula"); check("Custom: explicit discard restores original choice", Tag(sc) == originalPreset && !scroll.IsVisible && scroll.PendingLocation is null);
            Choose(sc, "Custom"); expr.Text = Rebound; version = doc.ChangeVersion;
            var actual = plot.Curve;
            check("Custom: overshooting draft graphs actual runtime knots and warns without editing", actual is { Knots.Count: > 2, Maximum: > 1 } && actual.Evaluate(.5) == 1.5 && notice.IsVisible && doc.SelectedChart.ScrollSpeeds[0].CustomExpr is null);
            // Render the actual plot control using the same headless Skia backend as the workbench.
            plot.Measure(new Size(180, 75)); plot.Arrange(new Rect(0, 0, 180, 75));
            using (var bitmap = new RenderTargetBitmap(new PixelSize(180, 75), new Vector(96, 96))) bitmap.Render(plot);
            await w.SavePackageToForVerification(copy);
            check("Custom: valid unapplied draft is not silently omitted from Save As", !Directory.Exists(copy) && doc.ChangeVersion == version);
            var beforeAddNotice = notice.Text;
            Click(timing, "AddScrollButton"); // Duplicate bar 0: the destination fails, not the selected segment.
            check("Custom: rejected Add neither consumes nor attaches its error to the original draft", doc.ChangeVersion == version && scroll.HasDraft && expr.Text == Rebound && notice.Text == beforeAddNotice);
            timing.FindControl<TextBox>("ScrollTimeValue")!.Text = "1";
            Click(timing, "AddScrollButton");
            var added = doc.SelectedChart.ScrollSpeeds.Single(s => s.Time == Bar(1));
            check("Custom: Add copies the displayed formula and selects the new event without committing the bar-0 draft", doc.ChangeVersion == version + 1 && doc.Selection.Key == added.Key && added.CustomExpr == Rebound && doc.SelectedChart.ScrollSpeeds[0].CustomExpr is null && !scroll.FindControl<Button>("DiscardFormula")!.IsVisible);
            // These calls must be blocked synchronously, not leave an unanswered upgrade sheet.
            var hiddenSave = w.SavePackageForVerification();
            if (w.FindControl<Border>("WorkbenchModalLayer")!.IsVisible) Respond("CancelCurveUpgrade");
            await hiddenSave;
            var hiddenSaveAs = w.SavePackageToForVerification(copy);
            if (w.FindControl<Border>("WorkbenchModalLayer")!.IsVisible) Respond("CancelCurveUpgrade");
            await hiddenSaveAs;
            check("Custom: source draft hidden by Add still blocks Save and Save As", scroll.PendingLocation == $"{doc.SelectedChartId} / Scroll 0" && !Directory.Exists(copy) && bytes.SequenceEqual(File.ReadAllBytes(path)) && w.StatusMessage.Contains(scroll.PendingLocation));
            w.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            w.ShortcutForVerification(Key.Y, KeyModifiers.Control);
            check("Custom: redo Add restores the same new event, leaving the source draft unapplied", doc.SelectedChart.ScrollSpeeds.Single(s => s.Time == Bar(1)).Key == added.Key && doc.SelectedChart.ScrollSpeeds[0].CustomExpr is null && scroll.HasDraft);
            w.ShortcutForVerification(Key.Z, KeyModifiers.Control); Scroll(Bar(0));
            check("Custom: returning to the source after undo Add restores its editable draft", doc.SelectedChart.ScrollSpeeds.Count == 2 && expr.Text == Rebound && scroll.FindControl<Button>("DiscardFormula")!.IsVisible);
            version = doc.ChangeVersion;
            Click(timing, "ApplyScrollButton");
            check("Custom: Scroll Apply commits the formula exactly once", doc.ChangeVersion == version + 1 && doc.SelectedChart.ScrollSpeeds[0].CustomExpr == Rebound && !w.FindControl<Button>("DiscardFormulaDrafts")!.IsVisible);
            w.ShortcutForVerification(Key.Z, KeyModifiers.Control);
            check("Custom: one undo restores the original scroll without a stale draft", doc.SelectedChart.ScrollSpeeds[0].CustomExpr is null && !scroll.IsVisible);
            w.ShortcutForVerification(Key.Y, KeyModifiers.Control);
            w.SelectNoteForVerification("ribbon"); Choose(pc, "Custom"); pathExpr.Text = Rebound;
            version = doc.ChangeVersion; Click(inspector, "ApplyCurveButton");
            check("Custom: actual negative width rejects Apply without discarding the entered formula", version == doc.ChangeVersion && pathExpr.Text == Rebound && ribbon.PendingLocation is not null);
            pathExpr.Text = "t^1.5"; Click(inspector, "ApplyCurveButton");
            check("Custom: Inspector stores fractional power on the head", doc.SelectedChart.Notes.Single(n => n.Id == "ribbon").CustomExpr == "t^1.5" && ribbon.PendingLocation is null);
            w.StageEventForVerification("selection", "{\"ids\":[\"knee\"],\"nodeId\":\"knee\",\"parentId\":\"ribbon\"}");
            Choose(pc, "Custom"); pathExpr.Text = Rebound; Click(inspector, "ApplyCurveButton");
            w.StageEventForVerification("selection", "{\"ids\":[\"tip\"],\"nodeId\":\"tip\",\"parentId\":\"ribbon\"}");
            check("Custom: independent node expression; tail remains disabled", doc.SelectedChart.Notes.Single(n=>n.Id=="ribbon").Nodes[0].CustomExpr == Rebound && !pc.IsEnabled && !ribbon.IsVisible);
            var cancelled = w.SavePackageForVerification(); Respond("CancelCurveUpgrade"); await cancelled;
            check("Custom: cancelling the first v2.1 upgrade preserves v2.0 bytes", doc.SourceFormatMinor == 0 && doc.HasUnsavedChanges && bytes.SequenceEqual(File.ReadAllBytes(path)));
            var accepted = w.SavePackageToForVerification(copy); Respond("ConfirmCurveUpgrade"); await accepted;
            var reopened = EditorPackageRepository.Open(copy);
            check("Custom: upgrade confirmation saves v2.1 to the copy only and formulas reopen", reopened.SourceFormatMinor == 1 && reopened.SelectedChart.ScrollSpeeds[0].CustomExpr == Rebound && reopened.SelectedChart.Notes.Single(n=>n.Id=="ribbon").CustomExpr == "t^1.5" && bytes.SequenceEqual(File.ReadAllBytes(path)));
            var next = w.SavePackageForVerification(); check("Custom: a subsequently saved v2.1 pack does not request upgrade again", !w.FindControl<Border>("WorkbenchModalLayer")!.IsVisible); await next;
            w.SelectNoteForVerification("ribbon"); pathExpr.Text = "1"; w.SelectNoteForVerification("fixture-note");
            Click(w, "DiscardFormulaDrafts"); await w.SavePackageForVerification();
            check("Custom: a hidden draft can be explicitly discarded without altering its saved formula", ribbon.PendingLocation is null && EditorPackageRepository.Open(copy).SelectedChart.Notes.Single(n=>n.Id=="ribbon").CustomExpr == "t^1.5");
            await VerifyChartIdentity(w, ribbon, pathExpr, Path.Combine(root, "blocked-save"), check);
            void Scroll(ExactBarTime time)
            {
                var list = timing.FindControl<ListBox>("ScrollList")!;
                list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, time));
            }
            void Respond(string button)
            {
                var slot = w.FindControl<ContentControl>("WorkbenchModalSlot")!;
                var sheet = slot.Content as Control ?? throw new InvalidOperationException("Missing first-save formula upgrade sheet");
                var action = sheet.GetVisualDescendants().OfType<Button>().Single(b => b.Name == button);
                action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }
        finally
        {
            w.ClosePromptForVerification = () => Task.FromResult(UnsavedCloseChoice.Discard);
            w.Close(); await w.CloseDecisionForVerification; Dispatcher.UIThread.RunJobs();
        }
    }
    private static async Task VerifyChartIdentity(MainWindow w, CustomCurveEditor panel, TextBox input,
        string blockedSave, Action<string, bool> check)
    {
        var doc = w.CurrentDocument!; var oldId = doc.SelectedChartId; var key = doc.SelectedChart.Key;
        const string renamed = "renamed", originalDraft = "t^1.75", copyDraft = "t*t";
        void SelectPoint() => w.SelectNoteForVerification("ribbon");
        void Undo() => w.ShortcutForVerification(Key.Z, KeyModifiers.Control);
        void Redo() => w.ShortcutForVerification(Key.Y, KeyModifiers.Control);
        SelectPoint(); input.Text = originalDraft;
        w.SelectNoteForVerification("fixture-note");
        doc.Execute(new RenameChartCommand(oldId, renamed, "charts/renamed.json"));
        w.SelectChartForVerification(renamed); // Normal panel refresh with no point selected.
        await w.SavePackageToForVerification(blockedSave);
        check("Custom: a hidden draft reports the renamed chart before its point is reselected and blocks writing", !Directory.Exists(blockedSave) && w.StatusMessage.Contains(renamed + " / ribbon"));
        SelectPoint(); check("Custom: renaming preserves the point's draft and discard action", input.Text == originalDraft && panel.FindControl<Button>("DiscardFormula")!.IsVisible);
        Undo(); check("Custom: undo rename restores the old label and keeps chart identity", doc.SelectedChart.Key == key && panel.PendingLocation == oldId + " / ribbon");
        Redo(); check("Custom: redo rename restores the new label on the same draft", doc.SelectedChart.Key == key && panel.PendingLocation == renamed + " / ribbon");
        w.CopyChartForVerification(); var copyKey = doc.SelectedChart.Key; var copyId = doc.SelectedChartId;
        SelectPoint(); check("Custom: copied chart has a distinct identity and starts with the committed formula only", copyKey != key && input.Text == "t^1.5" && !panel.FindControl<Button>("DiscardFormula")!.IsVisible);
        input.Text = copyDraft; Undo(); SelectPoint();
        check("Custom: undo copy restores the original chart and its own draft", doc.SelectedChart.Key == key && input.Text == originalDraft);
        Redo(); SelectPoint();
        check("Custom: redo copy restores the same copy identity and its independent draft", doc.SelectedChart.Key == copyKey && doc.SelectedChartId == copyId && input.Text == copyDraft);
        Click(panel, "DiscardFormula"); w.SelectChartForVerification(renamed); SelectPoint();
        check("Custom: discarding one chart's formula leaves the other chart's draft untouched", input.Text == originalDraft && panel.PendingLocation == renamed + " / ribbon");
        Click(panel, "DiscardFormula"); check("Custom: last draft can be discarded directly on the renamed point", !panel.HasDraft && !w.FindControl<Button>("DiscardFormulaDrafts")!.IsVisible);
    }

    private static string? Tag(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string;
    private static void Choose(ComboBox c, string tag) => c.SelectedItem = c.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, tag));
    private static void Click(Control c, string name) => c.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
