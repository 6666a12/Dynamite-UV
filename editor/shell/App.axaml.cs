using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DynamiteUniverse.Editor.Shell;

public sealed class App : Application
{
    internal static bool VerificationOwnsWindow { get; set; }
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Resources["OfficialFont"] = ShellTypography.OfficialFont;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (!VerificationOwnsWindow && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
