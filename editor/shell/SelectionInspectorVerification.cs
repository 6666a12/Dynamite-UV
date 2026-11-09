using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

internal static class SelectionInspectorVerification
{
    public static void Run(MainWindow window, Action<string, bool> check)
    {
        var doc = window.CurrentDocument!;
        DynamiteUniverse.ChartEditor.Core.EditableNote Note(string id) => doc.SelectedChart.Notes.Single(n => n.Id == id);
        var ids = doc.SelectedChart.Notes.Select(n => n.Id).ToArray();
        window.SelectNotesForVerification(ids);
        var panel = window.GetVisualDescendants().OfType<SelectionInspectorView>().Single();
        T C<T>(string name) where T : Control => panel.FindControl<T>(name)!;
        void Click(string name)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            C<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        var old = ids.Select(id => Note(id).Time).ToArray();
        check("Multi Inspector visible and quiet", panel.IsVisible && !C<Button>("ApplyPropertiesButton").IsEnabled && !C<Button>("ApplyTransformButton").IsEnabled);
        C<TextBox>("TimeInput").Text = "+1/3";
        Click("ApplyTransformButton");
        check("Multi Inspector applies exact transform", ids.Select((id, i) => Note(id).Time == old[i] + ExactBarTime.FromFraction(1, 3)).All(v => v) && window.SelectedNoteIds.Count == ids.Length);
        window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
        C<TextBox>("TimeInput").Text = "-100";
        Click("ApplyTransformButton");
        check("Invalid batch retains draft and reports error", C<TextBlock>("TransformError").IsVisible && C<TextBox>("TimeInput").Text == "-100" && Note(ids[0]).Time == old[0]);
        Click("ResetTransformButton");
        var width = Note(ids[0]).Width;
        C<TextBox>("WidthInput").Text = "1.25";
        Click("ApplyPropertiesButton");
        check("Multi Inspector changes width only", ids.All(id => Note(id).Width == 1.25) && Note(ids[0]).Time == old[0]);
        window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
        check("Multi property edit is one undo", Note(ids[0]).Width == width);
        C<TextBox>("WidthInput").Text = "7";
        window.SelectNoteForVerification(ids[0]);
        window.SelectNotesForVerification(ids);
        check("Selection change discards old drafts", C<TextBox>("WidthInput").Text != "7" && !C<Button>("ApplyPropertiesButton").IsEnabled);
        var secondWidth = Note(ids[1]).Width;
        Note(ids[1]).Width = secondWidth + .5;
        window.SelectNotesForVerification(ids);
        check("Mixed widths show empty watermark", C<TextBox>("WidthInput").Text == "" && !C<Button>("ApplyPropertiesButton").IsEnabled);
        var oldTrack = Note(ids[0]).Track;
        C<ComboBox>("BatchTrack").SelectedIndex = ((int)oldTrack + 1) % 3 + 1;
        Click("ApplyPropertiesButton");
        check("Track edit leaves mixed widths untouched", Note(ids[0]).Width == width && Note(ids[1]).Width == secondWidth + .5);
        window.ShortcutForVerification(Key.Z, KeyModifiers.Control);
        Note(ids[1]).Width = secondWidth;
        window.SelectNotesForVerification(ids);
    }
}
