using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Rebuildable, mutable preview data. Editing a projection never mutates the document.</summary>
public sealed class CanvasProjection
{
    public List<AuthoringNote> Notes { get; } = new();
    public List<AuthoringBpmEvent> BpmEvents { get; } = new();
    public double DurationSeconds { get; private set; } = 16;

    public void Clear()
    {
        Notes.Clear();
        BpmEvents.Clear();
    }

    public void Rebuild(EditableChart chart)
    {
        Clear();
        var bpms = chart.Bpms;
        var orderedBpms = bpms.OrderBy(item => item.Time).ToArray();
        for (var i = 0; i < orderedBpms.Length; i++)
        {
            var eventItem = orderedBpms[i];
            BpmEvents.Add(new AuthoringBpmEvent
            {
                Id = $"bpm-{i + 1}",
                TimeExact = eventItem.Time,
                Time = EditorTime.BarToSeconds(eventItem.Time, bpms),
                Bpm = eventItem.Bpm,
                Locked = i == 0,
            });
        }

        var maxTime = 16d;
        foreach (var note in chart.Notes)
        {
            var tail = note.Nodes.Count > 0 ? note.Nodes[^1].Time : note.Time;
            var item = new AuthoringNote
            {
                Id = note.Id,
                Kind = CanvasKind(note.Type),
                Side = note.Track switch { EditorTrack.Left => 1, EditorTrack.Right => 2, _ => 0 },
                TimeExact = note.Time,
                Time = EditorTime.BarToSeconds(note.Time, bpms),
                EndExact = tail,
                EndTime = EditorTime.BarToSeconds(tail, bpms),
                Center = Math.Clamp(note.Center - note.Width / 2d, 0, 5 - note.Width),
                Width = Math.Clamp(note.Width, .1, 5),
                CurveToNext = note.CurveToNext,
            };
            maxTime = Math.Max(maxTime, item.Time + 4);
            foreach (var node in note.Nodes)
            {
                var nodeTime = EditorTime.BarToSeconds(node.Time, bpms);
                item.EndTime = Math.Max(item.EndTime, nodeTime);
                maxTime = Math.Max(maxTime, nodeTime + 2);
                item.Nodes.Add(new AuthoringPathNode
                {
                    Id = node.Id,
                    TimeExact = node.Time,
                    Time = nodeTime,
                    Center = Math.Clamp(node.Center - node.Width / 2d, 0, 5 - node.Width),
                    Width = Math.Clamp(node.Width, .1, 5),
                    CurveToNext = node.CurveToNext,
                    Judge = node.Judge == true,
                });
            }
            Notes.Add(item);
        }

        DurationSeconds = Math.Clamp(maxTime, 16, 600);
    }

    private static AuthoringNoteKind CanvasKind(V2NoteType type) => type switch
    {
        V2NoteType.Drag => AuthoringNoteKind.Chain,
        V2NoteType.Hold => AuthoringNoteKind.Hold,
        V2NoteType.Mixer => AuthoringNoteKind.Mixer,
        V2NoteType.ExTap => AuthoringNoteKind.ExTap,
        V2NoteType.Mine => AuthoringNoteKind.Mine,
        V2NoteType.BarLine => AuthoringNoteKind.BarLine,
        _ => AuthoringNoteKind.Normal,
    };
}
