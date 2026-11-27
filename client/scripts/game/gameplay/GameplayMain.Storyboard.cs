namespace DynamiteUniverse.Game;

public partial class GameplayMain
{
    private ChartPack? _storyboardPack;
    private GameplayStoryboardSession? _storyboard;
    private StoryboardBenchmark? _storyboardBenchmark;
    private ulong _storyboardWarmUntil;

    private int VerificationFallSpeedLevel(int preference)
    {
        if (_verification is not null && int.TryParse(Environment.GetEnvironmentVariable("DUV_VERIFY_FALL_SPEED_LEVEL"), out var level) &&
            DynamiteUniverse.Shared.Chart.GameplayFallSpeed.IsValid(level)) return level;
        return preference;
    }

    private void PrepareStoryboard()
    {
        // Debug switches are only accepted by the existing Editor/Internal verification runner.
        string? Setting(string name) => _verification is not null ? Environment.GetEnvironmentVariable(name) : null;
        var skip = Setting("DUV_STORYBOARD_OFF") == "1";
        var batch = Setting("DUV_STORYBOARD_NO_BATCH") != "1";
        int? brightnessOverride = int.TryParse(Setting("DUV_STORYBOARD_BRIGHTNESS"), out var brightness) ? brightness : null;
        var tier = Setting("DUV_STORYBOARD_MEMORY_MIB") switch
        {
            "128" => 128, "256" => 256, _ => GameSession.Settings.EffectiveStoryboardMemoryMiB
        };
        double? lookahead = Setting("DUV_STORYBOARD_PREFETCH") switch { "1" => 1, "2" => 2, _ => null };
        if (Setting("DUV_STORYBOARD_ATLAS") == "1")
            Godot.GD.PushWarning("The old eager Storyboard atlas benchmark is unavailable in the streaming runtime");
        _storyboard = GameplayStoryboardSession.Mount(_storyboardPack, _loaded, _sharedStage,
            StageOffsetY, _playback.AudioDurationSeconds, GameSession.Settings, skip, batch, brightnessOverride,
            tier, lookahead, Setting("DUV_STORYBOARD_VIDEO_FILE"));
        if (Setting("DUV_STORYBOARD_BENCH") == "1")
            _storyboardBenchmark = new StoryboardBenchmark(_run.ChartId, _storyboard is not null, skip, GetViewport());
    }

    private void UpdateStoryboard(double mediaSeconds, double delta)
    {
        var watch = _storyboardBenchmark;
        var start = watch?.Begin();
        _storyboard?.Sample(mediaSeconds, _playback.HasNaturallyEnded);
        if (watch is not null && start is { } timestamp)
        {
            watch.Frame(timestamp, mediaSeconds, delta, _storyboard?.ActiveItems ?? 0, _storyboard?.DrawRuns ?? 0,
                _storyboard?.SpriteBatches ?? 0, _storyboard?.VideoFrameUpdates ?? 0, _storyboard?.VideoPresenting == true);
            if (!watch.Done) return;
            _storyboard?.Report(watch.BeginSeconds, watch.EndSeconds, watch.VideoWindowUpdates, watch.VideoWindowActive);
            var cache = _sharedStage.StaticBackdropPixels;
            var window = GetWindow();
            Godot.GD.Print($"PLAYFIELD_BENCH cache={cache.X}x{cache.Y} mode={window.Mode} " +
                $"links_retained={_noteRenderer.RetainedLinkGeometryUpdates} links_submitted={_noteRenderer.SubmittedLinkGeometryUpdates}");
            if (!watch.Valid) Godot.GD.PushError("Storyboard benchmark window had no samples or transport was interrupted");
            GetTree().Quit(watch.Valid ? 0 : 2);
        }
    }
}
