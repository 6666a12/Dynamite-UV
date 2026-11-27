using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Shared.Storyboard;

/// <summary>Absolute half-open frame boundaries used by both sampling and the use table.</summary>
internal static class StoryboardSequenceTiming
{
    internal static StoryboardImage ImageAt(SamplerFrames sequence, in SamplerMoment start, double seconds) =>
        sequence.DistinctImages.Length == 1 ? sequence.DistinctImages[0] : sequence.Frames[Locate(sequence, start, seconds).Index].Image;

    internal static FramePosition Locate(SamplerFrames sequence, in SamplerMoment start, double seconds)
    {
        var frames = sequence.Frames;
        var cycleMs = 0L;
        if (sequence.Loop)
        {
            var duration = frames[^1].EndMilliseconds;
            var cycle = (long)((seconds - start.Seconds) * 1000 / duration);
            cycleMs = cycle * duration;
            // The quotient is only an estimate. Correct it against rounded absolute boundaries.
            if (cycle > 0 && seconds < Boundary(start, cycleMs)) cycleMs -= duration;
            else if (seconds >= Boundary(start, cycleMs + duration)) cycleMs += duration;
        }
        var lo = 0;
        var hi = frames.Length - 1;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (seconds >= Boundary(start, cycleMs + frames[mid].EndMilliseconds)) lo = mid + 1;
            else hi = mid;
        }
        return new(lo, cycleMs);
    }

    internal static double Boundary(in SamplerMoment start, long relativeMilliseconds)
    {
        if (relativeMilliseconds == 0) return start.Seconds;
        if (start.Milliseconds >= 0) return (start.Milliseconds + relativeMilliseconds) / 1000d;
        var anchor = start.SequenceAnchor!;
        var offset = relativeMilliseconds / 1000d;
        var sum = start.Seconds + offset;
        var z = sum - start.Seconds;
        var sumError = (start.Seconds - (sum - z)) + (offset - z);
        var offsetError = Math.FusedMultiplyAdd(-offset, 1000d, relativeMilliseconds) / 1000d;
        var correction = sumError + anchor.Residual + offsetError;
        // TwoSum is exact here; the remaining roundoffs are small relative to the input
        // corrections (plus subnormal rounding). If that uncertainty crosses a rounding
        // midpoint, resolve from the exact anchor so tiny decisive bits are not lost.
        var uncertainty = Math.ScaleB(Math.Abs(sumError) + Math.Abs(anchor.Residual) + Math.Abs(offsetError), -48) +
            8 * double.Epsilon;
        return sum + (correction - uncertainty) == sum + (correction + uncertainty)
            ? sum + correction
            : (anchor.ExactSeconds + ExactBarTime.FromFraction(relativeMilliseconds, 1000)).ToDouble();
    }

    internal readonly record struct FramePosition(int Index, long CycleStartMilliseconds)
    {
        internal long Ordinal(SamplerFrame[] frames) => CycleStartMilliseconds / frames[^1].EndMilliseconds * frames.Length + Index;
    }
}
