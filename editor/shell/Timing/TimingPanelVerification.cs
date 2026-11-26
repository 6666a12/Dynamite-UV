using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

internal static class TimingPanelVerification
{
    internal static void Run(string source, Action<string, bool> check)
    {
        var document = new EditorDocumentSession().Open(source);
        var fractional = ExactBarTime.FromFraction(7, 3);
        document.Execute(new AddBpmCommand(fractional, 172.125));
        for (var bar = 3; bar < 18; ++bar)
            document.Execute(new AddBpmCommand(ExactBarTime.FromFraction(bar, 1), 148 + bar));
        document.Execute(new AddScrollCommand(ExactBarTime.Zero, 1));
        document.Execute(new AddScrollCommand(fractional, 1.25));
        document.Execute(new AddScrollCommand(ExactBarTime.FromFraction(4, 1), .85));
        document.Select(EditorSelection.None);
        var panel = new TimingView();
        var localizer = WorkbenchLocalizer.Shared;
        var language = localizer.CurrentLanguage;
        var host = new Window { Width = 304, Height = 462, Background = SolidColorBrush.Parse("#0E1528"), Content = panel };
        T C<T>(string name) where T : Control => panel.FindControl<T>(name)!;
        void Click(Control control)
        {
            var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), host)!.Value;
            host.MouseDown(center, MouseButton.Left); host.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
        }
        void Submit(string name) => C<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var changedSelections = 0;
        panel.RowSelectionChanged += selection => { document.Select(selection); changedSelections++; };
        panel.CommandRequested = command =>
        {
            try { document.Execute(command); panel.SetDocument(document); return null; }
            catch (Exception ex) { return ex.Message; }
        };
        try
        {
            host.Show();
            panel.SetDocument(document);
            panel.SetCurrentBar(fractional.ToDouble(), fractional);
            Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
            var tabs = C<TabStrip>("EventTabs");
            var bpmTab = (TabStripItem)tabs.Items[0]!;
            var scrollTab = (TabStripItem)tabs.Items[1]!;
            var bpmList = C<ListBox>("BpmList");
            var scrollList = C<ListBox>("ScrollList");
            var row = bpmList.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, fractional));
            check("Timing table retains exact fraction and unrounded value in distinct columns",
                row.Content is TimingEventRow { Bar: "2+1/3", Value: "172.125" });
            bpmList.SelectedItem = row;
            var bpmTime = C<TextBox>("BpmTimeValue");
            bpmTime.Text = "2+2/3";
            var innerList = bpmList.GetVisualDescendants().OfType<ScrollViewer>().First();
            innerList.Offset = new(0, 59);
            Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
            var listOffset = innerList.Offset;
            var bpmNumbers = C<Grid>("BpmColumns").Children.OfType<TextBlock>().Last();
            var visibleRowNumber = bpmList.GetVisualDescendants().OfType<TextBlock>().First(b => b.Text == "172.125");
            check("Timing numeric table headers stay aligned when the list scrolls",
                bpmNumbers.TranslatePoint(new Point(bpmNumbers.Bounds.Width, 0), host) is { } columnRight &&
                visibleRowNumber.TranslatePoint(new Point(visibleRowNumber.Bounds.Width, 0), host) is { } rowRight &&
                Math.Abs(columnRight.X - rowRight.X) < 1.1);
            var add = C<Button>("AddBpmButton");
            var addPosition = add.TranslatePoint(default, host);
            var bpmPage = C<ScrollViewer>("BpmPage");
            bpmPage.Offset = new(0, 40);
            Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
            check("Add remains next to the list heading when the BPM editor scrolls", bpmPage.Offset.Y > 0 &&
                add.TranslatePoint(default, host) == addPosition);
            bpmPage.Offset = default;
            var offsetControl = C<TextBox>("OffsetMsInput");
            var offsetPosition = offsetControl.TranslatePoint(default, host);
            var version = document.ChangeVersion;
            var selections = changedSelections;
            Click(scrollTab);
            check("Clicking Scroll switches the page without moving audio offset or selecting a different event",
                !C<ScrollViewer>("BpmPage").IsVisible && C<ScrollViewer>("ScrollPage").IsVisible &&
                offsetControl.TranslatePoint(default, host) == offsetPosition && document.Selection.Kind == EditorSelectionKind.Bpm &&
                document.ChangeVersion == version && changedSelections == selections);
            Click(bpmTab);
            check("Returning to BPM retains its event, typed time and list scroll position", bpmList.SelectedItem == row &&
                bpmTime.Text == "2+2/3" && listOffset.Y > 0 && Math.Abs(innerList.Offset.Y - listOffset.Y) < 1);

            bpmTime.Text = "not a bar";
            Submit("AddBpmButton");
            check("Invalid BPM time highlights and keeps that field; other areas stay quiet", bpmTime.Classes.Contains("invalid") &&
                bpmTime.Text == "not a bar" && C<TextBlock>("BpmError").IsVisible &&
                !C<TextBlock>("OffsetError").IsVisible && !C<TextBlock>("ScrollError").IsVisible && document.ChangeVersion == version);
            offsetControl.Text = "bad offset";
            Submit("OffsetApplyButton");
            check("Offset errors are next to the persistent field", offsetControl.Classes.Contains("invalid") &&
                C<TextBlock>("OffsetError").IsVisible && document.ChangeVersion == version);
            offsetControl.Text = "0";
            Submit("OffsetApplyButton");
            check("Correcting offset clears only its own error marker", !offsetControl.Classes.Contains("invalid") &&
                !C<TextBlock>("OffsetError").IsVisible && C<TextBlock>("BpmError").IsVisible);

            bpmTime.Text = "0";
            Submit("ApplyBpmButton");
            check("Rejected BPM commands also appear at the editing area", C<TextBlock>("BpmError").IsVisible &&
                !bpmTime.Classes.Contains("invalid") && document.ChangeVersion == version);

            Click(scrollTab);
            scrollList.SelectedIndex = 0;
            var choice = C<ComboBox>("ScrollCurveChoice");
            choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "Custom"));
            var expression = panel.Formula.FindControl<TextBox>("ExpressionInput")!;
            expression.Text = "t ^ 2";
            var formulaVersion = panel.Formula.Revision;
            selections = changedSelections;
            Click(bpmTab);
            check("A Scroll expression keeps blocking save while its tab is hidden", panel.Formula.HasDraft &&
                panel.Formula.Revision == formulaVersion && scrollList.SelectedIndex == 0 && document.Selection.Kind == EditorSelectionKind.Scroll);
            Click(scrollTab);
            check("Returning to Scroll restores the pending expression without changing the document", panel.Formula.HasDraft &&
                expression.Text == "t ^ 2" && panel.Formula.Revision == formulaVersion &&
                changedSelections == selections && document.ChangeVersion == version);
            panel.Formula.DiscardAll();
            scrollList.SelectedIndex = scrollList.ItemCount - 1;
            localizer.SetLanguage("zh-CN");
            check("Scroll table localizes the visible curve label without changing the selected event",
                scrollList.Items.OfType<ListBoxItem>().First().Content is TimingEventRow { Curve: "线性" } &&
                scrollList.SelectedIndex == scrollList.ItemCount - 1);
            check("Scroll tail still has no outgoing curve and its table cell is marked with a dash",
                !choice.IsEnabled && scrollList.SelectedItem is ListBoxItem { Content: TimingEventRow { Curve: "—" } });

            if (WorkbenchLayoutVerification.PreviewRequested)
            {
                localizer.SetLanguage("zh-CN");
                scrollList.SelectedIndex = 1;
                host.Height = 600;
                C<ScrollViewer>("ScrollPage").Offset = default;
                WorkbenchLayoutVerification.Capture(host, host, "timing-scroll.png");
            }
        }
        finally { host.Close(); localizer.SetLanguage(language); }
    }
}
