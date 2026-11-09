using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Game;

/// <summary>Immutable per-track activity intervals. Sampling is seek-safe and independent of hits/FPS.</summary>
internal sealed class JudgmentLineTimeline
{
    internal readonly record struct Activity(Track Track, double Start, double End);
    private readonly Activity[][] _tracks;
    public const double DarkLevel = 0.14;
    public const double IdleGap = 10;
    public const double LeadSeconds = 3;
    public const double FadeSeconds = 1.5;

    public JudgmentLineTimeline(IEnumerable<Activity> activities)
    {
        var all = activities.Where(a => double.IsFinite(a.Start) && double.IsFinite(a.End) && a.End >= a.Start).ToArray();
        _tracks = new Activity[3][];
        for (var track = 0; track < 3; track++)
        {
            var merged = new List<Activity>();
            foreach (var item in all.Where(a => (int)a.Track == track).OrderBy(a => a.Start))
            {
                if (merged.Count > 0 && item.Start - merged[^1].End < IdleGap)
                    merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, item.End) };
                else merged.Add(item);
            }
            _tracks[track] = merged.ToArray();
        }
    }

    public static JudgmentLineTimeline FromPlan(JudgePlan.Plan plan) => new(
        plan.Units.Where(u => u.SustainHeadId is null).Select(u => new Activity(u.Track, u.Time, u.Time))
            .Concat(plan.Sustains.Values.Select(p => new Activity(p.Track, p.StartTime, p.EndTime))));

    public double Sample(Track track, double time, UiMotionMode motion)
    {
        if (!double.IsFinite(time)) return DarkLevel;
        var items = _tracks[(int)track];
        // First interval whose end has not passed; no per-frame allocations or full-chart scan.
        var lo = 0;
        var hi = items.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (items[mid].End < time) lo = mid + 1; else hi = mid;
        }
        if (lo < items.Length)
        {
            var next = items[lo];
            if (time >= next.Start) return 1;
            var age = time - (next.Start - LeadSeconds);
            if (age >= 0)
            {
                if (motion == UiMotionMode.Off || age >= 0.72) return 1;
                if (motion == UiMotionMode.Reduced)
                    return DarkLevel + (1 - DarkLevel) * Math.Clamp(age / 0.6, 0, 1);
                // Two restrained flashes, then steady: 180ms lit / 180ms dim.
                return (int)(age / 0.18) % 2 == 0 ? 1 : DarkLevel;
            }
        }
        if (lo == 0 || motion == UiMotionMode.Off) return DarkLevel;
        var fade = Math.Clamp((time - items[lo - 1].End) / FadeSeconds, 0, 1);
        var smooth = fade * fade * (3 - 2 * fade);
        return 1 - (1 - DarkLevel) * smooth;
    }
}
