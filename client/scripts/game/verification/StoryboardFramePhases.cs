using System.Diagnostics;
using System.Globalization;
using Godot;

namespace DynamiteUniverse.Game;

/// <summary>Developer-only timeline for the synchronous renderer; not a presentation/scanout tracer.</summary>
internal sealed class StoryboardFramePhases : IDisposable
{
    private readonly List<double> _toPre = new(1024), _draw = new(1024), _afterPost = new(1024);
    private long _logicEnd, _pre, _post;
    private bool _attached;

    internal StoryboardFramePhases()
    {
        RenderingServer.FramePreDraw += OnPreDraw;
        RenderingServer.FramePostDraw += OnPostDraw;
        _attached = true;
    }

    internal void PreviousFrame(long next)
    {
        if (_post > 0 && _post <= next)
        {
            _toPre.Add(Stopwatch.GetElapsedTime(_logicEnd, _pre).TotalMilliseconds);
            _draw.Add(Stopwatch.GetElapsedTime(_pre, _post).TotalMilliseconds);
            _afterPost.Add(Stopwatch.GetElapsedTime(_post, next).TotalMilliseconds);
        }
        _logicEnd = _pre = _post = 0;
    }

    internal void Arm(long logicEnd) => _logicEnd = logicEnd;
    internal void ResetPending() => _logicEnd = _pre = _post = 0;

    private void OnPreDraw()
    {
        if (_logicEnd > 0 && _pre == 0) _pre = Stopwatch.GetTimestamp();
    }

    private void OnPostDraw()
    {
        if (_pre > 0 && _post == 0) _post = Stopwatch.GetTimestamp();
    }

    internal void Print(string chart)
    {
        _toPre.Sort();
        _draw.Sort();
        _afterPost.Sort();
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"STORYBOARD_PHASE chart={chart} samples={_draw.Count} logic_end_to_draw_p50_ms={Percentile(_toPre, .5):F3} logic_end_to_draw_p95_ms={Percentile(_toPre, .95):F3} draw_wall_p50_ms={Percentile(_draw, .5):F3} draw_wall_p95_ms={Percentile(_draw, .95):F3} post_draw_to_logic_p50_ms={Percentile(_afterPost, .5):F3} post_draw_to_logic_p95_ms={Percentile(_afterPost, .95):F3}"));
    }

    private static double Percentile(List<double> values, double fraction) =>
        values.Count == 0 ? 0 : values[(int)Math.Ceiling(values.Count * fraction) - 1];

    public void Dispose()
    {
        if (!_attached) return;
        RenderingServer.FramePreDraw -= OnPreDraw;
        RenderingServer.FramePostDraw -= OnPostDraw;
        _attached = false;
    }
}
