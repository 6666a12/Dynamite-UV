using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>All layout/hit testing uses chart bars, never seconds, Scroll or playback rate.</summary>
internal readonly record struct TimelineBarWindow(double Start, double End)
{
    internal static TimelineBarWindow Follow(double current, double maximum)
    {
        var length = Math.Min(8, maximum);
        var start = Math.Clamp(current - length / 4, 0, maximum - length);
        return new(start, start + length);
    }

    internal double AtFraction(double f) => Start + Math.Clamp(f, 0, 1) * (End - Start);
    internal double FractionAt(double bar) => End <= Start ? 0 : Math.Clamp((bar - Start) / (End - Start), 0, 1);
}

internal readonly record struct TimelineSpan(double Start, double End);

/// <summary>One track snapshot. Draw separate series for glyphs and sustained coverage. Body ends
/// are monotone after merging overlaps in the same ink/row; a binary search therefore finds
/// a long note crossing the left edge even when thousands of shorter notes followed its head.</summary>
internal sealed class TimelineTrackLayout
{
    // Normal-sized chords get a distinct row per voice. Up to 33 subrows before coalescing
    // ink on the same raster row; a dense chord never spills its extra heads onto a later beat.
    private const int LastBand = 32;
    // Stable slots: a solo starts in the middle; voices 2 and 3 sit either side with
    // space for their glyphs. Only use the intervening bands for unusually large chords.
    private static readonly int[] Bands = BuildBands();
    private static int[] BuildBands()
    {
        var result = new List<int>(LastBand + 1) { 16, 8, 24, 0, LastBand };
        for (int gap = 4; gap > 0; gap /= 2)
            for (int i = gap; i < LastBand; i += gap * 2) result.Add(i);
        return result.ToArray();
    }
    internal sealed class Series(V2NoteType type, int band)
    {
        internal V2NoteType Type { get; } = type;
        internal double RelativeY => band / (double)LastBand;
        internal readonly List<double> Heads = [], Judgements = [], Tails = [];
        internal readonly List<TimelineSpan> Bodies = [];

        internal void Add(TimelineRhythmNote note)
        {
            Heads.Add(note.Start);
            if (note.Type is not (V2NoteType.Hold or V2NoteType.Mixer) || note.End <= note.Start) return;
            if (Bodies.Count != 0 && Bodies[^1].End >= note.Start)
                Bodies[^1] = new(Bodies[^1].Start, Math.Max(Bodies[^1].End, note.End));
            else Bodies.Add(new(note.Start, note.End));
            Tails.Add(note.End);
            foreach (var bar in note.Judgements)
                if (bar >= note.Start && bar <= note.End && double.IsFinite(bar)) Judgements.Add(bar);
        }
    }

    internal Series[] Lanes { get; }
    internal TimelineTrackLayout(IReadOnlyList<TimelineRhythmNote> sorted)
    {
        var series = new Dictionary<(V2NoteType, int), Series>();
        var occupied = new PriorityQueue<int, double>();
        var free = new PriorityQueue<int, int>();
        var nextLane = 0;
        foreach (var note in sorted)
        {
            if (note.Type == V2NoteType.BarLine || !double.IsFinite(note.Start) || !double.IsFinite(note.End)) continue;
            while (occupied.TryPeek(out var previous, out var end) && end < note.Start)
            { occupied.Dequeue(); free.Enqueue(previous, previous); }
            var lane = free.TryDequeue(out var reused, out _) ? reused : nextLane++;
            occupied.Enqueue(lane, Math.Max(note.Start, note.End));
            var band = Bands[lane % Bands.Length];
            var key = (note.Type, band);
            if (!series.TryGetValue(key, out var ink)) series.Add(key, ink = new(note.Type, band));
            ink.Add(note);
        }
        Lanes = series.Values.ToArray();
        foreach (var lane in Lanes) { lane.Judgements.Sort(); lane.Tails.Sort(); }
    }

    internal static int LowerBound(IReadOnlyList<double> sorted, double bar, int lo = 0)
    {
        var hi = sorted.Count;
        while (lo < hi) { var m = lo + (hi - lo) / 2; if (sorted[m] < bar) lo = m + 1; else hi = m; }
        return lo;
    }

    internal static int FirstBody(IReadOnlyList<TimelineSpan> spans, double start, int lo = 0)
    {
        var hi = spans.Count;
        while (lo < hi) { var m = lo + (hi - lo) / 2; if (spans[m].End < start) lo = m + 1; else hi = m; }
        return lo;
    }

    internal static int[] Density(IReadOnlyList<double> hits, double maximum, int bins)
    {
        var density = new int[bins];
        if (bins == 0 || hits.Count == 0) return density;
        var first = LowerBound(hits, 0);
        for (var i = 0; i < bins; i++)
        {
            var end = maximum * ((i + 1d) / bins);
            // Include an onset at the song end in the last bin.
            var next = LowerBound(hits, i == bins - 1 ? Math.BitIncrement(end) : end, first);
            density[i] = next - first; first = next;
        }
        return density;
    }
}
