namespace DynamiteUniverse.Shared.Storyboard;

/// <summary>Walks only the compiled texture-bearing extents; no sampling or rendering state is touched.</summary>
internal sealed class StoryboardUsageBuilder
{
    private readonly Dictionary<StoryboardImage, List<StoryboardTextureInterval>> _images = [];
    private int _remainingSequenceSpans = StoryboardLimits.MaxExpandedSequenceSpans;

    internal static IReadOnlyList<StoryboardImageUsage> Build(SamplerRoot[] roots, double mediaDurationSeconds)
    {
        if (!double.IsFinite(mediaDurationSeconds) || mediaDurationSeconds <= 0) return [];
        var builder = new StoryboardUsageBuilder();
        foreach (var root in roots)
            if (root.Node.TextureUseCost > 0)
                builder.Visit(root.Node, root.Moments, 0, mediaDurationSeconds);
        return Array.AsReadOnly(builder._images.OrderBy(pair => pair.Key.AssetId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Crop.HasValue).ThenBy(pair => pair.Key.Crop?.X).ThenBy(pair => pair.Key.Crop?.Y)
            .ThenBy(pair => pair.Key.Crop?.Width).ThenBy(pair => pair.Key.Crop?.Height)
            .Select(pair => new StoryboardImageUsage(pair.Key, Merge(pair.Value))).ToArray());
    }

    internal static IReadOnlyList<StoryboardTextureUsage> GroupByPath(IReadOnlyList<StoryboardImageUsage> images,
        IReadOnlyDictionary<string, string> assetPaths)
    {
        var byPath = new Dictionary<string, List<StoryboardTextureInterval>>(StringComparer.Ordinal);
        foreach (var image in images)
        {
            var path = assetPaths[image.Image.AssetId];
            if (!byPath.TryGetValue(path, out var intervals)) byPath.Add(path, intervals = []);
            intervals.AddRange(image.Intervals);
        }
        return Array.AsReadOnly(byPath.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new StoryboardTextureUsage(pair.Key, Merge(pair.Value))).ToArray());
    }

    private void Visit(SamplerNode node, SamplerTimeTable moments, double from, double until)
    {
        if (node.Windows is { } windows)
        {
            foreach (var window in windows)
            {
                if (window.From >= until) break;
                var windowFrom = Math.Max(from, window.From);
                var windowUntil = Math.Min(until, window.Until);
                if (windowFrom < windowUntil)
                    foreach (var child in node.TextureChildren)
                        Visit(child, window.Moments, windowFrom, windowUntil);
            }
            return;
        }
        if (node.First >= 0)
        {
            from = Math.Max(from, moments[node.First].Seconds);
            until = Math.Min(until, moments[node.Last].Seconds);
            if (from >= until) return;
        }
        // Only explicit active and clip time windows narrow this table. In particular, an
        // offstage, fully transparent or currently invisible sprite never changes prefetch demand.
        if (node.Image is { } image) Add(image, from, until);
        else if (node.Frames is { } sequence) AddSequence(sequence, moments[node.First], from, until);
        else foreach (var child in node.TextureChildren) Visit(child, moments, from, until);
    }

    private void AddSequence(SamplerFrames sequence, in SamplerMoment start, double from, double until)
    {
        if (sequence.DistinctImages.Length == 1 || _remainingSequenceSpans == 0)
        {
            AddConservativeSequence(sequence, from, until);
            return;
        }
        var frames = sequence.Frames;
        var first = StoryboardSequenceTiming.Locate(sequence, start, from);
        var last = StoryboardSequenceTiming.Locate(sequence, start, Math.BitDecrement(until));
        var count = last.Ordinal(frames) - first.Ordinal(frames) + 1;
        if (count > _remainingSequenceSpans)
        {
            AddConservativeSequence(sequence, from, until);
            return;
        }
        _remainingSequenceSpans -= (int)count;
        var index = first.Index;
        var cycleStart = first.CycleStartMilliseconds;
        for (var i = 0; i < count; i++)
        {
            // The frame owning the last representable sample also owns the clipped tail.
            var end = i == count - 1 ? until : StoryboardSequenceTiming.Boundary(start, cycleStart + frames[index].EndMilliseconds);
            if (from < end) Add(frames[index].Image, from, end);
            from = end;
            if (++index == frames.Length)
            {
                index = 0;
                cycleStart += frames[^1].EndMilliseconds;
            }
        }
    }

    private void AddConservativeSequence(SamplerFrames sequence, double from, double until)
    {
        foreach (var image in sequence.DistinctImages) Add(image, from, until);
    }

    private void Add(StoryboardImage image, double from, double until)
    {
        if (!_images.TryGetValue(image, out var intervals)) _images.Add(image, intervals = []);
        intervals.Add(new(from, until));
    }

    private static IReadOnlyList<StoryboardTextureInterval> Merge(List<StoryboardTextureInterval> intervals)
    {
        intervals.Sort(static (left, right) => left.From.CompareTo(right.From));
        var kept = 0;
        for (var i = 0; i < intervals.Count; i++)
        {
            var interval = intervals[i];
            if (kept > 0 && interval.From <= intervals[kept - 1].Until)
            {
                if (interval.Until > intervals[kept - 1].Until)
                    intervals[kept - 1] = intervals[kept - 1] with { Until = interval.Until };
            }
            else intervals[kept++] = interval;
        }
        intervals.RemoveRange(kept, intervals.Count - kept);
        intervals.TrimExcess();
        return intervals.AsReadOnly();
    }
}
