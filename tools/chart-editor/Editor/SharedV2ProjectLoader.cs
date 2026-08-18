using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Editor;

/// <summary>Application adapter that keeps Avalonia snapshots separate from the editable core document.</summary>
public sealed class SharedV2ProjectLoader
{
    private const double PathScreenError = 0.5;
    private const int PathSubdivisionDepth = 12;

    public EditorDocument Open(string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
            throw new ArgumentException("Choose a versioned clean-room pack directory or its meta.json.");

        var fullPath = Path.GetFullPath(requestedPath.Trim().Trim('"'));
        var root = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("The pack root could not be resolved.");
        if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "meta.json")))
            throw new FileNotFoundException("The selected pack does not contain meta.json.", Path.Combine(root, "meta.json"));
        RejectDevelopmentTestdata(root);
        return EditorPackageRepository.Open(root);
    }

    public static EditorProject Snapshot(EditorDocument document, double? audioDurationSec = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.PackageDirectory ?? string.Empty;
        return new EditorProject
        {
            RootPath = root,
            PackId = document.PackId,
            Revision = document.Revision,
            Title = document.Title,
            Artist = document.Artist,
            MetaFile = string.IsNullOrEmpty(root) ? string.Empty : Path.Combine(root, "meta.json"),
            Charts = document.Charts.Select(chart => SnapshotChart(document, chart, audioDurationSec)).ToArray(),
        };
    }

    private static EditorChartDocument SnapshotChart(EditorDocument document, EditableChart chart,
        double? audioDurationSec)
    {
        var chartSnapshot = document.BuildChartSnapshot(chart.Id);
        var timeline = new V2BpmTimeline(chartSnapshot.Bpms, chartSnapshot.AudioOffsetSec);
        var scroll = new V2ScrollMap(chartSnapshot.ScrollSpeeds);
        var notes = chartSnapshot.AllNotes
            .Select(pair => AdaptNote(pair.Track, pair.Note, timeline))
            .OrderBy(note => note.Bar)
            .ThenBy(note => note.Track)
            .ThenBy(note => note.Id, StringComparer.Ordinal)
            .ToArray();
        var bpms = chartSnapshot.Bpms
            .Select(bpm => new EditorBpmEvent(
                bpm.Time.ToString(), bpm.Time.ToDouble(), timeline.ToSeconds(bpm.Time),
                bpm.Bpm, bpm.Time.ToString()))
            .ToArray();
        var scrolls = chartSnapshot.ScrollSpeeds
            .Select(item => new EditorScrollEventModel(
                item.Time.ToString(), item.Time.ToDouble(), timeline.ToSeconds(item.Time),
                item.Value, item.CurveToNext, item.Time.ToString()))
            .ToArray();
        var lastSecond = notes.Length == 0
            ? chartSnapshot.AudioOffsetSec
            : notes.Max(note => note.EndSecond);
        var entry = document.BuildPackSnapshot().Charts.Single(item => item.Id == chart.Id);
        var resolvedAudio = entry.Audio ?? document.Audio ?? "";
        var resolvedAudioPath = string.IsNullOrEmpty(document.PackageDirectory) || string.IsNullOrEmpty(resolvedAudio)
            ? resolvedAudio
            : Path.GetFullPath(Path.Combine(document.PackageDirectory, resolvedAudio.Replace('/', Path.DirectorySeparatorChar)));
        var judgementCount = V2SemanticValidator.CountMainJudgements(chartSnapshot);
        return new EditorChartDocument
        {
            Id = chart.Id,
            Difficulty = chart.Difficulty.ToString().ToLowerInvariant(),
            DifficultyDisplay = chart.Difficulty == V2Difficulty.Custom
                ? chart.DifficultyKey ?? "custom"
                : chart.Difficulty.ToString().ToUpperInvariant(),
            Level = chart.Level,
            Charters = chart.Charters.ToArray(),
            SourceFile = chart.File,
            ResolvedAudio = resolvedAudio,
            ResolvedAudioPath = resolvedAudioPath,
            AudioOffsetSec = chart.AudioOffsetSec,
            DurationSec = audioDurationSec is > 0
                ? audioDurationSec.Value
                : Math.Max(8.0, lastSecond + 2.0),
            MainJudgementCount = judgementCount > long.MaxValue ? long.MaxValue : (long)judgementCount,
            Bpms = bpms,
            Scrolls = scrolls,
            Notes = notes,
            SecondsToBar = second => timeline.ToBarTime(second).ToDouble(),
            BarToSeconds = bar => timeline.ToSeconds(ExactBarTime.FromDouble(Math.Max(0, bar))),
            ScrollSpeedAtBar = bar => scroll.SpeedAt(ExactBarTime.FromDouble(Math.Max(0, bar))),
        };
    }

    private static EditorNoteModel AdaptNote(Track track, V2Note note, V2BpmTimeline timeline)
    {
        var path = note is V2PathNote pathNote
            ? SamplePath(pathNote, timeline)
            : [new EditorPathPoint(note.Time.ToDouble(), timeline.ToSeconds(note.Time), note.Center, note.Width)];
        var nodes = note is V2PathNote editablePath
            ? editablePath.Nodes.Select((node, index) => new EditorPathNodeModel(
                node.Id, node.Time.ToDouble(), timeline.ToSeconds(node.Time), node.Center, node.Width,
                node.CurveToNext, node.Judge, node.Time.ToString(), index + 1 == editablePath.Nodes.Count)).ToArray()
            : [];
        return new EditorNoteModel
        {
            Id = note.Id,
            Type = note.Type,
            Track = track switch
            {
                Track.Left => EditorTrack.Left,
                Track.Center => EditorTrack.Center,
                _ => EditorTrack.Right,
            },
            Bar = note.Time.ToDouble(),
            Second = timeline.ToSeconds(note.Time),
            Center = note.Center,
            Width = note.Width,
            ExactTime = note.Time.ToString(),
            CurveToNext = note is V2PathNote curvePath ? curvePath.CurveToNext : null,
            Path = path,
            PathNodes = nodes,
        };
    }

    private static IReadOnlyList<EditorPathPoint> SamplePath(V2PathNote note, V2BpmTimeline timeline)
    {
        var evaluator = new V2PathEvaluator(note);
        var result = new List<EditorPathPoint>();
        var times = new List<ExactBarTime> { note.Time };
        times.AddRange(note.Nodes.Select(node => node.Time));
        AddSample(result, evaluator, timeline, times[0]);
        for (var segment = 0; segment + 1 < times.Count; segment++)
            Subdivide(result, evaluator, timeline, times[segment], times[segment + 1], 0);
        return result;
    }

    private static void Subdivide(List<EditorPathPoint> result, V2PathEvaluator evaluator,
        V2BpmTimeline timeline, ExactBarTime start, ExactBarTime end, int depth)
    {
        var middle = (start + end) / new System.Numerics.BigInteger(2);
        var first = evaluator.Evaluate(start);
        var last = evaluator.Evaluate(end);
        var actual = evaluator.Evaluate(middle);
        var interpolatedCenter = (first.Center + last.Center) * 0.5;
        var interpolatedWidth = (first.Width + last.Width) * 0.5;
        var centerError = Math.Abs(actual.Center - interpolatedCenter) * 273.2;
        var widthError = Math.Abs(actual.Width - interpolatedWidth) * 273.2 * 0.5;
        if (depth < PathSubdivisionDepth && Math.Max(centerError, widthError) > PathScreenError)
        {
            Subdivide(result, evaluator, timeline, start, middle, depth + 1);
            Subdivide(result, evaluator, timeline, middle, end, depth + 1);
            return;
        }
        AddSample(result, evaluator, timeline, end);
    }

    private static void AddSample(List<EditorPathPoint> result, V2PathEvaluator evaluator,
        V2BpmTimeline timeline, ExactBarTime time)
    {
        var sample = evaluator.Evaluate(time);
        result.Add(new EditorPathPoint(time.ToDouble(), timeline.ToSeconds(time), sample.Center, sample.Width));
    }

    private static void RejectDevelopmentTestdata(string root)
    {
        var normalized = Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/');
        if (normalized.Contains("/client/testdata", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("client/testdata is development-only and cannot be loaded by this editor.");
    }
}
