namespace DynamiteUniverse.Shared.Judge;

/// <summary>加载期视觉对象池容量的硬边界。</summary>
public static class VisualPoolPlanning
{
    public const int PoolMinimumCapacity = 8;
    public const int PoolMaximumCapacity = 32;
    public const double BloomLifetimeSeconds = 0.36;
    public const double HitParticleLifetimeSeconds = 0.62;
    public const double HitParticleRecycleTailSeconds = 0.12;
    public const double EffectWindowSeconds =
        HitParticleLifetimeSeconds + HitParticleRecycleTailSeconds;
    public const double NoteActivityWindowSeconds = 1.54;

    public readonly record struct ActivityInterval(double Start, double End);

    public readonly record struct PeakCounts(int PeakHits, int PeakNotes, int PeakSustains)
    {
        public int HitCapacity => ClampCapacity(PeakHits);
        public int NoteCapacity => ClampCapacity(PeakNotes);
        public int SustainCapacity => ClampCapacity(PeakSustains);
    }

    public static int ClampCapacity(int peak) =>
        Math.Clamp(peak, PoolMinimumCapacity, PoolMaximumCapacity);

    public static PeakCounts Analyze(
        IEnumerable<double> hitTimes,
        IEnumerable<ActivityInterval> noteIntervals,
        IEnumerable<ActivityInterval> sustainIntervals,
        double effectWindowSeconds = EffectWindowSeconds)
    {
        ArgumentNullException.ThrowIfNull(hitTimes);
        ArgumentNullException.ThrowIfNull(noteIntervals);
        ArgumentNullException.ThrowIfNull(sustainIntervals);
        if (!double.IsFinite(effectWindowSeconds) || effectWindowSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(effectWindowSeconds));

        var hits = hitTimes.Where(double.IsFinite).OrderBy(static t => t).ToArray();
        var peakHits = PeakOverWindow(hits, effectWindowSeconds);
        var peakNotes = PeakOverIntervals(noteIntervals);
        var peakSustains = PeakOverIntervals(sustainIntervals);
        return new PeakCounts(peakHits, peakNotes, peakSustains);
    }

    public static PeakCounts Analyze(
        IEnumerable<JudgeUnit> units,
        IEnumerable<DynamiteUniverse.Shared.Chart.Note> notes,
        IEnumerable<SustainPath> sustains,
        double effectWindowSeconds = EffectWindowSeconds,
        double noteActivityWindowSeconds = NoteActivityWindowSeconds)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(sustains);
        if (!double.IsFinite(noteActivityWindowSeconds) || noteActivityWindowSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(noteActivityWindowSeconds));

        return Analyze(
            units.Select(static unit => unit.Time),
            notes.Select(note => new ActivityInterval(note.Second,
                note.Second + noteActivityWindowSeconds)),
            sustains.Select(static path => new ActivityInterval(path.StartTime, path.EndTime)),
            effectWindowSeconds);
    }

    public readonly record struct TypedActivity(
        DynamiteUniverse.Shared.Chart.NoteType Type, ActivityInterval Interval);

    public static DynamiteUniverse.Shared.Chart.NoteType PoolType(
        DynamiteUniverse.Shared.Chart.NoteType type) => type switch
    {
        DynamiteUniverse.Shared.Chart.NoteType.HoldNode => DynamiteUniverse.Shared.Chart.NoteType.HoldHead,
        DynamiteUniverse.Shared.Chart.NoteType.MixerNode => DynamiteUniverse.Shared.Chart.NoteType.MixerHead,
        _ => type,
    };

    public static IReadOnlyDictionary<DynamiteUniverse.Shared.Chart.NoteType, int> PeaksByType(
        IEnumerable<TypedActivity> activities) => activities
        .GroupBy(activity => PoolType(activity.Type))
        .ToDictionary(group => group.Key,
            group => PeakOverIntervals(group.Select(activity => activity.Interval)));

    public static int PeakOverWindow(IReadOnlyList<double> sortedTimes, double windowSeconds)
    {
        if (sortedTimes.Count == 0)
            return 0;
        var peak = 0;
        var start = 0;
        for (var end = 0; end < sortedTimes.Count; end++)
        {
            while (sortedTimes[end] - sortedTimes[start] > windowSeconds)
                start++;
            peak = Math.Max(peak, end - start + 1);
        }
        return peak;
    }

    public static int PeakOverIntervals(IEnumerable<ActivityInterval> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        var events = new List<(double Time, int Delta)>();
        foreach (var interval in intervals)
        {
            if (!double.IsFinite(interval.Start) || !double.IsFinite(interval.End))
                continue;
            var start = Math.Min(interval.Start, interval.End);
            var end = Math.Max(interval.Start, interval.End);
            events.Add((start, 1));
            events.Add((end, -1));
        }
        var active = 0;
        var peak = 0;
        foreach (var group in events.OrderBy(static e => e.Time).GroupBy(static e => e.Time))
        {
            // Starts are counted before ends at the same instant: both objects can
            // legitimately coexist during a frame boundary.
            active += group.Where(static e => e.Delta > 0).Sum(static e => e.Delta);
            peak = Math.Max(peak, active);
            active += group.Where(static e => e.Delta < 0).Sum(static e => e.Delta);
        }
        return peak;
    }
}

/// <summary>无 Godot 依赖的固定容量池；满池时复用最早借出的项目。</summary>
public sealed class StealablePool<T> where T : class
{
    private sealed class Entry
    {
        public required T Value;
        public long LeaseOrder;
        public bool InUse;
    }

    private readonly int _capacity;
    private readonly Func<T> _factory;
    private readonly Action<T>? _reset;
    private readonly List<Entry> _entries = new();
    private long _nextLeaseOrder;

    public StealablePool(int capacity, Func<T> factory, Action<T>? reset = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _reset = reset;
    }

    public int Capacity => _capacity;
    public int Count => _entries.Count;
    public int InUseCount => _entries.Count(static entry => entry.InUse);

    public T Acquire()
    {
        var free = _entries.FirstOrDefault(static entry => !entry.InUse);
        if (free is null)
        {
            if (_entries.Count < _capacity)
            {
                free = new Entry { Value = _factory() };
                _entries.Add(free);
            }
            else
            {
                free = _entries.Where(static entry => entry.InUse)
                    .OrderBy(static entry => entry.LeaseOrder).First();
                _reset?.Invoke(free.Value);
            }
        }
        free.InUse = true;
        free.LeaseOrder = _nextLeaseOrder++;
        return free.Value;
    }

    public bool Release(T value)
    {
        var entry = _entries.FirstOrDefault(entry => ReferenceEquals(entry.Value, value));
        if (entry is null || !entry.InUse)
            return false;
        entry.InUse = false;
        _reset?.Invoke(entry.Value);
        return true;
    }

    public bool IsInUse(T value) =>
        _entries.Any(entry => ReferenceEquals(entry.Value, value) && entry.InUse);

    public void ResetAll()
    {
        foreach (var entry in _entries)
        {
            entry.InUse = false;
            _reset?.Invoke(entry.Value);
        }
    }
}
