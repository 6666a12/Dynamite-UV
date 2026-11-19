using System.Text.Json;
using Avalonia.Controls;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>The preview stage owns time. Shell displays the last authoritative pair, or a
/// locally selected seek while its reply is in flight. There is no second advancing clock.</summary>
internal sealed class WorkbenchPreviewClock(TimelineView timeline, TimingView timing, TextBlock readout, IWorkbenchLocalizer text)
{
    private long _request;
    private bool? _intendedPlayback;
    internal double Second { get; private set; }
    internal double Bar { get; private set; }
    internal bool Playing { get; private set; }

    internal void ResetSession() { _request = 0; _intendedPlayback = null; Playing = false; }
    internal bool Toggle(Func<bool, long, bool> send)
    {
        var next = !(_intendedPlayback ?? Playing);
        if (send(next, _request + 1)) { _request++; _intendedPlayback = next; }
        return next;
    }
    internal void Seek(double bar, double second, Func<double, long, bool> send)
    {
        if (send(bar, _request + 1)) _request++;
        ShowPosition(second, bar);
    }
    internal bool Accept(JsonElement data, long chartRevision)
    {
        if (data.TryGetProperty("revision", out var rev) && rev.GetInt64() != chartRevision) return false;
        if (data.TryGetProperty("clockRequest", out var request) && request.GetInt64() < _request) return false;
        var second = data.GetProperty("second").GetDouble(); var bar = data.GetProperty("bar").GetDouble();
        if (!double.IsFinite(second) || !double.IsFinite(bar)) return false;
        _intendedPlayback = null;
        Playing = data.GetProperty("playing").GetBoolean();
        ShowPosition(second, bar);
        return true;
    }
    internal void ShowPosition(double second, double bar)
    {
        if (Second == second && Bar == bar) return;
        Second = second; Bar = bar;
        timeline.CurrentBar = bar;
        timing.SetCurrentBar(bar);
        RefreshReadout();
    }
    internal void RefreshReadout() => readout.Text = text.Format("Transport.Position",
        Second < TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(Math.Max(0, Second)).ToString(@"mm\:ss\.fff")
            : FormattableString.Invariant($"{Second:0.######E+0} s"), Bar);
}
