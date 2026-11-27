using System.Globalization;
using DynamiteUniverse.Shared.Storyboard;
using DynamiteUniverse.Ui;
using Godot;

namespace DynamiteUniverse.Game;

/// <summary>Per-song ownership of a visual-only performance, its streaming work and its fallback.</summary>
internal sealed class GameplayStoryboardSession(StoryboardCanvas canvas, LoadedPerformance performance,
    StoryboardTextureStore textures, double audioLength, int tierMiB, StoryboardVideoPilot? video,
    Action restoreStockBackdrop, string identity)
{
    private bool _insidePlayback, _disposed, _gradientUnavailable;
    private readonly long _mountedAt = System.Diagnostics.Stopwatch.GetTimestamp();
    private long _seekAt;
    private double _firstReadyMs = -1, _seekMaxMs;
    private int _seeksReady;
    private StoryboardGradientPage? _gradientPage;
    private long _gradientBytes;
    public int ActiveItems => _disposed || !canvas.Visible ? 0 : canvas.ActiveDrawItems;
    public int DrawRuns => _disposed || !canvas.Visible ? 0 : canvas.DrawRuns;
    public int SpriteBatches => _disposed || !canvas.Visible ? 0 : canvas.SpriteBatches;
    public long VideoFrameUpdates => video?.FrameUpdates ?? 0;
    public bool VideoPresenting => !_disposed && video?.IsPresenting == true;
    public bool VideoCovers(double from, double until) => video?.Covers(from, until) == true;

    public static GameplayStoryboardSession? Mount(ChartPack? pack, LoadedChart chart, GameplayStageRenderer stage,
        float stageOffsetY, double audioLength, GameSettings settings, bool verificationSkip, bool batchSprites,
        int? verificationBrightness, int tierMiB, double? verificationLookahead, string? verificationVideo = null)
    {
        // Suppression and Public gating precede even checking for an attachment.
        if (pack is null || !ChartPack.IsEditorOrInternal || !settings.StoryboardEnabled ||
            settings.MotionMode != UiMotionMode.Full || verificationSkip || !(audioLength > 0) || !double.IsFinite(audioLength))
            return null;
        try
        {
            var loaded = GameplayStoryboardLoader.Load(pack, chart, audioLength);
            if (loaded is null) return null;
            if (!StoryboardResidencyLedger.RendererSupported)
            {
                GD.PushWarning("Storyboard uses the stock backdrop on renderers without confirmed texture retirement");
                return null;
            }
            StoryboardTextureStore? textures = null;
            var canvas = new StoryboardCanvas(loaded.Sampler, image => textures!.Resolve(image), batchSprites) { Visible = false };
            textures = new(loaded.TexturePlan, tierMiB, canvas.InvalidateTextureCommands, verificationLookahead);
            var backdrop = loaded.Document.Backdrop;
            Color? replacement = backdrop.Mode == StoryboardBackdropMode.Replace
                ? new Color(backdrop.Color.R / 255f, backdrop.Color.G / 255f, backdrop.Color.B / 255f, 1f) : null;
            var brightness = Math.Clamp(verificationBrightness ?? settings.StoryboardBrightnessPercent, 0, 100) * 0.01f;
            var video = TryMountLocalVideo(pack, verificationVideo);
            Node2D node = canvas;
            if (video is not null)
            {
                node = new Node2D { Name = "StoryboardPerformance" };
                node.AddChild(video);
                node.AddChild(canvas);
            }
            var restoreBackdrop = stage.AttachStoryboard(node, stageOffsetY, replacement, brightness);
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"STORYBOARD_LOAD chart={chart.ChartId} bytes={loaded.SourceBytes} read_ms={loaded.ReadMs:F3} parse_ms={loaded.ParseMs:F3} preflight_ms={loaded.AssetPreflightMs:F3} compile_ms={loaded.CompileMs:F3} backdrop={backdrop.Mode} declarations={loaded.Document.Assets.Count} tier_mib={tierMiB}"));
            ReportSet(loaded.TexturePlan);
            return new(canvas, loaded, textures, audioLength, tierMiB, video, restoreBackdrop, $"{chart.PackId}/{chart.ChartId}");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or
            ArgumentException or System.Text.Json.JsonException or OverflowException)
        {
            GD.PushWarning($"Storyboard for {chart.PackId}/{chart.ChartId} skipped: {ex.Message}");
            return null;
        }
    }

    private static void ReportSet(StoryboardTexturePlan plan)
    {
        var edges = new List<(double Time, int Count, long Bytes)>();
        foreach (var request in plan.Requests)
            foreach (var interval in request.Intervals)
            {
                edges.Add((interval.From, 1, request.Key.TextureBytes));
                edges.Add((interval.Until, -1, -request.Key.TextureBytes));
            }
        edges.Sort(static (a, b) => { var time = a.Time.CompareTo(b.Time); return time != 0 ? time : a.Count.CompareTo(b.Count); });
        long bytes = 0, peakBytes = 0;
        int count = 0, peakCount = 0;
        double bytesTime = 0, slotsTime = 0;
        foreach (var edge in edges)
        {
            count += edge.Count;
            bytes += edge.Bytes;
            if (count > peakCount) { peakCount = count; slotsTime = edge.Time; }
            if (bytes > peakBytes) { peakBytes = bytes; bytesTime = edge.Time; }
        }
        GD.Print(FormattableString.Invariant($"STORYBOARD_WORKING_SET peak_slots={peakCount} slots_at_s={slotsTime:F3} peak_bytes={peakBytes} bytes_at_s={bytesTime:F3}"));
    }

    private static StoryboardVideoPilot? TryMountLocalVideo(ChartPack pack, string? file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        if (file.Length > 65 || !string.Equals(Path.GetExtension(file), ".ogv", StringComparison.OrdinalIgnoreCase) ||
            !file.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            GD.PushWarning("Storyboard video pilot expects a local .ogv fixture file name");
            return null;
        }
        var root = Path.GetFullPath(ProjectSettings.GlobalizePath("res://../.tmp"));
        if (!Path.IsPathFullyQualified(pack.DirPath) || !StoryboardLocalFiles.Within(root, Path.GetFullPath(pack.DirPath)))
        {
            GD.PushWarning("Storyboard video pilot skipped outside the workspace .tmp directory");
            return null;
        }
        try
        {
            var path = StoryboardLocalFiles.SafePath(Path.Combine(pack.DirPath, "storyboard"), file, ".ogv");
            return StoryboardVideoPilot.TryCreate(path, authoredStartSeconds: 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            GD.PushWarning($"Storyboard video pilot skipped: {ex.Message}");
            return null;
        }
    }

    public void SetPaused(bool paused) => video?.SetPaused(paused);
    public void Report(double from, double until, long videoUpdates, bool active)
    {
        textures.Report("bench");
        ReportReadiness("bench");
        video?.ReportTelemetry("bench", videoUpdates, active && VideoCovers(from, until));
    }

    public bool Prime(double mediaSeconds)
    {
        if (_disposed || mediaSeconds >= audioLength) return true;
        var gradientsReady = PrepareGradients();
        textures.Tick(mediaSeconds, warming: true);
        if (CheckFailure()) return true;
        return gradientsReady && textures.CurrentReady;
    }

    public void TransportJump()
    {
        if (_disposed) return;
        textures.InvalidateRequests();
        _seekAt = System.Diagnostics.Stopwatch.GetTimestamp();
        canvas.InvalidateTextureCommands();
        video?.TransportJump();
    }

    public void MemoryPressure() => textures.MemoryPressure();

    public void Sample(double mediaSeconds, bool naturallyEnded)
    {
        if (_disposed) return;
        if (naturallyEnded || mediaSeconds >= audioLength || !(mediaSeconds >= 0))
        {
            if (_insidePlayback) textures.InvalidateRequests();
            _insidePlayback = false;
            canvas.Hide();
            video?.LeavePlaybackRange();
            return;
        }
        _insidePlayback = true;
        var gradientsReady = PrepareGradients();
        textures.Tick(mediaSeconds);
        if (CheckFailure()) return;
        video?.Sync(mediaSeconds);
        if (gradientsReady && textures.CurrentReady)
        {
            canvas.Show();
            canvas.Sample(mediaSeconds);
            if (_firstReadyMs < 0 && canvas.ActiveDrawItems > 0)
                _firstReadyMs = System.Diagnostics.Stopwatch.GetElapsedTime(_mountedAt).TotalMilliseconds;
            if (_seekAt != 0)
            {
                _seekMaxMs = Math.Max(_seekMaxMs, System.Diagnostics.Stopwatch.GetElapsedTime(_seekAt).TotalMilliseconds);
                ++_seeksReady;
                _seekAt = 0;
            }
        }
        else canvas.Hide(); // Retain only the authorized static backdrop (and independent video pilot).
    }

    private bool PrepareGradients()
    {
        if (_gradientPage is not null || performance.Sampler.Gradients.Count == 0) return true;
        if (_gradientUnavailable) return false;
        var rows = performance.Sampler.Gradients.Count;
        var bytes = (long)StoryboardLimits.GradientRampWidth * rows * 4;
        var scratch = 3 * bytes;
        if (rows > StoryboardLimits.MaxTextureDimension || bytes + scratch > tierMiB * StoryboardTextureStore.MiB)
        {
            _gradientUnavailable = true;
            GD.PushWarning($"Storyboard for {identity} exceeds the prepared gradient-page limit; using a static fallback");
            return false;
        }
        if (!StoryboardResidencyLedger.TryReserve(tierMiB * StoryboardTextureStore.MiB, bytes, scratch)) return false;
        try
        {
            _gradientPage = new(performance.Sampler.Gradients);
            _gradientBytes = bytes;
            canvas.Gradients = _gradientPage;
            StoryboardResidencyLedger.SubmitJob(bytes, 2 * bytes);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            StoryboardResidencyLedger.CancelJob(bytes, scratch);
            _gradientUnavailable = true;
            GD.PushWarning($"Storyboard gradient page for {identity} could not be created: {ex.Message}");
            return false;
        }
    }

    private void ReportReadiness(string reason) => GD.Print(FormattableString.Invariant(
        $"STORYBOARD_READY reason={reason} first_visible_ms={_firstReadyMs:F3} seeks_ready={_seeksReady} seek_ready_max_ms={_seekMaxMs:F3}"));

    private bool CheckFailure()
    {
        if (textures.Failure is not { } error) return false;
        GD.PushWarning($"Storyboard for {identity} skipped: {error}");
        restoreStockBackdrop();
        Stop();
        return true;
    }

    public void Stop()
    {
        if (_disposed) return;
        _disposed = true;
        video?.Stop();
        if (GodotObject.IsInstanceValid(canvas)) canvas.Finish();
        ReportReadiness("stop");
        textures.Dispose();
        if (_gradientPage is not null)
        {
            _gradientPage.Dispose();
            _gradientPage = null;
            StoryboardResidencyLedger.Retire(_gradientBytes, 1);
        }
    }
}
