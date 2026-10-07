namespace DynamiteUniverse.Ui;

/// <summary>Result-only clock. Asset loading may extend the opaque hold, never the opening.</summary>
public sealed class ResultRevealTimeline
{
    public const double CloseEnd = 0.60;
    public const double HoldEnd = 0.92;
    public const double GradeStart = 1.40;
    public const double FullDuration = 2.65;
    public const double ReducedDuration = 0.26;
    public const int FrameCount = 30;
    public const int FramesPerSecond = 24;
    public UiMotionMode Mode { get; }
    public double Elapsed { get; private set; }
    private bool _skipRequested;
    public double Duration => Mode == UiMotionMode.Full ? FullDuration :
        Mode == UiMotionMode.Reduced ? ReducedDuration : 0;
    public bool IsComplete => Elapsed >= Duration;
    public bool BackgroundVisible => Mode != UiMotionMode.Full || Elapsed >= CloseEnd;
    public bool GradeVisible => Mode != UiMotionMode.Full || Elapsed >= GradeStart;
    public int GradeFrame => Math.Clamp((int)Math.Floor((Elapsed - GradeStart) * FramesPerSecond), 0, FrameCount - 1);
    public double Coverage => Mode != UiMotionMode.Full ? 0 : Elapsed < CloseEnd
        ? Smooth(Elapsed / CloseEnd) : Elapsed <= HoldEnd ? 1 : 1 - Smooth((Elapsed - HoldEnd) / (GradeStart - HoldEnd));

    public ResultRevealTimeline(UiMotionMode mode) => Mode = mode;

    public void Advance(double delta, bool gradeReady)
    {
        if (!double.IsFinite(delta) || delta < 0) return;
        if (_skipRequested && gradeReady) { Elapsed = Duration; return; }
        var next = Math.Min(Duration, Elapsed + delta);
        // Even a long frame cannot reveal an unloaded grade or half-built result.
        Elapsed = Mode == UiMotionMode.Full && !gradeReady ? Math.Min(next, HoldEnd) : next;
    }

    public void Skip(bool gradeReady)
    {
        _skipRequested = true;
        if (gradeReady) Elapsed = Duration;
    }

    public double Opacity(double start, double end) => Mode switch
    {
        UiMotionMode.Off => 1,
        UiMotionMode.Reduced => Smooth(Elapsed / ReducedDuration),
        _ => Smooth((Elapsed - start) / (end - start)),
    };

    private static double Smooth(double v)
    {
        v = Math.Clamp(v, 0, 1);
        return v * v * (3 - 2 * v);
    }
}
