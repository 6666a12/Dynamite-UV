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

public sealed record EditorSelection(EditorSelectionKind Kind, string? Id, EditorEntityKey? Key = null)
{
    public static EditorSelection None { get; } = new(EditorSelectionKind.None, null);
}

public enum EditorTrack
{
    Left,
    Center,
    Right,
}

/// <summary>Non-serialized identity used by editor selection and history; unlike v2 IDs it never changes on rename.</summary>
public readonly record struct EditorEntityKey(Guid Value)
{
    public static EditorEntityKey New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public sealed class EditableBpm
{
    public EditorEntityKey Key { get; internal set; } = EditorEntityKey.New();
    public required ExactBarTime Time { get; set; }
    public required double Bpm { get; set; }
}

public sealed class EditableScroll
{
    public EditorEntityKey Key { get; internal set; } = EditorEntityKey.New();
    public required ExactBarTime Time { get; set; }
    public required double Value { get; set; }
    public V2ScrollCurve? CurveToNext { get; set; }

    public EditableScroll Clone() => new()
    {
        Key = Key,
        Time = Time,
        Value = Value,
        CurveToNext = CurveToNext,
    };
}

public sealed class EditablePathNode
{
    public EditorEntityKey Key { get; internal set; } = EditorEntityKey.New();
    public required string Id { get; set; }
    public required ExactBarTime Time { get; set; }
    public required double Center { get; set; }
    public required double Width { get; set; }
    public V2PathCurve? CurveToNext { get; set; }
    public bool? Judge { get; set; }
}

public sealed class EditableNote
{
    public EditorEntityKey Key { get; internal set; } = EditorEntityKey.New();
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
            Key = Key,
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
                Key = node.Key,
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
    public EditorEntityKey Key { get; internal set; } = EditorEntityKey.New();
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

    public EditableChart Clone()
    {
        var clone = new EditableChart
        {
            Key = Key,
            Id = Id, Difficulty = Difficulty, DifficultyKey = DifficultyKey, Level = Level,
            Unrated = Unrated, File = File, Audio = Audio, Preview = Preview,
            AudioOffsetSec = AudioOffsetSec,
        };
        clone.Charters.AddRange(Charters);
        clone.Bpms.AddRange(Bpms.Select(item => new EditableBpm { Key = item.Key, Time = item.Time, Bpm = item.Bpm }));
        clone.ScrollSpeeds.AddRange(ScrollSpeeds.Select(item => item.Clone()));
        clone.Notes.AddRange(Notes.Select(item => item.Clone()));
        return clone;
    }
}

public sealed class EditorDocument
{
    private readonly Stack<EditorHistoryEntry> _undo = new();
    private readonly Stack<EditorHistoryEntry> _redo = new();
    private int _historyPosition;
    private int _savedHistoryPosition;
    private int _nextId = 1;
    // Lazily rebuilt note/node ID cache. Every mutation entry point (Execute/Undo/Redo and
    // InsertChart/RemoveChart) invalidates it; never mutate note/node IDs outside those paths.
    private HashSet<string>? _allIds;

    public string? PackageDirectory { get; private set; }
    public required string PackId { get; set; }
    public required long Revision { get; set; }
    public required string Title { get; set; }
    public required string Artist { get; set; }
    public string? Audio { get; set; }
    public string? Cover { get; set; }
    public V2Preview? Preview { get; set; }
    public List<EditableChart> Charts { get; } = [];
    private readonly Dictionary<string, string> _pendingResourceReplacements = new(StringComparer.OrdinalIgnoreCase);
    public string SelectedChartId { get; private set; } = string.Empty;
    public EditorSelection Selection { get; private set; } = EditorSelection.None;
    public int GridDivisor { get; set; } = 16;
    public bool SnapEnabled { get; set; } = true;
    public bool IsUnpersisted { get; private set; }
    public bool HasUnsavedChanges { get; private set; }
    public bool IsDirty => HasUnsavedChanges;
    public IReadOnlyList<V2ExternalResourceMapping> PendingResourceReplacements =>
        _pendingResourceReplacements.Select(item => new V2ExternalResourceMapping(item.Value, item.Key)).ToArray();

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

    /// <summary>Normalizes legacy serialized IDs to stable keys at the document boundary.</summary>
    public void Select(EditorSelection selection)
    {
        if (selection.Kind == EditorSelectionKind.None || selection.Key is not null)
        {
            Selection = selection;
            return;
        }
        var key = selection.Kind switch
        {
            EditorSelectionKind.Note => SelectedChart.Notes.SingleOrDefault(item => item.Id == selection.Id)?.Key,
            EditorSelectionKind.PathNode => SelectedChart.Notes.SelectMany(item => item.Nodes)
                .SingleOrDefault(item => item.Id == selection.Id)?.Key,
            EditorSelectionKind.Bpm => ExactBarTimeText.TryParse(selection.Id ?? string.Empty, out var bpmTime)
                ? SelectedChart.Bpms.SingleOrDefault(item => item.Time == bpmTime)?.Key : null,
            EditorSelectionKind.Scroll => ExactBarTimeText.TryParse(selection.Id ?? string.Empty, out var scrollTime)
                ? SelectedChart.ScrollSpeeds.SingleOrDefault(item => item.Time == scrollTime)?.Key : null,
            _ => null,
        };
        Selection = selection with { Key = key };
    }

    internal void SetPendingResource(string packageRelativePath, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(packageRelativePath) || string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Resource paths cannot be empty.");
        _pendingResourceReplacements[packageRelativePath] = Path.GetFullPath(sourcePath);
    }

    internal void RemovePendingResource(string packageRelativePath) =>
        _pendingResourceReplacements.Remove(packageRelativePath);

    internal string? GetPendingResource(string packageRelativePath) =>
        _pendingResourceReplacements.TryGetValue(packageRelativePath, out var source) ? source : null;

    internal void InsertChart(int index, EditableChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (index < 0 || index > Charts.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (Charts.Any(item => string.Equals(item.Id, chart.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Chart ID '{chart.Id}' already exists.");
        if (Charts.Any(item => string.Equals(item.File, chart.File, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Chart file '{chart.File}' is already used.");
        Charts.Insert(index, chart.Clone());
        _allIds = null;
    }

    internal (EditableChart Chart, int Index) RemoveChart(string id)
    {
        if (Charts.Count <= 1)
            throw new InvalidOperationException("A package must contain at least one Chart.");
        var index = Charts.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (index < 0)
            throw new KeyNotFoundException($"Chart '{id}' is not open.");
        var removed = Charts[index].Clone();
        Charts.RemoveAt(index);
        _allIds = null;
        if (SelectedChartId == id)
            SelectChart(Charts[Math.Min(index, Charts.Count - 1)].Id);
        return (removed, index);
    }

    internal int MoveChart(string id, int destinationIndex)
    {
        var currentIndex = Charts.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (currentIndex < 0)
            throw new KeyNotFoundException($"Chart '{id}' is not open.");
        if (destinationIndex < 0 || destinationIndex >= Charts.Count)
            throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        if (currentIndex == destinationIndex)
            return currentIndex;
        var chart = Charts[currentIndex];
        Charts.RemoveAt(currentIndex);
        Charts.Insert(destinationIndex, chart);
        return currentIndex;
    }

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
        _allIds ??= new HashSet<string>(AllIds(), StringComparer.Ordinal);
        while (true)
        {
            var id = $"{prefix}-{_nextId++:D4}";
            if (!_allIds.Contains(id))
                return id;
        }
    }

    public void Execute(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var chartId = SelectedChartId;
        try
        {
            command.Execute(this);
        }
        finally
        {
            _allIds = null;
        }
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
        if (Charts.Any(chart => chart.Id == entry.ChartId))
            SelectChart(entry.ChartId);
        try
        {
            entry.Command.Undo(this);
        }
        finally
        {
            _allIds = null;
        }
        _redo.Push(entry);
        _historyPosition--;
        UpdateDirtyState();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;
        var entry = _redo.Pop();
        if (Charts.Any(chart => chart.Id == entry.ChartId))
            SelectChart(entry.ChartId);
        try
        {
            entry.Command.Execute(this);
        }
        finally
        {
            _allIds = null;
        }
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
