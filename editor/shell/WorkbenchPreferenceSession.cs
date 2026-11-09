using Avalonia.Controls;
using Avalonia.Threading;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Owns preference snapshots; no document writes, selection or playback restoration.</summary>
internal sealed class WorkbenchPreferenceSession
{
    private readonly WorkbenchUserStore _store;
    private readonly Func<WorkbenchPreferences> _capture;
    private readonly Action<string> _report;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    public WorkbenchPreferences Initial { get; }
    public bool Enabled { get; set; }
    public WorkbenchPreferenceSession(WorkbenchUserStore store, Func<WorkbenchPreferences> capture, Action<string> report)
    {
        _store = store; _capture = capture; _report = report;
        var loaded = store.Load<WorkbenchPreferences>(WorkbenchPreferences.FileName);
        if (loaded is not null && !loaded.IsValid)
            store.Protect(WorkbenchPreferences.FileName, "Unsupported version or invalid values.");
        Initial = loaded is { IsValid: true } ? loaded : new();
        _timer.Tick += (_, _) => Flush();
    }
    public void Changed()
    {
        if (!Enabled) return;
        _timer.Stop(); _timer.Start();
    }
    public void Flush()
    {
        _timer.Stop();
        if (Enabled && !_store.Save(WorkbenchPreferences.FileName, _capture()))
            _report(_store.Warning ?? "Preferences could not be saved.");
    }
}
