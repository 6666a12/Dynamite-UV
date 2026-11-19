using Avalonia.Input;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Tracks physical key gestures for the workbench. Guide display is session-local;
/// only offset steps go through the selected chart's undo history.</summary>
internal sealed class WorkbenchTimingKeys(TimingView timing, Action<object> send, Action<string> report, IWorkbenchLocalizer text)
{
    private readonly HashSet<Key> _guideDown = [];
    private readonly Dictionary<Key, int> _guides = new() { [Key.Left] = 0, [Key.Down] = 0, [Key.Right] = 0 };
    private Key? _offsetKey;

    public int GuideLevel(Key key) => _guides[key];
    public void Sync() => send(new { command = "barGuides", left = _guides[Key.Left], center = _guides[Key.Down], right = _guides[Key.Right] });
    public static bool IsTimingKey(Key key) => key is Key.O or Key.P or Key.Left or Key.Down or Key.Right;

    public bool Press(Key key, KeyModifiers mods, bool native, bool echo, bool busy)
    {
        if (!IsTimingKey(key) || mods is not (KeyModifiers.None or KeyModifiers.Shift)) return false;
        if (busy) { ResetHeld(); return true; }
        if (key is Key.O or Key.P)
        {
            if (native && echo && _offsetKey != key) return true;
            // A native initial press is distinct even if focus changed and its prior release was lost.
            if (_offsetKey != key || (native && !echo)) timing.EndOffsetNudge();
            _offsetKey = key;
            timing.NudgeOffset(key == Key.O ? 1 : -1, mods == KeyModifiers.Shift);
            return true;
        }
        if (mods != KeyModifiers.None) return false;
        if (native ? !echo : _guideDown.Add(key))
        {
            var level = _guides[key] = (_guides[key] + 1) % 3;
            Sync();
            var side = key == Key.Left ? "Left" : key == Key.Right ? "Right" : "Center";
            report(text.Format("Timing.GuideStatus", text["Context." + side], text[level switch
                { 1 => "Timing.GuideDim", 2 => "Timing.GuideFull", _ => "Context.Off" }]));
        }
        return true;
    }
    public void Release(Key key)
    {
        _guideDown.Remove(key);
        if (_offsetKey != key) return;
        _offsetKey = null; timing.EndOffsetNudge();
    }
    public void ResetHeld()
    {
        _guideDown.Clear(); _offsetKey = null; timing.EndOffsetNudge();
    }
}
