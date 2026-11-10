namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>One-axis screen-space magnet with acquisition/release hysteresis; caller filters candidates.</summary>
public sealed class ObjectMagnet
{
    public const double AcquirePixels = 6;
    public const double ReleasePixels = 10;
    public readonly record struct Candidate(int Id, int Feature, double Position, double Value);
    private (int Id, int Feature)? _held;
    public Candidate? Resolve(double position, IReadOnlyList<Candidate> candidates)
    {
        if (_held is { } held)
            foreach (var candidate in candidates)
                if (candidate.Id == held.Id && candidate.Feature == held.Feature && Math.Abs(candidate.Position - position) <= ReleasePixels)
                    return candidate;
        _held = null;
        Candidate? best = null;
        var distance = AcquirePixels;
        foreach (var candidate in candidates)
        {
            var delta = Math.Abs(candidate.Position - position);
            if (delta > distance || best is not null && delta == distance) continue;
            best = candidate;
            distance = delta;
        }
        if (best is { } chosen) _held = (chosen.Id, chosen.Feature);
        return best;
    }
    public void Reset() => _held = null;
}
