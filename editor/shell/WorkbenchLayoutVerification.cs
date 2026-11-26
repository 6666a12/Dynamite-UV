using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

internal static class WorkbenchLayoutVerification
{
    private const string PreviewDirectory = "DYNA_WORKBENCH_LAYOUT_PREVIEW";

    internal static bool PreviewRequested => Environment.GetEnvironmentVariable(PreviewDirectory) is { Length: > 0 };

    internal static void Run(EditorDocument document, Action<string, bool> check)
    {
        var tools = new ToolsView();
        var modes = tools.FindControl<UniformGrid>("ToolModes")!;
        var buttons = modes.Children.OfType<Button>().ToArray();
        var text = WorkbenchLocalizer.Shared;
        var language = text.CurrentLanguage;
        var host = new Window { Width = 220, Height = 760, Content = tools };
        try
        {
            host.Show();
            foreach (var lang in new[] { "en", "zh-CN" })
            {
                text.SetLanguage(lang);
                host.Width = 220;
                Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
                check($"Workbench tools form an aligned 2×2 grid ({lang})", modes.Columns == 2 && buttons.Length == 4 &&
                    buttons[0].Bounds.Top == buttons[1].Bounds.Top && buttons[2].Bounds.Top == buttons[3].Bounds.Top &&
                    buttons[0].Bounds.Left == buttons[2].Bounds.Left && buttons[1].Bounds.Left == buttons[3].Bounds.Left &&
                    buttons.All(b => b.Bounds.Width == buttons[0].Bounds.Width && b.Bounds.Height == 32));
                host.Width = 174;
                Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
                check($"Narrow workbench tools remain fully labeled in one column ({lang})", modes.Columns == 1 &&
                    buttons.Select(b => b.Bounds.Top).Distinct().Count() == 4);
            }
            tools.SelectTool("PATH");
            check("Exactly one authoring tool has the selected appearance", buttons.Count(b => b.Classes.Contains("current-mode")) == 1 &&
                buttons.Single(b => b.Classes.Contains("current-mode")).Tag?.ToString() == "PATH");
            buttons[0].Focus(NavigationMethod.Tab);
            var focusRing = buttons[0].GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_WorkbenchFocus");
            check("Keyboard focus visibly outlines an unselected tool", focusRing.BorderBrush is not null &&
                !buttons[0].Classes.Contains("current-mode"));
            if (!PreviewRequested) return;

            var inspector = new InspectorView();
            var note = document.SelectedChart.Notes.First();
            inspector.Select(document, [note.Id], note.Id);
            var timing = new TimingView();
            timing.SetDocument(document);
            timing.SetCurrentBar(2, ExactBarTime.FromFraction(2, 1));
            timing.FindControl<ListBox>("BpmList")!.SelectedIndex = 0;
            tools.SelectTool("SELECT");
            host.Content = null;
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("224,284,304"), ColumnSpacing = 10,
                Background = SolidColorBrush.Parse("#080C18") };
            foreach (var (panel, column) in new (Control, int)[] { (tools, 0), (inspector, 1), (timing, 2) })
            {
                var section = new Border { Background = SolidColorBrush.Parse("#0E1528"), Child = panel,
                    BorderThickness = new Thickness(1), BorderBrush = SolidColorBrush.Parse("#2A3560") };
                Grid.SetColumn(section, column);
                columns.Children.Add(section);
            }
            host.Width = 832;
            host.Height = 876;
            host.Content = columns;
            buttons[0].Focus(NavigationMethod.Pointer);
            Capture(host, columns, "workbench-layout.png");
        }
        finally
        {
            host.Close();
            text.SetLanguage(language);
        }
    }

    internal static void Capture(Window host, Control target, string fileName)
    {
        if (Environment.GetEnvironmentVariable(PreviewDirectory) is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(target.Bounds.Width * 1.5), (int)Math.Ceiling(target.Bounds.Height * 1.5)), new Vector(144, 144));
        bitmap.Render(target);
        bitmap.Save(Path.Combine(directory, fileName), PngBitmapEncoderOptions.Default);
    }
}
