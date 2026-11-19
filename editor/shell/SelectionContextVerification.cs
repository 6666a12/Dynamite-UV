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
        string Title() => inspector.FindControl<TextBlock>("NoteTitle")!.Text!;
        EditableNote Read(string id) => doc.SelectedChart.Notes.Single(n => n.Id == id);
        byte[] Bytes() => V2JsonEncoder.EncodeChart(doc.BuildChartSnapshot(doc.SelectedChartId));
        var before = Bytes();
        Stage([tap, hold], hold);
        Stage([tap], hold); // Defensive check: a stale parent hint cannot override the final set.
        check("A3: removing the primary leaves only the surviving parent in Inspector", window.SelectedNoteId == tap &&
            window.SelectedNoteIds.SequenceEqual([tap]) && Title() == tap);
        var tapNote = Read(tap); var oldHoldWidth = Read(hold).Width; var newWidth = tapNote.Width + .25;
        window.ApplyGeometryForVerification(ExactBarTimeText.Format(tapNote.Time), tapNote.Center, newWidth);
        check("A3: geometry writes to the surviving note, not the removed note", Read(tap).Width == newWidth && Read(hold).Width == oldHoldWidth);
        Menu("Edit.Undo");
        Stage([middle], hold, middle);
        check("A3: real node retains parent for copy and its own ID for editing", doc.Selection.Id == middle &&
            doc.Selection.Kind == EditorSelectionKind.PathNode && window.SelectedNoteId == hold && Title() == middle);
        Menu("Edit.CopyNotes");
        Menu("Edit.Delete");
        check("A3: Edit/Delete removes the selected node, not its parent", Read(hold).Nodes.All(n => n.Id != middle));
        Menu("Edit.Undo");
        check("A3: menu undo restores the authored node", Read(hold).Nodes.Any(n => n.Id == middle));
        Stage([], hold);
        check("A3: last deselection clears Inspector and global note commands", doc.Selection.Kind == EditorSelectionKind.None &&
            window.SelectedNoteIds.Count == 0 && window.SelectedNoteId is null &&
            !window.ShortcutForVerification(Key.Delete) && !window.ShortcutForVerification(Key.C, KeyModifiers.Control));

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
