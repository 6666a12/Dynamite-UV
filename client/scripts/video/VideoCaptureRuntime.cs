using System.Text.Json;
using DynamiteUniverse.Game;
using DynamiteUniverse.Ui;
using Godot;

namespace DynamiteUniverse.Video;

/// <summary>Drives the production scenes using their normal layout, transitions and Auto judging.</summary>
public partial class VideoCaptureRuntime : Node
{
    private const int Fps = VideoCaptureInvocation.FramesPerSecond;
    private const int MaxGameplaySeconds = 30 * 60;
    private readonly Dictionary<string, int> _marks = new(StringComparer.Ordinal);
    private VideoCaptureInvocation? _invocation;
    private const int WarmupFrames = 90;
    private const int PreviewLeadFrames = 12;
    private int _trimFrame = -1;
    private int _previewAvailableFrames, _recordedPreviewFrames;
    private int _totalFrames;
    private bool _autoResult;

    public override void _Ready()
    {
        if (VideoCaptureInvocation.IsDedicatedBuild)
            CallDeferred(MethodName.RunCapture);
    }

    private async void RunCapture()
    {
        try
        {
            _invocation = VideoCaptureInvocation.Read();
            GetWindow().GuiDisableInput = true;
            ProcessMode = ProcessModeEnum.Always;
            EmitStage("boot");
            await Record(_invocation);
            WriteReport(true, string.Empty);
            Emit(new { version = 1, @event = "done", chartId = _invocation.ChartId,
                frame = _marks["result-held"], trimFrame = _trimFrame, totalFrames = _totalFrames });
            GetTree().Quit();
        }
        catch (Exception error)
        {
            // Keep the original failure even if disk-full or invalid paths prevent a report.
            try { if (_invocation is not null) WriteReport(false, error.Message); }
            catch (Exception reportError) { GD.PushWarning($"Capture report could not be written: {reportError.Message}"); }
            Emit(new { version = 1, @event = "error", message = error.Message });
            GetTree().Quit(20);
        }
    }

    private async Task Record(VideoCaptureInvocation invocation)
    {
        _previewAvailableFrames = PrepareSelection(invocation);
        _recordedPreviewFrames = Math.Min(invocation.PreviewFrames, _previewAvailableFrames);
        var code = GetTree().ChangeSceneToFile(UiRoutes.SongSelect);
        if (code != Error.Ok)
            throw new InvalidOperationException($"Could not open Song Select: {code}");
        await WaitFor(() => GetTree().CurrentScene is SongSelect, 10 * Fps, "song-select scene did not become ready");
        var select = (SongSelect)GetTree().CurrentScene;
        var preview = _recordedPreviewFrames > 0;
        // Keep entry warm-up out of the stream, except the last 12 frames when auditioning.
        // Arm in ProcessFrame before the draw/AudioDriverDummy mix of that exact source frame.
        await WaitFrames(preview ? WarmupFrames - PreviewLeadFrames : WarmupFrames);
        if (Math.Abs(select.GetProcessDeltaTime() - 1.0 / Fps) > 1e-7)
            throw new InvalidDataException("UV Video Capture requires a fixed 60 FPS movie clock");
        if (preview)
        {
            Arm(invocation);
            await WaitFrames(PreviewLeadFrames);
        }
        EmitStage("select");
        Mark("expand");
        select.PrepareVideoCapture(invocation.ChartId, preview);
        await WaitFor(() => select.VideoCaptureReadyToStart, 15 * Fps, "selected chart did not become ready");
        if (preview)
        {
            await WaitFor(() => select.VideoCapturePreviewStartFrame.HasValue, 15 * Fps, "formal preview did not start");
            var first = checked((int)select.VideoCapturePreviewStartFrame!.Value);
            _marks.Add("preview-playing", first);
            EmitStage("preview", first);
            await WaitFrames(VideoCaptureTiming.FramesUntilPreviewEnd(first, _recordedPreviewFrames, SourceFrame()));
            Mark("preview-ended");
        }

        Mark("start");
        select.StartVideoCapture(invocation.ChartId);
        await WaitFor(() => GetTree().Paused && TransitionDirector.IsBusy, 2 * Fps, "formal Track Handoff did not begin");
        Mark("handoff");
        if (!preview)
            Arm(invocation);
        if (_trimFrame != (preview ? _marks["expand"] - PreviewLeadFrames : _marks["handoff"]))
            throw new InvalidDataException("The first planned stream frame did not match the clip boundary");
        EmitStage("handoff");
        await WaitFor(() => GetTree().CurrentScene is GameplayMain, 10 * Fps, "gameplay scene did not load");
        var gameplay = (GameplayMain)GetTree().CurrentScene;
        await WaitFor(() => gameplay.VideoCaptureMusicStarted, 10 * Fps, "gameplay music did not start");
        Mark("gameplay-music");
        EmitStage("gameplay");

        var duration = gameplay.VideoCaptureExpectedEnd;
        if (!double.IsFinite(duration) || duration <= 0 || duration > MaxGameplaySeconds)
            throw new InvalidDataException($"recording duration must be positive and no longer than {MaxGameplaySeconds / 60} minutes");
        await WaitFor(() =>
        {
            if (gameplay.VideoCaptureMusicEnded && !_marks.ContainsKey("music-ended"))
                Mark("music-ended");
            return gameplay.VideoCaptureResultStarted;
        }, (int)Math.Ceiling((duration + 15) * Fps), "gameplay did not complete within its duration plus 15 seconds");
        if (!_marks.ContainsKey("music-ended") || !gameplay.VideoCaptureAutoResult)
            throw new InvalidDataException("expected a completed Auto result after the music ended");
        _autoResult = true;
        Mark("result-intro");
        EmitStage("result");
        await WaitForRevealDraw(gameplay);
        Mark("result-revealed");
        var held = VideoCaptureTiming.EndFrame(_marks["result-revealed"], invocation.ResultHoldFrames);
        await WaitFrames(invocation.ResultHoldFrames);
        Mark("result-held");
        if (_marks["result-held"] != held) throw new InvalidDataException("The result hold skipped a frame.");
        _totalFrames = VideoCaptureTiming.EndFrame(held, 1); // The iteration calling Quit still draws and writes its frame.
        invocation.ConfirmNoSavedData();
    }

    private async Task WaitForRevealDraw(GameplayMain gameplay)
    {
        // Pinned Godot runs idle timers after node processing but before returning the quit flag
        // and drawing. ProcessFrame alone observes a node's first complete reveal one frame late.
        for (var i = 0; i < 20 * Fps; i++)
        {
            await ToSignal(GetTree().CreateTimer(0), SceneTreeTimer.SignalName.Timeout);
            if (gameplay.VideoCaptureResultRevealed) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        throw new InvalidOperationException("formal result reveal did not complete");
    }

    private static int PrepareSelection(VideoCaptureInvocation invocation)
    {
        GameSession.EnsureInit();
        // Force Auto in memory only. Do not persist the configuration or touch normal saves.
        GameSession.Settings.AutoEnabled = true;
        if (GameSession.Packs.Count != 1 || GameSession.Packs[0] is not { IsV2: true } pack ||
            pack.ChartOf(invocation.ChartId) is not { } diff)
            throw new InvalidDataException($"chart '{invocation.ChartId}' was not found in the single strict V2 snapshot");
        if (!pack.TryLoadChart(diff, out var loaded) || loaded is null)
            throw new InvalidDataException($"chart '{invocation.ChartId}' could not be loaded");
        // Resolve both media resources before the first streamed frame, outside timed stages.
        var audio = VideoCaptureAudio.Load(loaded.ResolvedAudioPath)
            ?? throw new InvalidDataException($"The selected chart audio could not be decoded: '{invocation.ChartId}'");
        CoverTextureCache.Load(pack.CoverPath);
        GameSession.CachePreload(pack, diff, loaded);
        GameSession.CommitSelection(pack, diff, loaded);
        if (invocation.PreviewFrames == 0) return 0;
        var preview = pack.PreviewFor(diff) ?? throw new InvalidDataException("The selected chart has no audition interval.");
        return VideoCaptureTiming.AvailablePreviewFrames(preview.StartSec, preview.DurationSec, audio.GetLength());
    }

    private async Task WaitFor(Func<bool> predicate, int budget, string error)
    {
        for (var elapsed = 0; elapsed < budget; elapsed++)
        {
            if (predicate()) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (!predicate()) throw new InvalidOperationException(error);
    }

    private async Task WaitFrames(int count)
    {
        for (var i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Arm(VideoCaptureInvocation invocation)
    {
        var frame = SourceFrame();
        if (_trimFrame >= 0)
            throw new InvalidOperationException("A capture cannot start twice");
        if (invocation.StreamWriter is { } writer)
        {
            using var accepted = writer.Call("arm");
            if (accepted.VariantType != Variant.Type.Bool || !accepted.AsBool())
                throw new InvalidOperationException("The native video stream could not be armed");
        }
        _trimFrame = frame;
    }

    // While the scene is processing, both counters count completed iterations; the
    // draw and MovieWriter callback for this iteration are still ahead. An offscreen/missed
    // draw would change the clip, so fail instead of shipping duplicated/stale content.
    private static int SourceFrame()
    {
        var frames = Engine.GetFramesDrawn();
        if ((ulong)frames != Engine.GetProcessFrames())
            throw new InvalidDataException("The recording viewport did not render every source frame");
        return frames;
    }

    private void Mark(string name) => _marks.Add(name, SourceFrame());

    private static void EmitStage(string stage, int? frame = null) =>
        Emit(new { version = 1, @event = "stage", stage, frame = frame ?? SourceFrame() });

    private static void Emit<T>(T value) => GD.Print("UV_VIDEO_EXPORT " + JsonSerializer.Serialize(value));

    private void WriteReport(bool pass, string reason)
    {
        var invocation = _invocation!;
        var report = new { version = 2, pass, reason, chartId = invocation.ChartId, autoResult = _autoResult,
            trimFrame = _trimFrame, totalFrames = _totalFrames, frames = _marks,
            previewFrames = invocation.PreviewFrames, previewAvailableFrames = _previewAvailableFrames,
            recordedPreviewFrames = _recordedPreviewFrames,
            resultHoldFrames = invocation.ResultHoldFrames };
        using var file = new FileStream(Path.Combine(invocation.OutputDirectory, "capture-result.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(file, report);
    }
}
