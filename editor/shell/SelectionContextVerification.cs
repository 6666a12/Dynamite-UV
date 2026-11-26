using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

internal static class SelectionContextVerification
{
    public static void Run(MainWindow window, string tap, string hold, string middle, Action<string, bool> check)
    {
        var doc = window.CurrentDocument!;
        var inspector = window.GetVisualDescendants().OfType<InspectorView>().Single();
        var edit = (MenuFlyout)window.GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, WorkbenchLocalizer.Shared["Menu.Edit"])).Flyout!;
        var rev = doc.ChangeVersion;
        void Menu(string key) => edit.Items.OfType<MenuItem>().Single(i => Equals(i.Header, WorkbenchLocalizer.Shared[key]))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        void Stage(string[] ids, string primary, string? node = null) => window.StageEventForVerification("selection",
            JsonSerializer.Serialize(new { ids, parentId = primary, nodeId = node }));
        void Press(Key key, RawInputModifiers mods, PhysicalKey physical)
        {
            window.KeyPress(key, mods, physical, null); window.KeyRelease(key, mods, physical, null);
            Dispatcher.UIThread.RunJobs();
        }
        var text = WorkbenchLocalizer.Shared;
        T C<T>(string name) where T : Control => inspector.FindControl<T>(name)!;
        string? Label(string name) => C<TextBlock>(name).Text;
        bool Visible(string name) => C<Control>(name).IsVisible;
        bool SpeedVisible() => Visible("SpeedPanel");
        EditableNote Read(string id) => doc.SelectedChart.Notes.Single(n => n.Id == id);
        byte[] Bytes() => V2JsonEncoder.EncodeChart(doc.BuildChartSnapshot(doc.SelectedChartId));
        var before = Bytes();
        var noteId = C<TextBox>("NoteIdValue"); var nodeId = C<TextBox>("NodeIdValue");
        var nudge = C<Expander>("ExistingBatchTools");
        Stage([tap, hold], hold);
        check("C1: a note batch shows its count and batch tools without single-object fields",
            Label("NoteTitle") == text.Format("Inspector.Notes", 2) && Visible("BatchInspector") &&
            !Visible("SinglePanel") && !Visible("IdentityPanel") && !Visible("SinglePathPanel"));
        check("C2: speed is available for a note batch with nudge collapsed", SpeedVisible() && nudge.IsVisible && !nudge.IsExpanded);
        Stage([tap], hold); // Defensive check: a stale parent hint cannot override the final set.
        check("A3: removing the primary leaves only the surviving parent in Inspector", window.SelectedNoteId == tap &&
            window.SelectedNoteIds.SequenceEqual([tap]) && noteId.Text == tap);
        check("C1: a Tap shows type, track, copyable ID, geometry and type/track editing, no path controls",
            Label("NoteTitle") == text["Inspector.Tap"] && Label("NoteMeta") == text["Inspector.CenterStage"] &&
            Label("NoteIdLabel") == text["Inspector.NoteId"] && noteId.IsReadOnly && Visible("IdentityPanel") &&
            !Visible("NodeIdentity") && Visible("SinglePanel") && Visible("KindPanel") && !Visible("SinglePathPanel") &&
            C<Button>("ApplyGeometryButton").IsEnabled && C<Button>("ApplyNoteKindButton").IsEnabled);
        var tapNote = Read(tap); var oldHoldWidth = Read(hold).Width; var newWidth = tapNote.Width + .25;
        window.ApplyGeometryForVerification(ExactBarTimeText.Format(tapNote.Time), tapNote.Center, newWidth);
        check("A3: geometry writes to the surviving note, not the removed note", Read(tap).Width == newWidth && Read(hold).Width == oldHoldWidth);
        Menu("Edit.Undo");
        Stage([hold], hold);
        check("C1: a Hold head retains its outgoing curve and type/track editor, not a node judgement control",
            Label("NoteTitle") == text["Inspector.Hold"] && Visible("KindPanel") && Visible("CurvePanel") && !Visible("HoldJudgePanel"));
        Stage([middle], hold, middle);
        check("A3: real node retains parent for copy and its own ID for editing", doc.Selection.Id == middle &&
            doc.Selection.Kind == EditorSelectionKind.PathNode && window.SelectedNoteId == hold && nodeId.Text == middle);
        check("C1: a middle Hold node identifies its parent and node and exposes real curve and judgement editors",
            Label("NoteTitle") == text["Inspector.PathNode"] && Label("NoteMeta") == $"{text["Inspector.Hold"]} · {text["Inspector.CenterStage"]}" &&
            Label("NoteIdLabel") == text["Inspector.ParentNoteId"] && noteId.Text == hold && Visible("NodeIdentity") && nodeId.IsReadOnly &&
            Visible("CurvePanel") && C<Button>("ApplyCurveButton").IsEnabled && Visible("HoldJudgePanel") &&
            C<Button>("ApplyHoldJudgeButton").IsEnabled && !Visible("TailCurveState") && !Visible("HoldTailJudgeState") &&
            Label("SpatialWarnings")!.Contains(text["Risk.Sampled"], StringComparison.Ordinal));
        check("C2: path node exposes parent speed and geometry but not type or track edits", SpeedVisible() && Visible("ParentSpeedHint") &&
            C<Button>("ApplyGeometryButton").IsEnabled && !Visible("KindPanel") && !C<Button>("ApplyNoteKindButton").IsEnabled);

        var choice = C<ComboBox>("CurveChoice");
        var formula = inspector.Formula;
        var input = formula.FindControl<TextBox>("ExpressionInput")!;
        const string draft = "t+4*t*(1-t)";
        var savedNodeCurve = Read(hold).Nodes.Single(n => n.Id == middle).CurveToNext;
        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "Custom"));
        input.Text = draft;
        var tail = Read(hold).Nodes[^1];
        Stage([tail.Id], hold, tail.Id);
        check("C1: a Hold tail shows read-only curve and judgement states but keeps geometry, speed and nudge",
            Label("NoteTitle") == text["Inspector.TailNode"] && nodeId.Text == tail.Id && noteId.Text == hold &&
            Visible("TailCurveState") && Visible("HoldTailJudgeState") && !Visible("CurvePanel") && !Visible("HoldJudgePanel") &&
            !C<Button>("ApplyCurveButton").IsEnabled && !C<Button>("ApplyHoldJudgeButton").IsEnabled &&
            C<Button>("ApplyGeometryButton").IsEnabled && nudge.IsVisible && nudge.IsExpanded && SpeedVisible() && Visible("ParentSpeedHint"));
        var tailVersion = doc.ChangeVersion;
        window.ApplyCurveForVerification(V2PathCurve.Hold);
        window.ApplyHoldJudgeForVerification(false);
        check("C1: stale tail curve and judgement apply requests are rejected before touching the chart", doc.ChangeVersion == tailVersion);
        var tailCenter = tail.Center;
        window.NudgeSelectionForVerification(0, 1);
        check("C1: tail nudge edits the selected node, not the parent", Math.Abs(Read(hold).Nodes[^1].Center - tailCenter - .1) < 1e-10 &&
            doc.Selection.Id == tail.Id);
        Menu("Edit.Undo");
        Stage([], hold);
        var locator = C<ComboBox>("ObjectChoice");
        check("C1: empty selection keeps only its heading and locator on a chart without flagged risks",
            Label("NoteTitle") == text["Inspector.NoSelection"] && Visible("LocatePanel") && locator.IsEnabled &&
            !Visible("IdentityPanel") && !Visible("SinglePanel") && !Visible("BatchInspector") && !nudge.IsVisible &&
            !Visible("RiskSection") && !Visible("SpatialWarnings"));
        check("C1: leaving a path editor hides but does not commit or discard its custom draft",
            !formula.IsVisible && formula.HasDraft && Read(hold).Nodes.Single(n => n.Id == middle).CurveToNext == savedNodeCurve);
        locator.SelectedItem = locator.Items.Single(i => i!.ToString()!.TrimStart().StartsWith($"{middle} ·", StringComparison.Ordinal));
        check("C1: locating a path node from the empty state restores the correct draft and overshoot notice",
            doc.Selection.Kind == EditorSelectionKind.PathNode && doc.Selection.Id == middle &&
            window.SelectedNoteId == hold && input.Text == draft && formula.IsVisible && formula.FindControl<TextBlock>("FormulaNotice")!.IsVisible);
        formula.FindControl<Button>("DiscardFormula")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Menu("Edit.CopyNotes");
        Menu("Edit.Delete");
        check("A3: Edit/Delete removes the selected node, not its parent", Read(hold).Nodes.All(n => n.Id != middle));
        Menu("Edit.Undo");
        check("A3: menu undo restores the authored node", Read(hold).Nodes.Any(n => n.Id == middle));
        Stage([], hold);
        check("A3: last deselection clears Inspector and global note commands", doc.Selection.Kind == EditorSelectionKind.None &&
            window.SelectedNoteIds.Count == 0 && window.SelectedNoteId is null &&
            !window.ShortcutForVerification(Key.Delete) && !window.ShortcutForVerification(Key.C, KeyModifiers.Control));
        check("C2: the standalone speed group stays hidden with no note selected", !SpeedVisible());

        Stage([tap], tap);
        window.ApplyGeometryForVerification(ExactBarTimeText.Format(tapNote.Time), 2.5, 5.5);
        Stage([], tap);
        check("C1: empty selection reports only the actual chart-wide advisory, without selected-object text or zero categories",
            Visible("RiskSection") && Visible("ChartRiskSummary") && !Visible("SpatialWarnings") &&
            Label("ChartRiskSummary") == text.Format("Risk.Chart", text.Format("Risk.ChartOverflow", 1)));
        Menu("Edit.Undo");

        var timing = window.Timing;
        // Create a deletable non-base Scroll row via its real panel. Undo below leaves caller's fixture unchanged.
        var scroll = timing.FindControl<ListBox>("ScrollList")!;
        bool newBase = doc.SelectedChart.ScrollSpeeds.Count == 0;
        void AddScroll(string time) {
            timing.FindControl<TextBox>("ScrollTimeValue")!.Text = time;
            timing.FindControl<NumericUpDown>("ScrollValueInput")!.Value = 1;
            timing.FindControl<Button>("AddScrollButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        if (newBase) AddScroll("0");
        var bar = doc.SelectedChart.ScrollSpeeds.Max(s => s.Time) + ExactBarTime.One;
        AddScroll(ExactBarTimeText.Format(bar));
        foreach (var (listName, kind) in new[] { ("BpmList", EditorSelectionKind.Bpm), ("ScrollList", EditorSelectionKind.Scroll) })
        {
            Stage([tap], tap);
            var list = timing.FindControl<ListBox>(listName)!;
            list.SelectedItem = list.Items.OfType<ListBoxItem>().Last();
            var version = doc.ChangeVersion;
            var old = Bytes();
            check($"A4: {kind} row clears note focus and disables Inspector", doc.Selection.Kind == kind && window.SelectedNoteId is null &&
                window.SelectedNoteIds.Count == 0 && !inspector.FindControl<Button>("ApplyGeometryButton")!.IsEnabled);
            Menu("Edit.Delete"); Menu("Edit.CopyNotes"); window.ApplyGeometryForVerification("1", 2, 3);
            check($"A4: {kind} ignores note menu/shortcut Delete, copy and stale geometry", !window.ShortcutForVerification(Key.Delete) &&
                !window.ShortcutForVerification(Key.C, KeyModifiers.Control) && doc.ChangeVersion == version && Bytes().SequenceEqual(old));
        }
        // Last successful copy was the path node, which means its complete parent. No blocked copy overwrote it.
        var oldCount = doc.SelectedChart.Notes.Count; Menu("Edit.PasteNotes");
        check("A4: blocked copy did not replace clipboard; pasted whole parent path", doc.SelectedChart.Notes.Count == oldCount + 1 &&
            doc.SelectedChart.Notes.Count(n => n.Type == V2NoteType.Hold) == 2);
        Menu("Edit.Undo");
        Stage([tap], tap);
        var field = inspector.FindControl<TextBox>("BarTimeValue")!;
        window.UpdateLayout(); field.Focus(); field.Text = "abc"; field.SelectionStart = 0; field.SelectionEnd = 3;
        Press(Key.C, RawInputModifiers.Control, PhysicalKey.C); Press(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete);
        check("A4: text-input copy/delete stay in the focused editor", field.IsFocused && field.Text == "" && doc.SelectedChart.Notes.Count == oldCount);
        scroll.SelectedItem = scroll.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, bar));
        timing.FindControl<Button>("DeleteScrollButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check("A4: panel Delete targets Scroll; edit-menu Undo restores only the event", doc.SelectedChart.ScrollSpeeds.All(s => s.Time != bar));
        Menu("Edit.Undo");
        check("A4: undo of event deletion restores Scroll context, not the old note", doc.SelectedChart.ScrollSpeeds.Any(s => s.Time == bar) &&
            window.SelectedNoteId is null && doc.Selection.Kind == EditorSelectionKind.Scroll);
        Menu("Edit.Undo"); // original Add Scroll
        if (newBase) Menu("Edit.Undo");
        check("Selection regression leaves all original chart data intact", Bytes().SequenceEqual(before) && doc.ChangeVersion > rev);
    }
}
