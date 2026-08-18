using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DynamiteUniverse.ChartEditor.Localization;
using DynamiteUniverse.ChartEditor.Settings;

namespace DynamiteUniverse.ChartEditor;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        EditorLocalization.Current.SetLanguage(EditorPreferences.Current.Language);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
