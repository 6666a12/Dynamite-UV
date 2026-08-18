using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Shared.Judge;

/// <summary>Per-contact phase values used by the original manual judge.</summary>
public enum ContactPhase
{
    Began = 1,
    Moved = 2,
    Stationary = 3,
}

/// <summary>A valid touch projected into one track's chart-position coordinate.</summary>
public readonly record struct TouchSample(
    int Id, Track Track, double Position, ContactPhase Phase);

public readonly record struct NoteBounds(double Left, double Right)
{
    public double Center => (Left + Right) * 0.5;
}

/// <summary>Pure touch/phase rules shared by the client and core tests.</summary>
public static class InputJudgeRules
{
    /// <summary>
    /// Returns every candidate covered by the same touch snapshot. Candidates
    /// are evaluated independently; the touch is deliberately not consumed.
    /// </summary>
    public static IReadOnlyList<T> MatchingCandidates<T>(
        IEnumerable<T> candidates,
        TouchSample touch,
        Func<T, Track> track,
        Func<T, NoteBounds> bounds,
        Func<T, bool>? eligible = null,
        double touchWidth = 0.0,
        bool expandByTouchWidth = true,
        Func<T, bool>? expandByTouchWidthFor = null)
    {
        var matches = new List<T>();
        foreach (var candidate in candidates)
        {
            if (track(candidate) != touch.Track ||
                eligible is not null && !eligible(candidate) ||
                !Overlaps(bounds(candidate), touch.Position, touchWidth,
                    expandByTouchWidthFor?.Invoke(candidate) ?? expandByTouchWidth))
                continue;
            matches.Add(candidate);
        }
        return matches;
    }

    public static NoteBounds Bounds(double position, double width)
    {
        var other = position + width;
        return new NoteBounds(Math.Min(position, other), Math.Max(position, other));
    }

    /// <summary>
    /// Ordinary notes and sustain bodies expand by half the touch width on each
    /// side. Mines pass expandByTouchWidth=false and use their exact range.
    /// </summary>
    public static bool Overlaps(NoteBounds bounds, double touchPosition,
        double touchWidth, bool expandByTouchWidth = true)
    {
        var halfTouch = expandByTouchWidth ? Math.Max(0.0, touchWidth) * 0.5 : 0.0;
        return touchPosition >= bounds.Left - halfTouch &&
               touchPosition <= bounds.Right + halfTouch;
    }

    /// <summary>
    /// Chain/Drag: the outer half of the early Prefect window requires a new
    /// press; the inner half and the late side accept phases 1..3.
    /// </summary>
    public static bool AcceptsContactPhase(double noteTime, double currentTime,
        double prefectSecond, ContactPhase phase)
    {
        var noteDelta = noteTime - currentTime;
        if (noteDelta > prefectSecond)
            return false;
        if (noteDelta > prefectSecond * 0.5)
            return phase == ContactPhase.Began;
        return phase is ContactPhase.Began or ContactPhase.Moved or ContactPhase.Stationary;
    }

    /// <summary>
    /// Mine: only active during the early Prefect window. Its outer 3/4 requires
    /// phase 1; the final Prefect/4 accepts phases 1..3.
    /// </summary>
    public static bool AcceptsMinePhase(double noteTime, double currentTime,
        double prefectSecond, ContactPhase phase)
    {
        var noteDelta = noteTime - currentTime;
        if (noteDelta < 0.0 || noteDelta > prefectSecond)
            return false;
        if (noteDelta > prefectSecond * 0.25)
            return phase == ContactPhase.Began;
        return phase is ContactPhase.Began or ContactPhase.Moved or ContactPhase.Stationary;
    }
}
