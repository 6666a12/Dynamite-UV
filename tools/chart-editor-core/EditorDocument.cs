using System.Numerics;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public enum EditorSelectionKind
{
    None,
    Note,
    PathNode,
    Bpm,
    Scroll,
}

public sealed record EditorSelection(EditorSelectionKind Kind, string? Id)
{
    public static EditorSelection None { get; } = new(EditorSelectionKind.None, null);
}

public enum EditorTrack
{
    Left,
    Center,
    Right,
}

public sealed class EditableBpm
{
    public required ExactBarTime Time { get; set; }
    public required double Bpm { get; set; }
}

public sealed class EditableScroll
{
    public required ExactBarTime Time { get; set; }
    public required double Value { get; set; }
    public V2ScrollCurve? CurveToNext { get; set; }

    public EditableScroll Clone() => new()
    {
        Time = Time,
        Value = Value,
        CurveToNext = CurveToNext,
    };
}

public sealed class EditablePathNode
{
    public required string Id { get; set; }
    public required ExactBarTime Time { get; set; }
    public required double Center { get; set; }
    public required double Width { get; set; }
    public V2PathCurve? CurveToNext { get; set; }
    public bool? Judge { get; set; }
}

public sealed class EditableNote
{
    public required string Id { get; set; }
    public required V2NoteType Type { get; set; }
    public required EditorTrack Track { get; set; }
    public required ExactBarTime Time { get; set; }
    public required double Center { get; set; }
    public required double Width { get; set; }
    public V2PathCurve? CurveToNext { get; set; }
    public List<EditablePathNode> Nodes { get; } = [];

    public EditableNote Clone()
    {
        var clone = new EditableNote
        {
            Id = Id,
            Type = Type,
            Track = Track,
            Time = Time,
            Center = Center,
            Width = Width,
            CurveToNext = CurveToNext,
        };
        foreach (var node in Nodes)
        {
            clone.Nodes.Add(new EditablePathNode
            {
                Id = node.Id,
                Time = node.Time,
                Center = node.Center,
                Width = node.Width,
                CurveToNext = node.CurveToNext,
                Judge = node.Judge,
            });
        }
        return clone;
    }
}

public sealed class EditableChart
{
    public required string Id { get; set; }
    public required V2Difficulty Difficulty { get; set; }
    public string? DifficultyKey { get; set; }
    public int? Level { get; set; }
    public bool Unrated { get; set; }
    public List<string> Charters { get; } = [];
    public required string File { get; set; }
    public string? Audio { get; set; }
    public V2Preview? Preview { get; set; }
    public required double AudioOffsetSec { get; set; }
    public List<EditableBpm> Bpms { get; } = [];
    public List<EditableScroll> ScrollSpeeds { get; } = [];
    public List<EditableNote> Notes { get; } = [];
}

public sealed class EditorDocument
{
    private readonly Stack<EditorHistoryEntry> _undo = new();
    private readonly Stack<EditorHistoryEntry> _redo = new();
    private int _historyPosition;
    private int _savedHistoryPosition;
    private int _nextId = 1;

    public string? PackageDirectory { get; private set; }
    public required string PackId { get; set; }
    public required long Revision { get; set; }
    public required string Title { get; set; }
    public required string Artist { get; set; }
    public string? Audio { get; set; }
    public string? Cover { get; set; }
    public V2Preview? Preview { get; set; }
    public List<EditableChart> Charts { get; } = [];
    public string SelectedChartId { get; private set; } = string.Empty;
    public EditorSelection Selection { get; private set; } = EditorSelection.None;
    public int GridDivisor { get; set; } = 16;
    public bool SnapEnabled { get; set; } = true;
    public bool IsUnpersisted { get; private set; }
    public bool HasUnsavedChanges { get; private set; }
    public bool IsDirty => HasUnsavedChanges;

    public EditableChart SelectedChart => Charts.Single(chart => chart.Id == SelectedChartId);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public static EditorDocument FromPackage(string? packageDirectory, V2Pack pack,
        IReadOnlyDictionary<string, V2Chart> charts)
    {
        var document = new EditorDocument
        {
            PackageDirectory = packageDirectory,
            PackId = pack.Id,
            Revision = pack.Revision,
            Title = pack.Title,
            Artist = pack.Artist,
            Audio = pack.Audio,
            Cover = pack.Cover,
            Preview = pack.Preview,
        };
        foreach (var entry in pack.Charts)
        {
            if (!charts.TryGetValue(entry.Id, out var chart))
                throw new InvalidOperationException($"Chart '{entry.Id}' is missing from editor load data.");
            var editable = new EditableChart
            {
                Id = entry.Id,
                Difficulty = entry.Difficulty,
                DifficultyKey = entry.DifficultyKey,
                Level = entry.Level,
                Unrated = entry.Unrated,
                File = entry.File,
                Audio = entry.Audio,
                Preview = entry.Preview,
                AudioOffsetSec = chart.AudioOffsetSec,
            };
            editable.Charters.AddRange(entry.Charters);
            editable.Bpms.AddRange(chart.Bpms.Select(item => new EditableBpm
            {
                Time = item.Time,
                Bpm = item.Bpm,
            }));
            editable.ScrollSpeeds.AddRange(chart.ScrollSpeeds.Select(item => new EditableScroll
            {
                Time = item.Time,
                Value = item.Value,
                CurveToNext = item.CurveToNext,
            }));
            editable.Notes.AddRange(ToEditableNotes(chart.NotesLeft, EditorTrack.Left));
            editable.Notes.AddRange(ToEditableNotes(chart.NotesCenter, EditorTrack.Center));
            editable.Notes.AddRange(ToEditableNotes(chart.NotesRight, EditorTrack.Right));
            document.Charts.Add(editable);
        }
        document.SelectedChartId = document.Charts[0].Id;
        document._nextId = document.AllIds().Select(TrailingNumber).DefaultIfEmpty(0).Max() + 1;
        document.IsUnpersisted = packageDirectory is null;
        document.UpdateDirtyState();
        return document;
    }

    public void SelectChart(string id)
    {
        if (!Charts.Any(chart => chart.Id == id))
            throw new KeyNotFoundException($"Chart '{id}' is not open.");
        SelectedChartId = id;
        Selection = EditorSelection.None;
    }

    public void Select(EditorSelection selection) => Selection = selection;

    public ExactBarTime Snap(ExactBarTime time)
    {
        if (!SnapEnabled)
            return time;
        if (GridDivisor <= 0)
            throw new InvalidOperationException("Grid divisor must be positive.");
        var scaled = time.ImproperNumerator * GridDivisor;
        var denominator = time.Denominator;
        var rounded = BigInteger.Divide(scaled * 2 + denominator, denominator * 2);
        return ExactBarTime.FromFraction(rounded, GridDivisor);
    }

    public string AllocateId(string prefix)
    {
        while (true)
        {
            var id = $"{prefix}-{_nextId++:D4}";
            if (!AllIds().Contains(id, StringComparer.Ordinal))
                return id;
        }
    }

    public void Execute(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var chartId = SelectedChartId;
        command.Execute(this);
        _undo.Push(new EditorHistoryEntry(command, chartId));
        if (_redo.Count > 0 && _savedHistoryPosition > _historyPosition)
            _savedHistoryPosition = -1;
        _redo.Clear();
        _historyPosition++;
        UpdateDirtyState();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
            return;
        var entry = _undo.Pop();
        SelectChart(entry.ChartId);
        entry.Command.Undo(this);
        _redo.Push(entry);
        _historyPosition--;
        UpdateDirtyState();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;
        var entry = _redo.Pop();
        SelectChart(entry.ChartId);
        entry.Command.Execute(this);
        _undo.Push(entry);
        _historyPosition++;
        UpdateDirtyState();
    }

    public void MarkSaved(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        PackageDirectory = Path.GetFullPath(packageDirectory);
        IsUnpersisted = false;
        _savedHistoryPosition = _historyPosition;
        UpdateDirtyState();
    }

    private void UpdateDirtyState() =>
        HasUnsavedChanges = IsUnpersisted || _historyPosition != _savedHistoryPosition;

    internal EditableNote RequireNote(string id) => SelectedChart.Notes.Single(note => note.Id == id);
    internal EditablePathNode RequirePathNode(string noteId, string nodeId) =>
        RequireNote(noteId).Nodes.Single(node => node.Id == nodeId);
    internal EditableBpm RequireBpm(ExactBarTime time) => SelectedChart.Bpms.Single(item => item.Time == time);
    internal EditableScroll RequireScroll(ExactBarTime time) =>
        SelectedChart.ScrollSpeeds.Single(item => item.Time == time);

    internal void RequireChartWideIdAvailable(string id, string? exceptId = null)
    {
        var chartIds = SelectedChart.Notes.SelectMany(note =>
            new[] { note.Id }.Concat(note.Nodes.Select(node => node.Id)));
        if (chartIds.Any(existing =>
                !string.Equals(existing, exceptId, StringComparison.Ordinal) &&
                string.Equals(existing, id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Chart object ID '{id}' already exists.");
    }

    public V2Pack BuildPackSnapshot() => new()
    {
        Id = PackId,
        Revision = Revision,
        Title = Title,
        Artist = Artist,
        Audio = Audio,
        Cover = Cover,
        Preview = Preview,
        Charts = Charts.Select(chart => new V2ChartEntry
        {
            Id = chart.Id,
            Difficulty = chart.Difficulty,
            DifficultyKey = chart.DifficultyKey,
            Level = chart.Level,
            Unrated = chart.Unrated,
            Charters = chart.Charters.ToArray(),
            File = chart.File,
            Audio = chart.Audio,
            Preview = chart.Preview,
        }).ToArray(),
    };

    public V2Chart BuildChartSnapshot(string id)
    {
        var chart = Charts.Single(item => item.Id == id);
        return new V2Chart
        {
            ChartId = chart.Id,
            AudioOffsetSec = chart.AudioOffsetSec,
            Bpms = chart.Bpms.OrderBy(item => item.Time)
                .Select(item => new V2BpmEvent(item.Time, item.Bpm)).ToArray(),
            ScrollSpeeds = chart.ScrollSpeeds.OrderBy(item => item.Time)
                .Select(item => new V2ScrollEvent
                {
                    Time = item.Time,
                    Value = item.Value,
                    CurveToNext = item.CurveToNext,
                }).ToArray(),
            NotesLeft = BuildTrack(chart, EditorTrack.Left),
            NotesCenter = BuildTrack(chart, EditorTrack.Center),
            NotesRight = BuildTrack(chart, EditorTrack.Right),
        };
    }

    public IReadOnlyDictionary<string, V2Chart> BuildAllChartSnapshots() => Charts.ToDictionary(
        chart => chart.Id,
        chart => BuildChartSnapshot(chart.Id),
        StringComparer.Ordinal);

    private static IReadOnlyList<V2Note> BuildTrack(EditableChart chart, EditorTrack track) => chart.Notes
        .Where(note => note.Track == track)
        .OrderBy(note => note.Time)
        .ThenBy(note => note.Id, StringComparer.Ordinal)
        .Select(ToSnapshot)
        .ToArray();

    private static V2Note ToSnapshot(EditableNote note)
    {
        if (note.Type is not (V2NoteType.Hold or V2NoteType.Mixer))
        {
            return new V2BasicNote
            {
                Id = note.Id,
                Type = note.Type,
                Time = note.Time,
                Center = note.Center,
                Width = note.Width,
            };
        }
        var nodes = note.Nodes.Select((node, index) => new V2PathNode
        {
            Id = node.Id,
            Time = node.Time,
            Center = node.Center,
            Width = node.Width,
            CurveToNext = node.CurveToNext,
            Judge = note.Type == V2NoteType.Hold ? node.Judge : null,
            JudgeWasExplicit = note.Type == V2NoteType.Hold &&
                (node.Judge is not null || index + 1 == note.Nodes.Count),
        }).ToArray();
        return new V2PathNote
        {
            Id = note.Id,
            Type = note.Type,
            Time = note.Time,
            Center = note.Center,
            Width = note.Width,
            CurveToNext = note.CurveToNext,
            Nodes = nodes,
        };
    }

    private static IEnumerable<EditableNote> ToEditableNotes(IReadOnlyList<V2Note> source, EditorTrack track)
    {
        foreach (var note in source)
        {
            var editable = new EditableNote
            {
                Id = note.Id,
                Type = note.Type,
                Track = track,
                Time = note.Time,
                Center = note.Center,
                Width = note.Width,
                CurveToNext = note is V2PathNote path ? path.CurveToNext : null,
            };
            if (note is V2PathNote pathNote)
            {
                foreach (var node in pathNote.Nodes)
                {
                    editable.Nodes.Add(new EditablePathNode
                    {
                        Id = node.Id,
                        Time = node.Time,
                        Center = node.Center,
                        Width = node.Width,
                        CurveToNext = node.CurveToNext,
                        Judge = node.Judge,
                    });
                }
            }
            yield return editable;
        }
    }

    private IEnumerable<string> AllIds() => Charts.SelectMany(chart => chart.Notes)
        .SelectMany(note => new[] { note.Id }.Concat(note.Nodes.Select(node => node.Id)));

    private sealed record EditorHistoryEntry(IEditorCommand Command, string ChartId);

    private static int TrailingNumber(string id)
    {
        var digits = new string(id.Reverse().TakeWhile(char.IsAsciiDigit).Reverse().ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }
}
