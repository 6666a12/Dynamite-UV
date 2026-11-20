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
    public string? CustomExpr { get; set; }

    public EditableScroll Clone() => (EditableScroll)MemberwiseClone();
    internal V2ScrollEvent ToSnapshot() => new()
    {
        Time = Time, Value = Value, CurveToNext = CurveToNext, CustomExpr = CustomExpr,
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
    public string? CustomExpr { get; set; }
    public bool? Judge { get; set; }
    internal EditablePathNode Clone() => (EditablePathNode)MemberwiseClone();
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
    public double ScrollSpeedMultiplier { get; set; } = 1.0;
    public V2PathCurve? CurveToNext { get; set; }
    public string? CustomExpr { get; set; }
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
            ScrollSpeedMultiplier = ScrollSpeedMultiplier,
            CurveToNext = CurveToNext,
            CustomExpr = CustomExpr,
        };
        clone.Nodes.AddRange(Nodes.Select(node => node.Clone()));
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
    internal ChartOffsetPosition OffsetPosition { get; set; }
    public required double AudioOffsetSec
    {
        get => OffsetPosition.Seconds;
        set => OffsetPosition = ChartOffsetPosition.FromSeconds(value);
    }
    public List<EditableBpm> Bpms { get; } = [];
    public List<EditableScroll> ScrollSpeeds { get; } = [];
    public List<EditableNote> Notes { get; } = [];

    public CustomCurveCache CurveCache { get; internal set; } = new();

    // An explicitly v2.1 source without Custom is legal and its existing ruleset is part of its identity.
    internal int DeclaredMinorFloor { get; set; }
    public int FormatMinor => DeclaredMinorFloor != 0 || ScrollSpeeds.Exists(s => s.CurveToNext == V2ScrollCurve.Custom) ||
        Notes.Exists(n => n.CurveToNext == V2PathCurve.Custom || n.Nodes.Exists(p => p.CurveToNext == V2PathCurve.Custom))
            ? V2Format.CustomCurvesMinor : 0;

    public EditableChart Clone()
    {
        var clone = new EditableChart
        {
            Key = Key, DeclaredMinorFloor = DeclaredMinorFloor, CurveCache = CurveCache.Fork(),
            Id = Id, Difficulty = Difficulty, DifficultyKey = DifficultyKey, Level = Level,
            Unrated = Unrated, File = File, Audio = Audio, Preview = Preview,
            AudioOffsetSec = AudioOffsetSec, OffsetPosition = OffsetPosition,
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
    /// <summary>DynaMaker's default magnetic grid: 1/16 of a chart bar. Single source for the
    /// document default, the canvas reset shortcut and the new-project request.</summary>
    public const int DefaultGridDivisor = 16;

    private readonly Stack<EditorHistoryEntry> _undo = new();
    private readonly Stack<EditorHistoryEntry> _redo = new();
    private int _historyPosition;
    private int _savedHistoryPosition;
    private int _nextId = 1;
    // Lazily rebuilt note/node ID cache. Every mutation entry point (Execute/Undo/Redo and
    // InsertChart/RemoveChart) invalidates it; never mutate note/node IDs outside those paths.
    private HashSet<string>? _allIds;
    private ChartOffsetNudge? _activeOffsetNudge;

    public string? PackageDirectory { get; private set; }
    /// <summary>Minor version of the last opened or successfully written package, not the live edits.</summary>
    public int SourceFormatMinor { get; private set; }
    private int _declaredPackMinorFloor;
    public int FormatMinor => _declaredPackMinorFloor != 0 || Charts.Exists(chart => chart.FormatMinor != 0)
        ? V2Format.CustomCurvesMinor : 0;
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
    public int GridDivisor { get; set; } = DefaultGridDivisor;
    public bool SnapEnabled { get; set; } = true;
    public bool IsUnpersisted { get; private set; }
    public bool HasUnsavedChanges { get; private set; }
    public bool IsDirty => HasUnsavedChanges;
    public long ChangeVersion { get; private set; }
    public IReadOnlyList<V2ExternalResourceMapping> PendingResourceReplacements =>
        _pendingResourceReplacements.Select(item => new V2ExternalResourceMapping(item.Value, item.Key)).ToArray();

    public EditableChart SelectedChart => Charts.Single(chart => chart.Id == SelectedChartId);
    public bool CanUndo => _undo.Count > 0 || _activeOffsetNudge?.HasChange == true;
    public bool CanRedo => _redo.Count > 0 && _activeOffsetNudge?.HasChange != true;

    public static EditorDocument FromPackage(string? packageDirectory, V2Pack pack,
        IReadOnlyDictionary<string, V2Chart> charts)
    {
        var document = new EditorDocument
        {
            PackageDirectory = packageDirectory,
            SourceFormatMinor = packageDirectory is null ? 0 : pack.FormatMinor,
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
                CurveCache = CustomCurveCache.For(chart).Fork(),
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
                CurveToNext = item.CurveToNext, CustomExpr = item.CustomExpr,
            }));
            editable.Notes.AddRange(ToEditableNotes(chart.NotesLeft, EditorTrack.Left));
            editable.Notes.AddRange(ToEditableNotes(chart.NotesCenter, EditorTrack.Center));
            editable.Notes.AddRange(ToEditableNotes(chart.NotesRight, EditorTrack.Right));
            if (editable.FormatMinor == 0) editable.DeclaredMinorFloor = chart.FormatMinor;
            document.Charts.Add(editable);
        }
        if (document.FormatMinor == 0) document._declaredPackMinorFloor = pack.FormatMinor;
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
        if (id != SelectedChartId) EndChartOffsetNudge();
        SelectedChartId = id;
        Selection = EditorSelection.None;
    }

    public ChartOffsetNudge BeginChartOffsetNudge()
    {
        EndChartOffsetNudge();
        return _activeOffsetNudge = new ChartOffsetNudge(this, SelectedChart);
    }

    internal void OffsetStepApplied()
    {
        ChangeVersion++;
        UpdateDirtyState();
    }

    public void EndChartOffsetNudge()
    {
        if (_activeOffsetNudge is not { } nudge) return;
        _activeOffsetNudge = null;
        if (nudge.Finish() is { } command) RecordCommand(command, nudge.ChartId);
        UpdateDirtyState();
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
        EndChartOffsetNudge();
        var chartId = SelectedChartId;
        ExecuteValidated(command);
        ChangeVersion++;
        RecordCommand(command, chartId);
        UpdateDirtyState();
    }

    private void ExecuteValidated(IEditorCommand command)
    {
        var owner = SelectedChart;
        var chartId = owner.Id;
        var selection = Selection;
        var previous = owner.CurveCache;
        var candidates = previous.ForkForCommand();
        owner.CurveCache = candidates;
        try
        {
            command.Execute(this);
            // Individual path/Scroll edits can all be valid but their combined chart may
            // exceed the strict limit. Never publish history or an unusable stage snapshot.
            var current = SelectedChart;
            var minor = current.FormatMinor;
            if (minor != 0)
            {
                try { V2SemanticValidator.ValidateChart(BuildChartSnapshot(SelectedChartId), requireMainJudgement: false); }
                catch
                {
                    command.Undo(this);
                    if (SelectedChartId != chartId) SelectChart(chartId);
                    Select(selection);
                    throw;
                }
            }
            // Drop stale tables on downgrade; keep only validated entries on success.
            previous = ReferenceEquals(current, owner) && minor == 0 ? new() : candidates.Fork();
        }
        finally
        {
            owner.CurveCache = previous;
            _allIds = null;
        }
    }

    private void RecordCommand(IEditorCommand command, string chartId)
    {
        _undo.Push(new EditorHistoryEntry(command, chartId));
        if (_redo.Count > 0 && _savedHistoryPosition > _historyPosition)
            _savedHistoryPosition = -1;
        _redo.Clear();
        _historyPosition++;
    }

    public void Undo()
    {
        EndChartOffsetNudge();
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
        ChangeVersion++;
        _historyPosition--;
        UpdateDirtyState();
    }

    public void Redo()
    {
        EndChartOffsetNudge();
        if (_redo.Count == 0)
            return;
        var entry = _redo.Peek();
        if (Charts.Any(chart => chart.Id == entry.ChartId))
            SelectChart(entry.ChartId);
        ExecuteValidated(entry.Command);
        _redo.Pop();
        ChangeVersion++;
        _undo.Push(entry);
        _historyPosition++;
        UpdateDirtyState();
    }

    internal void CompleteSave(string directory, long version, int savedMinor, IReadOnlyList<V2ExternalResourceMapping> resources)
    {
        foreach (var resource in resources)
            if (_pendingResourceReplacements.TryGetValue(resource.PackageRelativePath, out var source) &&
                string.Equals(source, resource.ExternalSourcePath, StringComparison.OrdinalIgnoreCase))
                _pendingResourceReplacements.Remove(resource.PackageRelativePath);
        PackageDirectory = Path.GetFullPath(directory);
        SourceFormatMinor = savedMinor;
        IsUnpersisted = false;
        _savedHistoryPosition = ChangeVersion == version ? _historyPosition : -1;
        UpdateDirtyState();
    }

    public void MarkSaved(string packageDirectory)
    {
        EndChartOffsetNudge();
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        PackageDirectory = Path.GetFullPath(packageDirectory);
        SourceFormatMinor = FormatMinor;
        IsUnpersisted = false;
        _savedHistoryPosition = _historyPosition;
        UpdateDirtyState();
    }

    private void UpdateDirtyState() =>
        HasUnsavedChanges = IsUnpersisted || _historyPosition != _savedHistoryPosition || _activeOffsetNudge?.HasChange == true;

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
        FormatMinor = FormatMinor,
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

    public V2Chart BuildChartSnapshot(string id) => BuildChartSnapshot(id, isolateCache: false);

    private V2Chart BuildChartSnapshot(string id, bool isolateCache)
    {
        var chart = Charts.Single(item => item.Id == id);
        return (isolateCache ? chart.CurveCache.Fork() : chart.CurveCache).Attach(new V2Chart
        {
            ChartId = chart.Id,
            FormatMinor = chart.FormatMinor,
            AudioOffsetSec = chart.AudioOffsetSec,
            Bpms = chart.Bpms.OrderBy(item => item.Time)
                .Select(item => new V2BpmEvent(item.Time, item.Bpm)).ToArray(),
            ScrollSpeeds = chart.ScrollSpeeds.OrderBy(item => item.Time)
                .Select(item => item.ToSnapshot()).ToArray(),
            NotesLeft = BuildTrack(chart, EditorTrack.Left),
            NotesCenter = BuildTrack(chart, EditorTrack.Center),
            NotesRight = BuildTrack(chart, EditorTrack.Right),
        });
    }

    public IReadOnlyDictionary<string, V2Chart> BuildAllChartSnapshots() => Charts.ToDictionary(
        chart => chart.Id,
        chart => BuildChartSnapshot(chart.Id, isolateCache: true),
        StringComparer.Ordinal);

    private static IReadOnlyList<V2Note> BuildTrack(EditableChart chart, EditorTrack track) => chart.Notes
        .Where(note => note.Track == track)
        .OrderBy(note => note.Time)
        .ThenBy(note => note.Id, StringComparer.Ordinal)
        .Select(ToSnapshot)
        .ToArray();

    internal static V2Note ToSnapshot(EditableNote note)
    {
        if (note.Type is not (V2NoteType.Hold or V2NoteType.Mixer))
        {
            if (note.CustomExpr is not null || note.CurveToNext is not null || note.Nodes.Count != 0)
                throw new InvalidOperationException($"Non-path note '{note.Id}' has path data.");
            return new V2BasicNote
            {
                Id = note.Id,
                Type = note.Type,
                Time = note.Time,
                Center = note.Center,
                Width = note.Width,
                ScrollSpeedMultiplier = note.ScrollSpeedMultiplier,
            };
        }
        var nodes = note.Nodes.Select((node, index) => new V2PathNode
        {
            Id = node.Id,
            Time = node.Time,
            Center = node.Center,
            Width = node.Width,
            CurveToNext = node.CurveToNext, CustomExpr = node.CustomExpr,
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
            ScrollSpeedMultiplier = note.ScrollSpeedMultiplier,
            CurveToNext = note.CurveToNext, CustomExpr = note.CustomExpr,
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
                ScrollSpeedMultiplier = note.ScrollSpeedMultiplier,
                CurveToNext = (note as V2PathNote)?.CurveToNext,
                CustomExpr = (note as V2PathNote)?.CustomExpr,
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
                        CurveToNext = node.CurveToNext, CustomExpr = node.CustomExpr,
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
