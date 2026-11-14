using DynamiteUniverse.Network;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Game;

/// <summary>Captured once per scene/run, including Restart. Tokens never enter the immutable report.</summary>
internal sealed class GameplayOnlineRun : IDisposable
{
    private PlayUpload? _upload;
    public void Dispose() => _upload?.Dispose();
    internal string Mode { get; } = ScoreStore.CurrentMode;
    internal bool Bleed { get; } = GameSession.Settings.BleedEffective;
    internal bool Mirror { get; } = GameSession.Settings.MirrorEnabled;
    private readonly Guid _playId = Guid.NewGuid();
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private readonly InstalledRelease? _installation;
    private readonly PlayOrigin? _origin;
    private readonly bool _wasLoggedIn;
    private readonly bool _compatible;
    private readonly string _chartId, _digest, _ruleset;
    internal GameplayOnlineRun(LoadedChart loaded, bool verification)
    {
        _chartId = loaded.ChartId; _digest = loaded.GameplayDigest ?? ""; _ruleset = loaded.RulesetId ?? "";
        _installation = verification ? null : GameSession.CurrentSelection?.Pack.Installation;
        _wasLoggedIn = OnlineSession.Connection.State.Account is not null;
        _compatible = OnlineSession.Connection.State.Descriptor?.Contracts?.PersonalRecords == 2;
        if (!_compatible || _installation is null) return;
        try
        {
            var access = OnlineSession.CaptureCatalog();
            if (access.Identity == _installation.Source) _origin = new(access.Identity, access.AccountId, OnlineSession.Connection.State.Account?.Username);
        }
        catch (OnlineException) { }
    }
    internal PlayUpload Finish(ScoreRecord score, double raw, double maximum, bool auto, bool failure, bool verification, bool seek)
    {
        var initial = !_wasLoggedIn ? "not_logged_in" : !_compatible ? "incompatible" : "ineligible";
        PlayReport? report = null;
        if (_origin is not null && _installation is not null && !auto && !failure && !verification && !seek && _digest.Length == 64)
        {
            initial = "ready";
            report = new(_playId, _installation.Release.Id, _chartId, _digest, _ruleset, Mode, Mirror, auto, true,
                score.Score, score.Perfect, score.Great, score.Good, score.Miss, score.MaxCombo, 2, Bleed,
                raw, maximum, score.Acc, "completed", _started, verification, seek, false);
        }
        var upload = _upload = new PlayUpload(_origin, report, initial, new PlayTransport());
        _ = upload.AttemptAsync(CurrentAccess(), automatic: true);
        return upload;
    }
    internal static CatalogAccess? CurrentAccess()
    {
        try { return OnlineSession.CaptureCatalog(); }
        catch (OnlineException) { return null; }
    }
}
