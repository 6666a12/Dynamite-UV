using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Read-only drawing data in chart bars. Editing, hit judgement and seek are not owned by this projection.</summary>
internal sealed record TimelineRhythmNote(double Start, double End, V2NoteType Type, IReadOnlyList<double> Judgements);

internal sealed class TimelineRhythmData
{
    private readonly TimelineRhythmNote[][] _tracks;
    public IReadOnlyList<double> AllHits { get; }
    public double LastBar { get; }
    public IReadOnlyList<TimelineRhythmNote> ForTrack(EditorTrack track) => _tracks[(int)track];

    public TimelineRhythmData(EditableChart chart)
    {
        var tracks = new List<TimelineRhythmNote>[] { [], [], [] };
        foreach (var note in chart.Notes)
        {
            var start = note.Time.ToDouble();
            var sustain = note.Type is V2NoteType.Hold or V2NoteType.Mixer;
            // Authored nodes are in time order. A tail is the end of the band, never a new onset.
            var end = sustain && note.Nodes.Count != 0 ? note.Nodes[^1].Time.ToDouble() : start;
            LastBar = Math.Max(LastBar, end);
            if (note.Type == V2NoteType.BarLine) continue; // Display-only chart marker, not part of the played rhythm.
            var judges = note.Type == V2NoteType.Hold ? note.Nodes.Take(Math.Max(0, note.Nodes.Count - 1))
                .Where(n => n.Judge == true).Select(n => n.Time.ToDouble()).ToArray() : Array.Empty<double>();
            tracks[(int)note.Track].Add(new(start, end, note.Type, judges));
        }
        _tracks = tracks.Select(t => t.OrderBy(n => n.Start).ToArray()).ToArray();
        AllHits = _tracks.SelectMany(t => t).Select(n => n.Start).Order().ToArray();
    }
}

/// <summary>Maps the current recording's length to the current difficulty's bars. The stage supplies
/// its decoded duration; the shell never opens or decodes the media on the UI thread.</summary>
internal sealed class WorkbenchTimeline(TimelineView view)
{
    private V2BpmTimeline? _tempo;
    private double _duration;
    private EditableChart? _chart;
    private string? _audioPath;

    public void Refresh(EditableChart? chart)
    {
        if (!ReferenceEquals(_chart, chart)) _duration = 0;
        _chart = chart;
        view.Rhythm = chart is null ? null : new TimelineRhythmData(chart);
        RefreshTiming(chart);
    }
    public void RefreshTiming(EditableChart? chart)
    {
        _tempo = chart is null ? null : new V2BpmTimeline(chart.Bpms.Select(b => new V2BpmEvent(b.Time, b.Bpm)).OrderBy(b => b.Time).ToArray(), chart.AudioOffsetSec);
        UpdateExtent();
    }
    public void SetAudioSource(string? path)
    {
        if (_audioPath == path) return;
        _audioPath = path; _duration = 0; UpdateExtent();
    }
    public void SetAudioDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || _duration == seconds) return;
        _duration = seconds; UpdateExtent();
    }
    private void UpdateExtent()
    {
        var last = view.Rhythm?.LastBar ?? 0;
        view.MaximumBar = _duration > 0 && _tempo is not null
            ? Math.Max(last, _tempo.RuntimeBarAt(_duration)) : Math.Max(32, Math.Ceiling(last + 4));
    }
    public double SecondsAt(double bar) => _tempo?.RuntimeSecondsAt(bar) ?? 0;
    public double Advance(double bar, double seconds) => _tempo?.RuntimeBarAt(SecondsAt(bar) + seconds) ?? bar;
}
