using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>v2 metadata retained on each flattened legacy runtime Note.</summary>
public sealed record V2RuntimeNoteMetadata
{
    public required string SourceId { get; init; }
    public required string ParentSourceId { get; init; }
    public required ExactBarTime ExactTime { get; init; }
    public required V2NoteType SourceType { get; init; }
    public required bool IsPathNode { get; init; }
    public V2PathCurve? CurveToNext { get; init; }
    /// <summary>Resolved Hold judgement state; null for non-Hold points.</summary>
    public bool? HoldJudge { get; init; }
}

/// <summary>v2 metadata retained at runtime for exact timing, scroll, and path evaluation.</summary>
public sealed record V2RuntimeChartMetadata
{
    public required V2Chart SourceChart { get; init; }
    public required V2BpmTimeline BpmTimeline { get; init; }
    public required V2ScrollMap ScrollMap { get; init; }
    public required IReadOnlyDictionary<int, string> SourceIdsByRuntimeId { get; init; }
    public required IReadOnlyDictionary<string, int> RuntimeIdsBySourceId { get; init; }
    public required IReadOnlyDictionary<int, V2PathEvaluator> PathsByRuntimeHeadId { get; init; }
    public required IReadOnlySet<int> SyncAccentRuntimeIds { get; init; }
}

/// <summary>Result of projecting a semantic v2 chart into the existing runtime Chart/Note graph.</summary>
public sealed record V2RuntimeAdapterResult
{
    public required DynamiteUniverse.Shared.Chart.Chart RuntimeChart { get; init; }
    public required V2RuntimeChartMetadata Metadata { get; init; }
    public IReadOnlyDictionary<int, string> SourceIdsByRuntimeId => Metadata.SourceIdsByRuntimeId;
    public IReadOnlyDictionary<string, int> RuntimeIdsBySourceId => Metadata.RuntimeIdsBySourceId;
    public IReadOnlySet<int> SyncAccentRuntimeIds => Metadata.SyncAccentRuntimeIds;
}

/// <summary>Derives exact-time, cross-track Tap sync accents; no state is serialized.</summary>
public static class V2SyncAccentDeriver
{
    public static IReadOnlySet<string> DeriveSourceIds(V2Chart chart)
    {
        var groups = new Dictionary<ExactBarTime, List<(Track Track, V2Note Note)>>();
        foreach (var item in chart.AllNotes)
        {
            if (item.Note.Type is not (V2NoteType.Tap or V2NoteType.ExTap or
                V2NoteType.Hold or V2NoteType.Mixer))
                continue;
            if (!groups.TryGetValue(item.Note.Time, out var group))
                groups[item.Note.Time] = group = [];
            group.Add(item);
        }

        var accented = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups.Values)
        {
            if (group.Select(item => item.Track).Distinct().Take(2).Count() < 2)
                continue;
            foreach (var (_, note) in group)
            {
                if (note.Type == V2NoteType.Tap)
                    accented.Add(note.Id);
            }
        }
        return accented;
    }

    public static IReadOnlySet<int> DeriveRuntimeIds(
        V2Chart chart, IReadOnlyDictionary<string, int> runtimeIdsBySourceId)
    {
        var result = new HashSet<int>();
        foreach (var sourceId in DeriveSourceIds(chart))
        {
            if (runtimeIdsBySourceId.TryGetValue(sourceId, out var runtimeId))
                result.Add(runtimeId);
        }
        return result;
    }
}

/// <summary>
/// Flattens v2 Hold/Mixer paths into the existing Note/SubNoteId representation while preserving
/// exact source semantics in optional metadata. Coordinates are converted to legacy left edge using
/// <c>center-width/2</c> without clamping, so overscan and center-track Mixers remain legal.
/// </summary>
public static class V2RuntimeAdapter
{
    public static V2RuntimeAdapterResult Adapt(
        V2Chart chart, string? title = null, int difficulty = 0)
    {
        V2SemanticValidator.ValidateChart(chart);
        var timeline = new V2BpmTimeline(chart.Bpms, chart.AudioOffsetSec);
        var scroll = new V2ScrollMap(chart.ScrollSpeeds);
        var sourceIdsByRuntimeId = new Dictionary<int, string>();
        var runtimeIdsBySourceId = new Dictionary<string, int>(StringComparer.Ordinal);

        // String IDs are assigned by ordinal lexical order. This is deterministic across array
        // permutations and preserves stable identities as long as source IDs themselves remain stable.
        var allIds = chart.AllNotes
            .SelectMany(item => item.Note is V2PathNote path
                ? new[] { item.Note.Id }.Concat(path.Nodes.Select(node => node.Id))
                : [item.Note.Id])
            .Order(StringComparer.Ordinal)
            .ToArray();
        for (var i = 0; i < allIds.Length; i++)
        {
            var runtimeId = checked(i + 1);
            runtimeIdsBySourceId.Add(allIds[i], runtimeId);
            sourceIdsByRuntimeId.Add(runtimeId, allIds[i]);
        }

        var paths = new Dictionary<int, V2PathEvaluator>();
        var nextRuntimeId = allIds.Length + 1;
        var notesLeft = FlattenTrack(chart.NotesLeft, Track.Left, timeline,
            runtimeIdsBySourceId, sourceIdsByRuntimeId, paths, ref nextRuntimeId);
        var notesCenter = FlattenTrack(chart.NotesCenter, Track.Center, timeline,
            runtimeIdsBySourceId, sourceIdsByRuntimeId, paths, ref nextRuntimeId);
        var notesRight = FlattenTrack(chart.NotesRight, Track.Right, timeline,
            runtimeIdsBySourceId, sourceIdsByRuntimeId, paths, ref nextRuntimeId);
        var accents = V2SyncAccentDeriver.DeriveRuntimeIds(chart, runtimeIdsBySourceId);
        var metadata = new V2RuntimeChartMetadata
        {
            SourceChart = chart,
            BpmTimeline = timeline,
            ScrollMap = scroll,
            SourceIdsByRuntimeId = sourceIdsByRuntimeId,
            RuntimeIdsBySourceId = runtimeIdsBySourceId,
            PathsByRuntimeHeadId = paths,
            SyncAccentRuntimeIds = accents,
        };

        var runtime = new DynamiteUniverse.Shared.Chart.Chart
        {
            Name = chart.ChartId,
            Title = title ?? chart.ChartId,
            Difficulty = difficulty,
            TotalMainNote = ToRuntimeCount(V2SemanticValidator.CountMainJudgements(chart)),
            Sections = chart.Bpms.Select(bpm => new BarSection
            {
                Bpm = bpm.Bpm,
                BarTime = bpm.Time.ToDouble(),
                Seconds = timeline.ToSeconds(bpm.Time),
            }).ToArray(),
            NotesLeft = notesLeft,
            NotesCenter = notesCenter,
            NotesRight = notesRight,
            DropSpeeds = chart.ScrollSpeeds.Count == 0
                ? [(0.0, 1.0)]
                : chart.ScrollSpeeds.Select(item => (item.Time.ToDouble(), item.Value)).ToArray(),
            V2Metadata = metadata,
        };
        return new V2RuntimeAdapterResult
        {
            RuntimeChart = runtime,
            Metadata = metadata,
        };
    }

    private static IReadOnlyList<Note> FlattenTrack(IReadOnlyList<V2Note> source,
        Track track, V2BpmTimeline timeline, IReadOnlyDictionary<string, int> ids,
        Dictionary<int, string> sourceIdsByRuntimeId,
        Dictionary<int, V2PathEvaluator> paths, ref int nextRuntimeId)
    {
        var result = new List<Note>();
        foreach (var note in source)
        {
            var headId = ids[note.Id];
            if (note is not V2PathNote path)
            {
                result.Add(RuntimeNote(note.Id, note.Id, headId, -1,
                    RuntimeType(note.Type, pathNode: false), track, note.Time,
                    note.Center, note.Width, timeline, note.Type, false, null, null));
                continue;
            }

            paths.Add(headId, new V2PathEvaluator(path));
            var flattenedPoints = FlattenPathPoints(path, track, ids, sourceIdsByRuntimeId,
                ref nextRuntimeId);
            var nextId = flattenedPoints[0].RuntimeId;
            result.Add(RuntimeNote(path.Id, path.Id, headId, nextId,
                RuntimeType(path.Type, pathNode: false), track, path.Time,
                path.Center, path.Width, timeline, path.Type, false,
                path.CurveToNext ?? V2PathCurve.Linear,
                path.Type == V2NoteType.Hold ? true : null));
            for (var i = 0; i < flattenedPoints.Count; i++)
            {
                var point = flattenedPoints[i];
                var subId = i + 1 < flattenedPoints.Count
                    ? flattenedPoints[i + 1].RuntimeId : -1;
                result.Add(RuntimeNote(point.SourceId, path.Id, point.RuntimeId, subId,
                    RuntimeType(path.Type, pathNode: true), track, point.Time,
                    point.Center, point.Width, timeline, path.Type, true,
                    point.CurveToNext, point.HoldJudge));
            }
        }
        result.Sort(static (left, right) =>
        {
            var comparison = left.Second.CompareTo(right.Second);
            return comparison != 0 ? comparison : left.Id.CompareTo(right.Id);
        });
        return result;
    }

    private readonly record struct FlattenedPathPoint(
        int RuntimeId, string SourceId, ExactBarTime Time, double Center, double Width,
        V2PathCurve? CurveToNext, bool? HoldJudge);

    private static IReadOnlyList<FlattenedPathPoint> FlattenPathPoints(V2PathNote path,
        Track track, IReadOnlyDictionary<string, int> ids,
        Dictionary<int, string> sourceIdsByRuntimeId, ref int nextRuntimeId)
    {
        var evaluator = new V2PathEvaluator(path);
        var result = new List<FlattenedPathPoint>();
        var leftTime = path.Time;
        for (var nodeIndex = 0; nodeIndex < path.Nodes.Count; nodeIndex++)
        {
            var node = path.Nodes[nodeIndex];
            var curve = nodeIndex == 0
                ? path.CurveToNext ?? V2PathCurve.Linear
                : path.Nodes[nodeIndex - 1].CurveToNext ?? V2PathCurve.Linear;
            if (curve != V2PathCurve.Linear)
            {
                var subdivisions = RequiredSubdivisions(
                    evaluator, leftTime, node.Time, track);
                for (var step = 1; step < subdivisions; step++)
                {
                    var fraction = ExactBarTime.FromFraction(step, subdivisions);
                    var time = leftTime + (node.Time - leftTime) * fraction;
                    var sample = evaluator.Evaluate(time);
                    var runtimeId = checked(nextRuntimeId++);
                    var sourceId = $"{path.Id}.__shape.{nodeIndex}.{step}";
                    sourceIdsByRuntimeId.Add(runtimeId, sourceId);
                    result.Add(new FlattenedPathPoint(runtimeId, sourceId, time,
                        sample.Center, sample.Width, V2PathCurve.Linear,
                        path.Type == V2NoteType.Hold ? false : null));
                }
            }
            result.Add(new FlattenedPathPoint(ids[node.Id], node.Id, node.Time,
                node.Center, node.Width, node.CurveToNext, node.Judge));
            leftTime = node.Time;
        }
        return result;
    }

    private static int RequiredSubdivisions(V2PathEvaluator evaluator,
        ExactBarTime start, ExactBarTime end, Track track)
    {
        const int maxSegments = 256;
        const double tolerancePx = 0.5;
        var positionScale = track == Track.Center ? 273.2 : 115.0;
        var widthScale = track == Track.Center ? 273.2 : 102.0;
        for (var segments = 2; segments <= maxSegments; segments *= 2)
        {
            if (SubdivisionErrorPx(evaluator, start, end, segments,
                    positionScale, widthScale) <= tolerancePx)
                return segments;
        }
        return maxSegments;
    }

    private static double SubdivisionErrorPx(V2PathEvaluator evaluator,
        ExactBarTime start, ExactBarTime end, int segments,
        double positionScale, double widthScale)
    {
        var maxError = 0.0;
        for (var segment = 0; segment < segments; segment++)
        {
            var leftTime = start + (end - start) *
                ExactBarTime.FromFraction(segment, segments);
            var rightTime = start + (end - start) *
                ExactBarTime.FromFraction(segment + 1, segments);
            var midpointTime = start + (end - start) *
                ExactBarTime.FromFraction(2 * segment + 1, 2 * segments);
            var left = evaluator.Evaluate(leftTime);
            var right = evaluator.Evaluate(rightTime);
            var midpoint = evaluator.Evaluate(midpointTime);
            var centerError = Math.Abs(midpoint.Center -
                (left.Center + right.Center) / 2.0) * positionScale;
            var widthError = Math.Abs(midpoint.Width -
                (left.Width + right.Width) / 2.0) * widthScale / 2.0;
            maxError = Math.Max(maxError, centerError + widthError);
        }
        return maxError;
    }

    private static Note RuntimeNote(string sourceId, string parentSourceId, int runtimeId,
        int subId, NoteType type, Track track, ExactBarTime time, double center, double width,
        V2BpmTimeline timeline, V2NoteType sourceType, bool pathNode,
        V2PathCurve? curve, bool? holdJudge)
    {
        var second = timeline.ToSeconds(time);
        return new Note
        {
            Id = runtimeId,
            SubNoteId = subId,
            Type = type,
            Track = track,
            BarTime = time.ToDouble(),
            Position = center - width / 2.0,
            Width = width,
            Second = second,
            BakedSecond = second,
            SyncNote = 0,
            V2Metadata = new V2RuntimeNoteMetadata
            {
                SourceId = sourceId,
                ParentSourceId = parentSourceId,
                ExactTime = time,
                SourceType = sourceType,
                IsPathNode = pathNode,
                CurveToNext = curve,
                HoldJudge = holdJudge,
            },
        };
    }

    private static NoteType RuntimeType(V2NoteType type, bool pathNode) => type switch
    {
        V2NoteType.Tap => NoteType.Tap,
        V2NoteType.Drag => NoteType.Drag,
        V2NoteType.ExTap => NoteType.ExTap,
        V2NoteType.Hold => pathNode ? NoteType.HoldNode : NoteType.HoldHead,
        V2NoteType.Mixer => pathNode ? NoteType.MixerNode : NoteType.MixerHead,
        V2NoteType.Mine => NoteType.Mine,
        V2NoteType.BarLine => NoteType.BarLine,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static int ToRuntimeCount(System.Numerics.BigInteger count)
    {
        if (count > int.MaxValue)
            throw new InvalidOperationException(
                "v2 main judgement count exceeds the legacy runtime integer capacity.");
        return (int)count;
    }
}
