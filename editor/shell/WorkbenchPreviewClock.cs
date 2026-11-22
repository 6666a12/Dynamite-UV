using System.Text.Json;
using Avalonia.Controls;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>The preview stage owns time. Shell displays the last authoritative pair, or a
/// locally selected seek while its reply is in flight. There is no second advancing clock.</summary>
internal sealed class WorkbenchPreviewClock(TimelineView timeline, TimingView timing, TextBlock readout, IWorkbenchLocalizer text)
{
    private long _request;
    private bool? _intendedPlayback;
    private long? _exactSeekRequest;
    internal double Second { get; private set; }
    internal double Bar { get; private set; }
    internal ExactBarTime? ExactBar { get; private set; }
    internal bool Playing { get; private set; }

    internal void ResetSession() { _request = 0; _intendedPlayback = null; Playing = false; ShowPosition(Second, Bar); }
    internal bool Toggle(Func<bool, long, bool> send)
    {
        var next = !(_intendedPlayback ?? Playing);
        RequestPlayback(next, send);
        return next;
    }
    internal void Pause(Func<bool, long, bool> send)
    {
        if (_intendedPlayback ?? Playing) RequestPlayback(false, send);
    }
    private void RequestPlayback(bool playing, Func<bool, long, bool> send)
    {
        if (send(playing, _request + 1)) { _request++; _intendedPlayback = playing; }
    }
    internal void Seek(double bar, double second, Func<double, long, bool> send, ExactBarTime? exactBar = null)
    {
        bool sent = send(bar, _request + 1);
        if (sent) _request++;
        ShowPosition(second, bar, exactBar, sent && exactBar.HasValue ? _request : null);
    }
    internal bool Accept(JsonElement data, long chartRevision)
    {
        if (data.TryGetProperty("revision", out var rev) && rev.GetInt64() != chartRevision) return false;
        if (data.TryGetProperty("clockRequest", out var request) && request.GetInt64() < _request) return false;
        var second = data.GetProperty("second").GetDouble(); var bar = data.GetProperty("bar").GetDouble();
        if (!double.IsFinite(second) || !double.IsFinite(bar)) return false;
        _intendedPlayback = null;
        Playing = data.GetProperty("playing").GetBoolean();
        // Keep a rational anchor only if the stage says this is still the same seek. A stage
        // may numerically round-trip that position, or move to a visually indistinguishable one.
        bool anchored = !Playing && _exactSeekRequest is { } id &&
            data.TryGetProperty("positionSeek", out var seek) && seek.ValueKind == JsonValueKind.Number && seek.GetInt64() == id;
        ShowPosition(second, bar, anchored ? ExactBar : null, anchored ? _exactSeekRequest : null);
        return true;
    }
    internal void ShowPosition(double second, double bar, ExactBarTime? exactBar = null, long? positionSeek = null)
    {
        _exactSeekRequest = positionSeek;
        if (Second == second && Bar == bar && ExactBar == exactBar) return;
        Second = second; Bar = bar; ExactBar = exactBar;
        timeline.CurrentBar = bar;
        timing.SetCurrentBar(bar, exactBar);
        RefreshReadout();
    }
    internal void RefreshReadout() => readout.Text = text.Format("Transport.Position",
        Second < TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(Math.Max(0, Second)).ToString(@"mm\:ss\.fff")
            : FormattableString.Invariant($"{Second:0.######E+0} s"), Bar);
}
