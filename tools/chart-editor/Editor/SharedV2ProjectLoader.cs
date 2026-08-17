using DuxCommunity.ChartEditor.Core;
using DuxShared.Chart;
using DuxShared.Chart.V2;

namespace DuxCommunity.ChartEditor.Editor;

/// <summary>Application adapter that keeps Avalonia snapshots separate from the editable core document.</summary>
public sealed class SharedV2ProjectLoader
{
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

    public static EditorProject Snapshot(EditorDocument document)
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
            Charts = document.Charts.Select(chart => SnapshotChart(document, chart)).ToArray(),
        };
    }

    private static EditorChartDocument SnapshotChart(EditorDocument document, EditableChart chart)
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
        var lastSecond = notes.Length == 0
            ? chartSnapshot.AudioOffsetSec
            : notes.Max(note => note.EndSecond);
        var entry = document.BuildPackSnapshot().Charts.Single(item => item.Id == chart.Id);
        var resolvedAudio = entry.Audio ?? document.Audio ?? "";
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
            AudioOffsetSec = chart.AudioOffsetSec,
            DurationSec = Math.Max(8.0, lastSecond + 2.0),
            MainJudgementCount = judgementCount > long.MaxValue ? long.MaxValue : (long)judgementCount,
            Bpms = bpms,
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
        return new EditorNoteModel
        {
            Id = note.Id,
            Type = DisplayType(note.Type),
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
            Path = path,
        };
    }

    private static IReadOnlyList<EditorPathPoint> SamplePath(V2PathNote note, V2BpmTimeline timeline)
    {
        var evaluator = new V2PathEvaluator(note);
        var result = new List<EditorPathPoint>();
        var times = new List<ExactBarTime> { note.Time };
        times.AddRange(note.Nodes.Select(node => node.Time));
        for (var segment = 0; segment + 1 < times.Count; segment++)
        {
            const int subdivisions = 10;
            for (var step = segment == 0 ? 0 : 1; step <= subdivisions; step++)
            {
                var u = ExactBarTime.FromFraction(step, subdivisions);
                var time = times[segment] + (times[segment + 1] - times[segment]) * u;
                var sample = evaluator.Evaluate(time);
                result.Add(new EditorPathPoint(time.ToDouble(), timeline.ToSeconds(time), sample.Center, sample.Width));
            }
        }
        return result;
    }

    private static string DisplayType(V2NoteType type) => type switch
    {
        V2NoteType.ExTap => "EX-TAP",
        V2NoteType.BarLine => "BARLINE",
        _ => type.ToString().ToUpperInvariant(),
    };

    private static void RejectDevelopmentTestdata(string root)
    {
        var normalized = Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/');
        if (normalized.Contains("/client/testdata", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("client/testdata is development-only and cannot be loaded by this editor.");
    }
}
