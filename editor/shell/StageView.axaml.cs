using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DynamiteUniverse.Editor.Shell;

public sealed partial class StageView : UserControl
{
    public event Action? FloatRequested;
    public event Action? DockRequested;
    // Hidden internal mode reserved for capture tooling; no user-facing toggle or persistence.
    internal event Action<bool>? GamePreviewChanged;
    internal bool GamePreviewEnabled { get; private set; }
    internal void SetGamePreviewForVerification(bool enabled)
    {
        GamePreviewEnabled = enabled;
        GamePreviewChanged?.Invoke(enabled);
    }
    public event Action? PlayRequested;
    public NativeStageHost Host { get; }

    public StageView()
    {
        AvaloniaXamlLoader.Load(this);
        Host = this.FindControl<NativeStageHost>("StageHost")!;
    }

    public void SetPlaying(bool playing) =>
        this.FindControl<Ursa.Controls.IconButton>("PlayButton")!.Content = WorkbenchLocalizer.Shared[playing ? "Transport.Pause" : "Transport.Play"];

    public void SetLoading(string value) => this.FindControl<TextBlock>("LoadingText")!.Text = value;

    private void PlayClicked(object? sender, RoutedEventArgs e) => PlayRequested?.Invoke();
    private void FloatClicked(object? sender, RoutedEventArgs e) => FloatRequested?.Invoke();
    private void DockClicked(object? sender, RoutedEventArgs e) => DockRequested?.Invoke();
}
