using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

/// <summary>The A/B switch affects only the new menu material and receipt traces.
/// It must not change normal hover, card resize, scroll, GUI hit tests or scene transitions.</summary>
internal static class MenuContactFeedback
{
    private static readonly bool Suppressed = Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-contact-fx") >= 0;
    internal static UiMotionMode Mode => Suppressed ? UiMotionMode.Off : GameSession.Settings.MotionMode;
}
