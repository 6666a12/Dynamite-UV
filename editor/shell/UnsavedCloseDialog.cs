using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DynamiteUniverse.Editor.Shell;

internal enum UnsavedCloseChoice { Cancel, Save, Discard }

/// <summary>Owns the close prompt only; saving and document identity remain with the window/session.</summary>
internal static class UnsavedCloseDialog
{
    internal static Task<UnsavedCloseChoice> Show(Window owner, IWorkbenchLocalizer text)
    {
        var dialog = new Window
        {
            Title = text["Close.Title"], Width = 440, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (key, choice) in new[] { ("File.Save", UnsavedCloseChoice.Save),
                     ("Close.Discard", UnsavedCloseChoice.Discard), ("Create.Cancel", UnsavedCloseChoice.Cancel) })
        {
            var button = new Button { Content = text[key] };
            button.Click += (_, _) => dialog.Close(choice);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 24,
            Children = { new TextBlock { Text = text["Close.Message"], TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons },
        };
        return dialog.ShowDialog<UnsavedCloseChoice>(owner);
    }
}
