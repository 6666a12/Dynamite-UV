using System.Diagnostics;
using System.Globalization;
using Godot;
using Environment = System.Environment;

namespace DynamiteUniverse.Game;

/// <summary>Opt-in Editor/Internal profiler for a bounded media-time window in real gameplay.</summary>
internal sealed class StoryboardBenchmark : IDisposable
{
    private readonly StoryboardFramePhases? _phases;
    private readonly string _chartId;
    private readonly bool _storyboardLoaded, _forcedOff;
    private readonly double _beginSeconds, _endSeconds;
    private readonly Viewport _viewport;
    private readonly Rid _viewportRid;
    private readonly bool _profileGpu;
    private readonly List<double> _gpuMs = new(1024), _renderCpuMs = new(1024);
    private readonly List<double> _logicMs = new(1024), _noteMs = new(1024), _judgeMs = new(1024);
    private readonly List<int> _activeNotes = new(1024);
    private long _measurementStartUnixMs;
    private long _logicStartTicks, _noteStartTicks, _judgeStartTicks, _judgeEndTicks;
    private readonly List<int> _canvasCalls = new(1024);
    private int _rendererSamples;

    public StoryboardBenchmark(string chartId, bool storyboardLoaded, bool forcedOff, Viewport viewport)
    {
        _chartId = chartId;
        _storyboardLoaded = storyboardLoaded;
        _forcedOff = forcedOff;
        _viewport = viewport;
        _viewportRid = viewport.GetViewportRid();
        _profileGpu = Environment.GetEnvironmentVariable("DUV_STORYBOARD_GPU_PROFILE") == "1";
        _beginSeconds = ReadSeconds("DUV_STORYBOARD_BENCH_FROM", 4.25);
        _endSeconds = ReadSeconds("DUV_STORYBOARD_BENCH_UNTIL", 15.75);
        if (_endSeconds <= _beginSeconds || _endSeconds - _beginSeconds > 60)
        {
            _beginSeconds = 4.25;
            _endSeconds = 15.75;
        }
        if (_profileGpu) RenderingServer.ViewportSetMeasureRenderTime(_viewportRid, true);
        if (_profileGpu && Environment.GetEnvironmentVariable("DUV_STORYBOARD_PHASES") == "1")
            _phases = new StoryboardFramePhases();
    }

    private static double ReadSeconds(string variable, double fallback) => double.TryParse(
        Environment.GetEnvironmentVariable(variable), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
        && seconds >= 0 && seconds <= 3600 ? seconds : fallback;
    private readonly List<double> _frameMs = new(8192);
    private readonly List<double> _sampleMs = new(8192);
    private long _last;
    private int _maxItems, _maxRuns, _maxBatches;
    private long _sumItems;
    private bool _done, _disposed, _interrupted, _videoStarted, _videoWasActive, _videoLastActive;
    private long _videoStartFrames;
    public long VideoWindowUpdates { get; private set; }
    public bool VideoWindowActive => _videoStarted && _videoWasActive && _videoLastActive && !_interrupted;
    public bool Done => _done;
    public bool Valid => _done && !_interrupted && _frameMs.Count > 0;
    public double BeginSeconds => _beginSeconds;
    public double EndSeconds => _endSeconds;

    public void Interrupt()
    {
        _interrupted |= _last != 0 || _frameMs.Count != 0;
        _last = 0;
        _logicStartTicks = 0;
        _phases?.ResetPending();
    }

    public long Begin()
    {
        if (_done) return 0;
        var now = Stopwatch.GetTimestamp();
        _phases?.PreviousFrame(now);
        return now;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _phases?.Dispose();
        if (_profileGpu && GodotObject.IsInstanceValid(_viewport))
            RenderingServer.ViewportSetMeasureRenderTime(_viewportRid, false);
    }

    public void Frame(long timestamp, double mediaSeconds, double delta, int items, int runs, int batches,
        long videoFrames, bool videoActive)
    {
        if (_done) return;
        if (mediaSeconds >= _endSeconds)
        {
            _done = true;
            if (_videoStarted) VideoWindowUpdates = Math.Max(0, videoFrames - _videoStartFrames);
            if (_frameMs.Count != 0)
            {
                _frameMs.Sort();
                _sampleMs.Sort();
                GD.Print(string.Create(CultureInfo.InvariantCulture,
                    $"STORYBOARD_BENCH chart={_chartId} loaded={_storyboardLoaded} forced_off={_forcedOff} frames={_frameMs.Count} fps={1000 / _frameMs.Average():F2} frame_p50_ms={Percentile(_frameMs, .50):F3} frame_p95_ms={Percentile(_frameMs, .95):F3} frame_p99_ms={Percentile(_frameMs, .99):F3} update_p95_ms={Percentile(_sampleMs, .95):F3} average_items={(double)_sumItems / _frameMs.Count:F2} max_items={_maxItems} max_runs={_maxRuns} max_batches={_maxBatches}"));
                if (_profileGpu) PrintRendererProfile();
                _phases?.Print(_chartId);
            }
            Dispose();
            return;
        }
        if (mediaSeconds < _beginSeconds || delta <= 0 || !double.IsFinite(delta))
        {
            _last = 0;
            return;
        }
        if (!_videoStarted)
        {
            _videoStarted = true;
            _videoStartFrames = videoFrames;
            _videoWasActive = videoActive;
        }
        _videoLastActive = videoActive;
        if (_last != 0)
        {
            _frameMs.Add(Stopwatch.GetElapsedTime(_last, timestamp).TotalMilliseconds);
            _sampleMs.Add(Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);
            _maxItems = Math.Max(_maxItems, items);
            _maxRuns = Math.Max(_maxRuns, runs);
            _maxBatches = Math.Max(_maxBatches, batches);
            _sumItems += items;
            if (_measurementStartUnixMs == 0) _measurementStartUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_profileGpu && (_frameMs.Count & 7) == 0)
            {
                _logicStartTicks = timestamp;
                _noteStartTicks = _judgeStartTicks = _judgeEndTicks = 0;
            }
        }
        _last = timestamp;
    }

    public void MarkNotes() { if (_logicStartTicks != 0) _noteStartTicks = Stopwatch.GetTimestamp(); }
    public void MarkJudge() { if (_logicStartTicks != 0) _judgeStartTicks = Stopwatch.GetTimestamp(); }
    public void MarkHud() { if (_logicStartTicks != 0) _judgeEndTicks = Stopwatch.GetTimestamp(); }

    public void FinishLogic(int activeNotes)
    {
        if (_logicStartTicks == 0) return;
        var endTicks = Stopwatch.GetTimestamp();
        _logicMs.Add(Stopwatch.GetElapsedTime(_logicStartTicks, endTicks).TotalMilliseconds);
        _noteMs.Add(Stopwatch.GetElapsedTime(_noteStartTicks, _judgeStartTicks).TotalMilliseconds);
        _judgeMs.Add(Stopwatch.GetElapsedTime(_judgeStartTicks, _judgeEndTicks).TotalMilliseconds);
        _activeNotes.Add(activeNotes);
        _logicStartTicks = 0;
        var cpu = RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewportRid);
        var gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewportRid);
        if (double.IsFinite(cpu) && cpu > 0) _renderCpuMs.Add(cpu);
        if (double.IsFinite(gpu) && gpu > 0) _gpuMs.Add(gpu);
        _canvasCalls.Add(_viewport.GetRenderInfo(Viewport.RenderInfoType.Canvas, Viewport.RenderInfo.DrawCallsInFrame));
        _rendererSamples++;
        _phases?.Arm(Stopwatch.GetTimestamp());
    }

    private void PrintRendererProfile()
    {
        _gpuMs.Sort();
        _renderCpuMs.Sort();
        _canvasCalls.Sort();
        _logicMs.Sort();
        _noteMs.Sort();
        _judgeMs.Sort();
        _activeNotes.Sort();
        var ended = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"STORYBOARD_LOGIC chart={_chartId} from_unix_ms={_measurementStartUnixMs} until_unix_ms={ended} main_p50_ms={Percentile(_logicMs, .50):F3} main_p95_ms={Percentile(_logicMs, .95):F3} notes_p95_ms={Percentile(_noteMs, .95):F3} judge_p95_ms={Percentile(_judgeMs, .95):F3} active_notes_p95={Percentile(_activeNotes, .95)}"));
        var window = DisplayServer.WindowGetSize();
        // Sample the actual framebuffer once after the timing window. ViewportTexture.GetSize()
        // on a scaled root Window can instead report a transformed logical size.
        using var targetImage = _viewport.GetTexture().GetImage();
        var target = targetImage?.GetSize() ?? Vector2I.Zero;
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"STORYBOARD_GPU chart={_chartId} off={_forcedOff} window={window.X}x{window.Y} target={target.X}x{target.Y} renderer={RenderingServer.GetCurrentRenderingMethod()} vsync={DisplayServer.WindowGetVsyncMode()} max_fps={Engine.MaxFps} from_s={_beginSeconds:F3} until_s={_endSeconds:F3} samples={_rendererSamples} gpu_valid={_gpuMs.Count} viewport_gpu_p50_ms={Percentile(_gpuMs, .50):F3} viewport_gpu_p95_ms={Percentile(_gpuMs, .95):F3} viewport_cpu_p50_ms={Percentile(_renderCpuMs, .50):F3} viewport_cpu_p95_ms={Percentile(_renderCpuMs, .95):F3} canvas_calls_p50={Percentile(_canvasCalls, .50)} canvas_calls_p95={Percentile(_canvasCalls, .95)}"));
    }

    private static T Percentile<T>(List<T> sorted, double p) =>
        sorted.Count > 0 ? sorted[(int)Math.Ceiling(p * sorted.Count) - 1] : default!;
}
