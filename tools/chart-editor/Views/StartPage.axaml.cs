using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace DuxCommunity.ChartEditor.Views;

public sealed partial class StartPage : UserControl
{
    public StartPage()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var scale = Math.Min(1.0, Math.Min(Bounds.Width / 1180.0, Bounds.Height / 720.0));
            RootGrid.RenderTransformOrigin = new Avalonia.RelativePoint(0.5, 0.5,
                Avalonia.RelativeUnit.Relative);
            RootGrid.RenderTransform = new ScaleTransform(scale, scale);
        };
    }

    public event EventHandler? NewProjectRequested;
    public event EventHandler? OpenProjectRequested;

    private void NewProjectClicked(object? sender, RoutedEventArgs e) =>
        NewProjectRequested?.Invoke(this, EventArgs.Empty);

    private void OpenProjectClicked(object? sender, RoutedEventArgs e) =>
        OpenProjectRequested?.Invoke(this, EventArgs.Empty);
}
