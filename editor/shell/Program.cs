using Avalonia;

namespace DynamiteUniverse.Editor.Shell;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--headless")) return Verification.RunHeadless();
        if (args.Contains("--smoke-empty"))
            return DesktopVerification.Run(args.ElementAtOrDefault(args.ToList().IndexOf("--smoke-empty") + 1) ??
                "builds/editor-shell-empty-smoke", true);
        if (args.Contains("--smoke"))
            return DesktopVerification.Run(args.ElementAtOrDefault(args.ToList().IndexOf("--smoke") + 1) ?? "builds/editor-shell-smoke");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new Win32PlatformOptions { CompositionMode = new[] { Win32CompositionMode.RedirectionSurface } })
        .LogToTrace();
}
