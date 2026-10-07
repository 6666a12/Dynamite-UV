using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public interface IEditorCommand
{
    void Execute(EditorDocument document);
    void Undo(EditorDocument document);
}

public sealed class CompositeEditorCommand(params IEditorCommand[] commands) : IEditorCommand
{
    private readonly IReadOnlyList<IEditorCommand> _commands = commands.Length > 0
        ? commands
        : throw new ArgumentException("A composite command requires at least one command.", nameof(commands));

    public void Execute(EditorDocument document)
    {
        var completed = 0;
        try
        {
            for (; completed < _commands.Count; completed++)
                _commands[completed].Execute(document);
        }
        catch
        {
            for (var index = completed - 1; index >= 0; index--)
                _commands[index].Undo(document);
            throw;
        }
    }

    public void Undo(EditorDocument document)
    {
        for (var index = _commands.Count - 1; index >= 0; index--)
            _commands[index].Undo(document);
    }
}

/// <summary>Edits user-authored pack identity and metadata through the same undo history as canvas work.</summary>
public sealed class EditPackMetadataCommand(string packId, string title, string artist) : IEditorCommand
{
    private readonly string _packId = packId;
    private readonly string _title = title;
    private readonly string _artist = artist;
    private (string PackId, string Title, string Artist) _before;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        if (!_captured)
        {
            _before = (document.PackId, document.Title, document.Artist);
            _captured = true;
        }
        Apply(document, _packId, _title, _artist);
    }

    public void Undo(EditorDocument document) =>
        Apply(document, _before.PackId, _before.Title, _before.Artist);

    private static void Apply(EditorDocument document, string packId, string title, string artist)
    {
        var previous = (document.PackId, document.Title, document.Artist);
        document.PackId = packId;
        document.Title = title;
        document.Artist = artist;
        try
        {
            V2SemanticValidator.ValidatePack(document.BuildPackSnapshot());
            _ = V2JsonDecoder.DecodePack(V2JsonEncoder.EncodePack(document.BuildPackSnapshot()), "editor metadata");
        }
        catch
        {
            (document.PackId, document.Title, document.Artist) = previous;
            throw;
        }
    }
}

/// <summary>Edits a package or chart preview interval through strict v2 metadata validation.</summary>
public sealed class EditPreviewCommand(bool packScope, string? chartId, double startSec, double durationSec) : IEditorCommand
{
    private V2Preview? _before;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var chart = packScope ? null : document.Charts.SingleOrDefault(item => item.Id == chartId);
        if (!packScope && chart is null)
            throw new KeyNotFoundException($"Chart '{chartId}' is not open.");
        if (!_captured)
        {
            _before = packScope ? document.Preview : chart!.Preview;
            _captured = true;
        }
        Apply(document, chart, new V2Preview(startSec, durationSec));
    }

    public void Undo(EditorDocument document)
    {
        var chart = packScope ? null : document.Charts.SingleOrDefault(item => item.Id == chartId);
        if (!packScope && chart is null)
            throw new KeyNotFoundException($"Chart '{chartId}' is not open.");
        Apply(document, chart, _before);
    }

    private void Apply(EditorDocument document, EditableChart? chart, V2Preview? preview)
    {
        var previous = packScope ? document.Preview : chart!.Preview;
        if (packScope) document.Preview = preview; else chart!.Preview = preview;
        try { EditorChartCommandRules.Validate(document, chartId ?? document.SelectedChartId); }
        catch
        {
            if (packScope) document.Preview = previous; else chart!.Preview = previous;
            throw;
        }
    }
}

/// <summary>Creates a chart entry and its authored chart data as one undoable operation.</summary>
public sealed class AddChartCommand(EditableChart chart, int? index = null) : IEditorCommand
{
    private readonly EditableChart _chart = chart.Clone();
    private readonly int? _requestedIndex = index;
    private string? _previousSelectedChartId;
    private int _index;
    private bool _executed;

    public void Execute(EditorDocument document)
    {
        _previousSelectedChartId ??= document.SelectedChartId;
        _index = _executed ? _index : _requestedIndex ?? document.Charts.Count;
        document.InsertChart(_index, _chart);
        try { EditorChartCommandRules.Validate(document, _chart.Id); }
        catch
        {
            document.Charts.RemoveAt(_index);
            throw;
        }
        document.SelectChart(_chart.Id);
        _executed = true;
    }

    public void Undo(EditorDocument document)
    {
        document.RemoveChart(_chart.Id);
        document.SelectChart(_previousSelectedChartId ?? throw new InvalidOperationException("Add chart command has not executed."));
    }
}

/// <summary>Copies an existing chart so a new difficulty starts from valid authored data.</summary>
public sealed class CopyChartCommand(string sourceChartId, string chartId, string file, int? index = null) : IEditorCommand
{
    private readonly int? _requestedIndex = index;
    private EditableChart? _copy;
    private string? _previousSelectedChartId;
    private int _index;

    public void Execute(EditorDocument document)
    {
        _previousSelectedChartId ??= document.SelectedChartId;
        if (_copy is null)
        {
            var source = document.Charts.SingleOrDefault(item => item.Id == sourceChartId)
                ?? throw new KeyNotFoundException($"Chart '{sourceChartId}' is not open.");
            _copy = source.Clone();
            _copy.Id = chartId;
            _copy.File = file;
        }
        _index = _index == 0 && _requestedIndex is null ? document.Charts.Count : _index;
        if (_requestedIndex is not null) _index = _requestedIndex.Value;
        document.InsertChart(_index, _copy);
        try { EditorChartCommandRules.Validate(document, _copy.Id); }
        catch
        {
            document.Charts.RemoveAt(_index);
            throw;
        }
        document.SelectChart(_copy.Id);
    }

    public void Undo(EditorDocument document)
    {
        if (_copy is null) throw new InvalidOperationException("Copy chart command has not executed.");
        document.RemoveChart(_copy.Id);
        document.SelectChart(_previousSelectedChartId ?? throw new InvalidOperationException("Copy chart command has not executed."));
    }
}

public sealed class DeleteChartCommand(string chartId) : IEditorCommand
{
    private EditableChart? _deleted;
    private int _index;
    private string? _previousSelectedChartId;

    public void Execute(EditorDocument document)
    {
        _previousSelectedChartId ??= document.SelectedChartId;
        var removed = document.RemoveChart(chartId);
        _deleted = removed.Chart;
        _index = removed.Index;
    }

    public void Undo(EditorDocument document)
    {
        if (_deleted is null) throw new InvalidOperationException("Delete chart command has not executed.");
        document.InsertChart(_index, _deleted);
        document.SelectChart(_previousSelectedChartId ?? _deleted.Id);
    }
}

public sealed class MoveChartCommand(string chartId, int destinationIndex) : IEditorCommand
{
    private int _sourceIndex = -1;

    public void Execute(EditorDocument document)
    {
        _sourceIndex = document.MoveChart(chartId, destinationIndex);
    }

    public void Undo(EditorDocument document)
    {
        if (_sourceIndex < 0) throw new InvalidOperationException("Move chart command has not executed.");
        document.MoveChart(chartId, _sourceIndex);
    }
}

/// <summary>Edits metadata stored in a chart entry while preserving its chart identity and file binding.</summary>
public sealed class EditChartDetailsCommand(
    string chartId,
    V2Difficulty difficulty,
    string? difficultyKey,
    int? level,
    bool unrated,
    IReadOnlyList<string> charters) : IEditorCommand
{
    private readonly string[] _charters = charters?.ToArray() ?? throw new ArgumentNullException(nameof(charters));
    private ChartDetails? _before;

    public void Execute(EditorDocument document)
    {
        var chart = document.Charts.SingleOrDefault(item => item.Id == chartId)
            ?? throw new KeyNotFoundException($"Chart '{chartId}' is not open.");
        _before ??= ChartDetails.From(chart);
        Apply(document, chart, new ChartDetails(difficulty, difficultyKey, level, unrated, _charters));
    }

    public void Undo(EditorDocument document)
    {
        var chart = document.Charts.SingleOrDefault(item => item.Id == chartId)
            ?? throw new KeyNotFoundException($"Chart '{chartId}' is not open.");
        Apply(document, chart, _before ?? throw new InvalidOperationException("Chart details command has not executed."));
    }

    private static void Apply(EditorDocument document, EditableChart chart, ChartDetails values)
    {
        var previous = ChartDetails.From(chart);
        chart.Difficulty = values.Difficulty;
        chart.DifficultyKey = values.DifficultyKey;
        chart.Level = values.Level;
        chart.Unrated = values.Unrated;
        chart.Charters.Clear();
        chart.Charters.AddRange(values.Charters);
        try { EditorChartCommandRules.Validate(document, chart.Id); }
        catch
        {
            chart.Difficulty = previous.Difficulty;
            chart.DifficultyKey = previous.DifficultyKey;
            chart.Level = previous.Level;
            chart.Unrated = previous.Unrated;
            chart.Charters.Clear();
            chart.Charters.AddRange(previous.Charters);
            throw;
        }
    }

    private sealed record ChartDetails(V2Difficulty Difficulty, string? DifficultyKey, int? Level,
        bool Unrated, IReadOnlyList<string> Charters)
    {
        public static ChartDetails From(EditableChart chart) => new(chart.Difficulty, chart.DifficultyKey,
            chart.Level, chart.Unrated, chart.Charters.ToArray());
    }
}

/// <summary>Atomically changes a chart entry id and storage path.</summary>
public sealed class RenameChartCommand(string chartId, string newChartId, string newFile) : IEditorCommand
{
    private string? _oldFile;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var chart = document.Charts.SingleOrDefault(item => item.Id == chartId)
            ?? throw new KeyNotFoundException($"Chart '{chartId}' is not open.");
        if (!_captured) _oldFile = chart.File;
        Apply(document, chart, newChartId, newFile);
        _captured = true;
    }

    public void Undo(EditorDocument document)
    {
        var chart = document.Charts.SingleOrDefault(item => item.Id == newChartId)
            ?? throw new KeyNotFoundException($"Chart '{newChartId}' is not open.");
        Apply(document, chart, chartId, _oldFile ?? throw new InvalidOperationException("Rename command has not executed."));
    }

    private static void Apply(EditorDocument document, EditableChart chart, string id, string file)
    {
        var oldId = chart.Id;
        var oldFile = chart.File;
        chart.Id = id;
        chart.File = file;
        try
        {
            EditorChartCommandRules.Validate(document, id);
            document.SelectChart(id);
        }
        catch
        {
            chart.Id = oldId;
            chart.File = oldFile;
            throw;
        }
    }
}

/// <summary>Replaces a pack/chart media reference and stages its external source for the next save.</summary>
public sealed class ReplaceChartResourceCommand(string chartId, bool packScope, bool coverScope,
    string packageRelativePath, string externalSourcePath) : IEditorCommand
{
    private string? _oldReference;
    private string? _oldPendingSource;

    public void Execute(EditorDocument document)
    {
        var chart = document.Charts.SingleOrDefault(item => item.Id == chartId);
        if (!packScope && chart is null)
            throw new KeyNotFoundException($"Chart '{chartId}' is not open.");
        if (!File.Exists(externalSourcePath))
            throw new FileNotFoundException("External resource does not exist.", externalSourcePath);
        _oldPendingSource ??= document.GetPendingResource(packageRelativePath);
        if (packScope)
        {
            _oldReference ??= coverScope ? document.Cover : document.Audio;
            if (coverScope) document.Cover = packageRelativePath; else document.Audio = packageRelativePath;
        }
        else
        {
            _oldReference ??= chart!.Audio;
            chart!.Audio = packageRelativePath;
        }
        try
        {
            document.SetPendingResource(packageRelativePath, externalSourcePath);
            EditorChartCommandRules.Validate(document, chartId);
        }
        catch
        {
            if (packScope) { if (coverScope) document.Cover = _oldReference; else document.Audio = _oldReference; }
            else chart!.Audio = _oldReference;
            RestorePending(document);
            throw;
        }
    }

    public void Undo(EditorDocument document)
    {
        var chart = document.Charts.SingleOrDefault(item => item.Id == chartId);
        if (packScope) { if (coverScope) document.Cover = _oldReference; else document.Audio = _oldReference; }
        else if (chart is not null) chart.Audio = _oldReference;
        RestorePending(document);
    }

    private void RestorePending(EditorDocument document)
    {
        if (_oldPendingSource is null) document.RemovePendingResource(packageRelativePath);
        else document.SetPendingResource(packageRelativePath, _oldPendingSource);
    }
}

/// <summary>Removes Chart-level overrides so playback inherits Pack media/preview values.</summary>
public sealed class ClearChartOverridesCommand(string chartId, bool clearAudio, bool clearPreview) : IEditorCommand
{
    private string? _oldAudio;
    private V2Preview? _oldPreview;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var chart = document.Charts.SingleOrDefault(item => item.Id == chartId)
            ?? throw new KeyNotFoundException($"Chart '{chartId}' is not open.");
        if (!_captured)
        {
            _oldAudio = chart.Audio;
            _oldPreview = chart.Preview;
            _captured = true;
        }
        Apply(document, chart, clearAudio ? null : chart.Audio, clearPreview ? null : chart.Preview);
    }

    public void Undo(EditorDocument document)
    {
        var chart = document.Charts.SingleOrDefault(item => item.Id == chartId)
            ?? throw new KeyNotFoundException($"Chart '{chartId}' is not open.");
        Apply(document, chart, _oldAudio, _oldPreview);
    }

    private static void Apply(EditorDocument document, EditableChart chart, string? audio, V2Preview? preview)
    {
        var oldAudio = chart.Audio;
        var oldPreview = chart.Preview;
        chart.Audio = audio;
        chart.Preview = preview;
        try { EditorChartCommandRules.Validate(document, chart.Id); }
        catch
        {
            chart.Audio = oldAudio;
            chart.Preview = oldPreview;
            throw;
        }
    }
}

internal static class EditorChartCommandRules
{
    public static void Validate(EditorDocument document, string chartId)
    {
        V2SemanticValidator.ValidatePack(document.BuildPackSnapshot());
        _ = V2JsonDecoder.DecodePack(V2JsonEncoder.EncodePack(document.BuildPackSnapshot()),
            "editor chart metadata");
    }
}

public sealed class AddNoteCommand(EditableNote note) : IEditorCommand
{
    private readonly EditableNote _note = note.Clone();

    public void Execute(EditorDocument document)
    {
        EditorCommandRules.ValidateNote(_note);
        var ids = new[] { _note.Id }.Concat(_note.Nodes.Select(node => node.Id)).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidOperationException($"Note '{_note.Id}' contains duplicate parent/node IDs.");
        foreach (var id in ids)
            document.RequireChartWideIdAvailable(id);

        document.SelectedChart.Notes.Add(_note.Clone());
        document.Select(new EditorSelection(EditorSelectionKind.Note, _note.Id));
    }

    public void Undo(EditorDocument document)
    {
        document.SelectedChart.Notes.RemoveAll(item => item.Id == _note.Id);
        document.Select(EditorSelection.None);
    }
}

public sealed class DeleteNoteCommand(string noteId) : IEditorCommand
{
    private EditableNote? _deleted;
    private int _index;

    public void Execute(EditorDocument document)
    {
        _index = document.SelectedChart.Notes.FindIndex(item => item.Id == noteId);
        if (_index < 0)
            throw new KeyNotFoundException($"Note '{noteId}' was not found.");
        _deleted = document.SelectedChart.Notes[_index].Clone();
        document.SelectedChart.Notes.RemoveAt(_index);
        document.Select(EditorSelection.None);
    }

    public void Undo(EditorDocument document)
    {
        if (_deleted is null)
            throw new InvalidOperationException("Delete command has not executed.");
        document.SelectedChart.Notes.Insert(_index, _deleted.Clone());
        document.Select(new EditorSelection(EditorSelectionKind.Note, _deleted.Id));
    }
}

public sealed class MoveNoteCommand(string noteId, ExactBarTime time, double center, double width) : IEditorCommand
{
    private ExactBarTime _oldTime;
    private double _oldCenter;
    private double _oldWidth;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        var candidate = note.Clone();
        candidate.Time = time;
        candidate.Center = center;
        candidate.Width = width;
        EditorCommandRules.ValidateNote(candidate);
        if (!_captured)
        {
            _oldTime = note.Time;
            _oldCenter = note.Center;
            _oldWidth = note.Width;
            _captured = true;
        }
        note.Time = time;
        note.Center = center;
        note.Width = width;
    }

    public void Undo(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        note.Time = _oldTime;
        note.Center = _oldCenter;
        note.Width = _oldWidth;
    }
}

/// <summary>Moves a Hold/Mixer head and all of its path nodes by the same exact bar delta.</summary>
public sealed class MovePathNoteCommand(string noteId, ExactBarTime time, double center,
    double width) : IEditorCommand
{
    private ExactBarTime _oldTime;
    private double _oldCenter;
    private double _oldWidth;
    private ExactBarTime _delta;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        if (!_captured)
        {
            _oldTime = note.Time;
            _oldCenter = note.Center;
            _oldWidth = note.Width;
            _delta = time - note.Time;
            _captured = true;
        }
        var candidate = note.Clone();
        candidate.Time = time;
        candidate.Center = center;
        candidate.Width = width;
        foreach (var node in candidate.Nodes)
            node.Time = node.Time + _delta;
        EditorCommandRules.ValidateNote(candidate);

        note.Time = time;
        note.Center = center;
        note.Width = width;
        foreach (var node in note.Nodes)
            node.Time = node.Time + _delta;
        document.Select(new EditorSelection(EditorSelectionKind.Note, noteId));
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Move path command has not executed.");
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        note.Time = _oldTime;
        note.Center = _oldCenter;
        note.Width = _oldWidth;
        foreach (var node in note.Nodes)
            node.Time = node.Time - _delta;
        document.Select(new EditorSelection(EditorSelectionKind.Note, noteId));
    }
}

public sealed class RenameNoteCommand(string noteId, string newId) : IEditorCommand
{
    public void Execute(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        EditorCommandRules.ValidateId(newId, "Note");
        document.RequireChartWideIdAvailable(newId, noteId);
        note.Id = newId;
        document.Select(new EditorSelection(EditorSelectionKind.Note, newId, note.Key));
    }

    public void Undo(EditorDocument document)
    {
        var note = document.RequireNote(newId);
        document.RequireChartWideIdAvailable(noteId, newId);
        note.Id = noteId;
        document.Select(new EditorSelection(EditorSelectionKind.Note, noteId, note.Key));
    }
}

public sealed class SetNoteTrackCommand(string noteId, EditorTrack track) : IEditorCommand
{
    private EditorTrack _before;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        if (!_captured) { _before = note.Track; _captured = true; }
        note.Track = track;
        document.Select(new EditorSelection(EditorSelectionKind.Note, note.Id, note.Key));
    }

    public void Undo(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        note.Track = _before;
        document.Select(new EditorSelection(EditorSelectionKind.Note, note.Id, note.Key));
    }
}

public sealed class RenamePathNodeCommand(string noteId, string nodeId, string newId) : IEditorCommand
{
    public void Execute(EditorDocument document)
    {
        var node = document.RequirePathNode(noteId, nodeId);
        EditorCommandRules.ValidateId(newId, "Path node");
        document.RequireChartWideIdAvailable(newId, nodeId);
        node.Id = newId;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, newId, node.Key));
    }

    public void Undo(EditorDocument document)
    {
        var node = document.RequirePathNode(noteId, newId);
        document.RequireChartWideIdAvailable(nodeId, newId);
        node.Id = nodeId;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId, node.Key));
    }
}

public sealed class ChangeNoteTypeCommand : IEditorCommand
{
    private readonly string _noteId;
    private readonly V2NoteType _newType;
    private readonly EditablePathNode? _requestedTerminal;
    private EditableNote? _before;
    private EditableNote? _after;
    private int _index;

    public ChangeNoteTypeCommand(string noteId, V2NoteType newType)
        : this(noteId, newType, null)
    {
    }

    public ChangeNoteTypeCommand(string noteId, V2NoteType newType, EditablePathNode? pathTerminal)
    {
        _noteId = noteId;
        _newType = newType;
        _requestedTerminal = pathTerminal is null ? null : EditorCommandRules.CloneNode(pathTerminal);
    }

    public void Execute(EditorDocument document)
    {
        if (!Enum.IsDefined(_newType))
            throw new InvalidOperationException($"Unknown v2 note type '{_newType}'.");
        _index = document.SelectedChart.Notes.FindIndex(note => note.Id == _noteId);
        if (_index < 0)
            throw new KeyNotFoundException($"Note '{_noteId}' was not found.");

        if (_after is null)
        {
            _before = document.SelectedChart.Notes[_index].Clone();
            try
            {
                _after = BuildChangedNote(document, _before);
                EditorCommandRules.ValidateNote(_after);
            }
            catch
            {
                _before = null;
                throw;
            }
        }
        document.SelectedChart.Notes[_index] = _after.Clone();
        document.Select(new EditorSelection(EditorSelectionKind.Note, _noteId));
    }

    public void Undo(EditorDocument document)
    {
        if (_before is null)
            throw new InvalidOperationException("Change note type command has not executed.");
        document.SelectedChart.Notes[_index] = _before.Clone();
        document.Select(new EditorSelection(EditorSelectionKind.Note, _noteId));
    }

    private EditableNote BuildChangedNote(EditorDocument document, EditableNote source)
    {
        var changed = source.Clone();
        changed.Type = _newType;
        if (_newType is not (V2NoteType.Hold or V2NoteType.Mixer))
        {
            changed.CurveToNext = null;
            changed.Nodes.Clear();
            return changed;
        }

        if (changed.Nodes.Count == 0)
        {
            EditablePathNode terminal;
            if (_requestedTerminal is not null)
            {
                terminal = EditorCommandRules.CloneNode(_requestedTerminal);
                document.RequireChartWideIdAvailable(terminal.Id);
            }
            else
            {
                if (document.GridDivisor <= 0)
                    throw new InvalidOperationException("Grid divisor must be positive when creating a path terminal.");
                terminal = new EditablePathNode
                {
                    Id = document.AllocateId(_newType == V2NoteType.Hold ? "hold-node" : "mixer-node"),
                    Time = changed.Time + ExactBarTime.FromFraction(1, document.GridDivisor),
                    Center = changed.Center,
                    Width = changed.Width,
                };
            }
            terminal.CurveToNext = null;
            changed.Nodes.Add(terminal);
        }

        foreach (var node in changed.Nodes)
            node.Judge = _newType == V2NoteType.Hold ? node.Judge ?? true : null;
        changed.Nodes[^1].CurveToNext = null;
        if (_newType == V2NoteType.Hold)
            changed.Nodes[^1].Judge = true;
        return changed;
    }
}

public sealed class AddPathNodeCommand(string noteId, EditablePathNode node) : IEditorCommand
{
    private readonly EditablePathNode _node = EditorCommandRules.CloneNode(node);
    private int _index;

    public void Execute(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        document.RequireChartWideIdAvailable(_node.Id);
        EditorCommandRules.ValidateId(_node.Id, "Path node");
        EditorCommandRules.ValidateTime(_node.Time, "Path node time");
        EditorCommandRules.ValidatePoint(_node.Center, _node.Width, "Path node");

        _index = note.Nodes.FindIndex(existing => existing.Time > _node.Time);
        if (_index < 0)
            _index = note.Nodes.Count;
        var previousTime = _index == 0 ? note.Time : note.Nodes[_index - 1].Time;
        if (_node.Time <= previousTime || _index < note.Nodes.Count && _node.Time >= note.Nodes[_index].Time)
            throw new InvalidOperationException("Path node times must be strictly increasing.");

        var added = EditorCommandRules.CloneNode(_node);
        if (note.Type == V2NoteType.Hold)
            added.Judge ??= true;
        else if (added.Judge is not null)
            throw new InvalidOperationException("Mixer path nodes must not carry judge state.");
        if (_index == note.Nodes.Count)
        {
            if (added.CurveToNext is not null)
                throw new InvalidOperationException("Tail path node must omit curveToNext.");
            if (note.Type == V2NoteType.Hold)
                added.Judge = true;
        }

        var candidate = note.Clone();
        candidate.Nodes.Insert(_index, EditorCommandRules.CloneNode(added));
        EditorCommandRules.ValidateNote(candidate);
        note.Nodes.Insert(_index, added);
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, added.Id));
    }

    public void Undo(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        if (_index >= note.Nodes.Count || note.Nodes[_index].Id != _node.Id)
            throw new InvalidOperationException("Added path node is no longer at its command-owned position.");
        note.Nodes.RemoveAt(_index);
        document.Select(new EditorSelection(EditorSelectionKind.Note, noteId));
    }
}

public sealed class EditPathNodeCommand(
    string noteId,
    string nodeId,
    ExactBarTime time,
    double center,
    double width) : IEditorCommand
{
    private ExactBarTime _oldTime;
    private double _oldCenter;
    private double _oldWidth;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        var index = note.Nodes.FindIndex(node => node.Id == nodeId);
        if (index < 0)
            throw new KeyNotFoundException($"Path node '{nodeId}' was not found in note '{noteId}'.");
        EditorCommandRules.ValidateTime(time, "Path node time");
        EditorCommandRules.ValidatePoint(center, width, "Path node");
        var previousTime = index == 0 ? note.Time : note.Nodes[index - 1].Time;
        if (time <= previousTime || index + 1 < note.Nodes.Count && time >= note.Nodes[index + 1].Time)
            throw new InvalidOperationException("Path node times must remain strictly increasing.");

        var node = note.Nodes[index];
        var candidate = note.Clone();
        candidate.Nodes[index].Time = time;
        candidate.Nodes[index].Center = center;
        candidate.Nodes[index].Width = width;
        EditorCommandRules.ValidateNote(candidate);
        if (!_captured)
        {
            _oldTime = node.Time;
            _oldCenter = node.Center;
            _oldWidth = node.Width;
            _captured = true;
        }
        node.Time = time;
        node.Center = center;
        node.Width = width;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Edit path node command has not executed.");
        var node = document.RequirePathNode(noteId, nodeId);
        node.Time = _oldTime;
        node.Center = _oldCenter;
        node.Width = _oldWidth;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
    }
}

public sealed class DeletePathNodeCommand(string noteId, string nodeId) : IEditorCommand
{
    private EditablePathNode? _deleted;
    private int _index;
    private V2PathCurve? _promotedTailCurve;
    private bool? _promotedTailJudge;
    private bool _changedPromotedTail;

    public void Execute(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        if (note.Nodes.Count == 1)
            throw new InvalidOperationException("Hold and Mixer require at least one path node.");
        _index = note.Nodes.FindIndex(node => node.Id == nodeId);
        if (_index < 0)
            throw new KeyNotFoundException($"Path node '{nodeId}' was not found in note '{noteId}'.");
        _deleted ??= EditorCommandRules.CloneNode(note.Nodes[_index]);

        var candidate = note.Clone();
        if (_index == candidate.Nodes.Count - 1)
        {
            candidate.Nodes[^2].CurveToNext = null;
            if (candidate.Type == V2NoteType.Hold)
                candidate.Nodes[^2].Judge = true;
        }
        candidate.Nodes.RemoveAt(_index);
        EditorCommandRules.ValidateNote(candidate);

        _changedPromotedTail = _index == note.Nodes.Count - 1;
        if (_changedPromotedTail)
        {
            var promoted = note.Nodes[^2];
            _promotedTailCurve = promoted.CurveToNext;
            if (note.Type == V2NoteType.Hold)
                _promotedTailJudge = promoted.Judge;
            promoted.CurveToNext = null;
            if (note.Type == V2NoteType.Hold)
                promoted.Judge = true;
        }
        note.Nodes.RemoveAt(_index);
        document.Select(new EditorSelection(EditorSelectionKind.Note, noteId));
    }

    public void Undo(EditorDocument document)
    {
        if (_deleted is null)
            throw new InvalidOperationException("Delete path node command has not executed.");
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        note.Nodes.Insert(_index, EditorCommandRules.CloneNode(_deleted));
        if (_changedPromotedTail)
        {
            var restoredPrevious = note.Nodes[_index - 1];
            restoredPrevious.CurveToNext = _promotedTailCurve;
            restoredPrevious.Judge = _promotedTailJudge;
        }
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
    }
}

public sealed class SetPathCurveCommand : IEditorCommand
{
    private readonly string _noteId;
    private readonly string? _nodeId;
    private readonly V2PathCurve? _curveToNext;
    private V2PathCurve? _oldCurve;
    private bool _captured;

    public SetPathCurveCommand(string noteId, V2PathCurve? curveToNext)
        : this(noteId, null, curveToNext)
    {
    }

    public SetPathCurveCommand(string noteId, string? nodeId, V2PathCurve? curveToNext)
    {
        _noteId = noteId;
        _nodeId = nodeId;
        _curveToNext = curveToNext;
    }

    public void Execute(EditorDocument document)
    {
        if (_curveToNext is not null && !Enum.IsDefined(_curveToNext.Value))
            throw new InvalidOperationException($"Unknown path curve '{_curveToNext}'.");
        var note = EditorCommandRules.RequirePathNote(document, _noteId);
        if (_nodeId is null)
        {
            var candidate = note.Clone();
            candidate.CurveToNext = _curveToNext;
            EditorCommandRules.ValidateNote(candidate);
            if (!_captured)
            {
                _oldCurve = note.CurveToNext;
                _captured = true;
            }
            note.CurveToNext = _curveToNext;
            document.Select(new EditorSelection(EditorSelectionKind.Note, _noteId));
            return;
        }

        var index = note.Nodes.FindIndex(node => node.Id == _nodeId);
        if (index < 0)
            throw new KeyNotFoundException($"Path node '{_nodeId}' was not found in note '{_noteId}'.");
        if (index == note.Nodes.Count - 1)
            throw new InvalidOperationException("Tail path node has no outgoing curve.");
        var node = note.Nodes[index];
        var nodeCandidate = note.Clone();
        nodeCandidate.Nodes[index].CurveToNext = _curveToNext;
        EditorCommandRules.ValidateNote(nodeCandidate);
        if (!_captured)
        {
            _oldCurve = node.CurveToNext;
            _captured = true;
        }
        node.CurveToNext = _curveToNext;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, _nodeId));
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Set path curve command has not executed.");
        var note = EditorCommandRules.RequirePathNote(document, _noteId);
        if (_nodeId is null)
        {
            note.CurveToNext = _oldCurve;
            document.Select(new EditorSelection(EditorSelectionKind.Note, _noteId));
        }
        else
        {
            document.RequirePathNode(_noteId, _nodeId).CurveToNext = _oldCurve;
            document.Select(new EditorSelection(EditorSelectionKind.PathNode, _nodeId));
        }
    }
}

public sealed class SetHoldJudgeCommand(string noteId, string nodeId, bool judge) : IEditorCommand
{
    private bool? _oldJudge;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        if (note.Type != V2NoteType.Hold)
            throw new InvalidOperationException("Only Hold path nodes carry judge state.");
        var index = note.Nodes.FindIndex(node => node.Id == nodeId);
        if (index < 0)
            throw new KeyNotFoundException($"Path node '{nodeId}' was not found in note '{noteId}'.");
        if (index == note.Nodes.Count - 1 && !judge)
            throw new InvalidOperationException("Hold tail must keep judge:true.");
        var node = note.Nodes[index];
        var candidate = note.Clone();
        candidate.Nodes[index].Judge = judge;
        EditorCommandRules.ValidateNote(candidate);
        if (!_captured)
        {
            _oldJudge = node.Judge;
            _captured = true;
        }
        node.Judge = judge;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Set Hold judge command has not executed.");
        document.RequirePathNode(noteId, nodeId).Judge = _oldJudge;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
    }
}

public sealed class EditBpmCommand(ExactBarTime originalTime, ExactBarTime newTime, double newBpm) : IEditorCommand
{
    private double _oldBpm;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var bpm = document.RequireBpm(originalTime);
        EditorCommandRules.ValidateTime(newTime, "BPM time");
        if (!double.IsFinite(newBpm) || newBpm <= 0)
            throw new InvalidOperationException("BPM must be finite and greater than zero.");
        if (originalTime == ExactBarTime.Zero && newTime != ExactBarTime.Zero)
            throw new InvalidOperationException("The base BPM event at bar 0 cannot be moved.");
        if (newTime != originalTime && document.SelectedChart.Bpms.Any(item => item.Time == newTime))
            throw new InvalidOperationException($"BPM already exists at {newTime}.");
        if (!_captured)
        {
            _oldBpm = bpm.Bpm;
            _captured = true;
        }
        bpm.Time = newTime;
        bpm.Bpm = newBpm;
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, ExactBarTimeText.Format(newTime)));
    }

    public void Undo(EditorDocument document)
    {
        var bpm = document.RequireBpm(newTime);
        bpm.Time = originalTime;
        bpm.Bpm = _oldBpm;
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, ExactBarTimeText.Format(originalTime)));
    }
}

public sealed class AddBpmCommand(ExactBarTime time, double bpm) : IEditorCommand
{
    private readonly EditorEntityKey _key = EditorEntityKey.New();

    public void Execute(EditorDocument document)
    {
        EditorCommandRules.ValidateTime(time, "BPM time");
        if (!double.IsFinite(bpm) || bpm <= 0)
            throw new InvalidOperationException("BPM must be finite and greater than zero.");
        if (document.SelectedChart.Bpms.Any(item => item.Time == time))
            throw new InvalidOperationException($"BPM already exists at {time}.");
        document.SelectedChart.Bpms.Add(new EditableBpm { Key = _key, Time = time, Bpm = bpm });
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, ExactBarTimeText.Format(time)));
    }

    public void Undo(EditorDocument document)
    {
        document.SelectedChart.Bpms.RemoveAll(item => item.Time == time);
        document.Select(EditorSelection.None);
    }
}

public sealed class DeleteBpmCommand(ExactBarTime time) : IEditorCommand
{
    private EditableBpm? _deleted;
    private int _index;

    public void Execute(EditorDocument document)
    {
        if (time == ExactBarTime.Zero)
            throw new InvalidOperationException("The base BPM event at bar 0 cannot be deleted.");
        _index = document.SelectedChart.Bpms.FindIndex(item => item.Time == time);
        if (_index < 0)
            throw new KeyNotFoundException($"BPM at '{time}' was not found.");
        _deleted = document.SelectedChart.Bpms[_index];
        document.SelectedChart.Bpms.RemoveAt(_index);
        document.Select(EditorSelection.None);
    }

    public void Undo(EditorDocument document)
    {
        if (_deleted is null)
            throw new InvalidOperationException("Delete BPM command has not executed.");
        document.SelectedChart.Bpms.Insert(_index, _deleted);
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, ExactBarTimeText.Format(_deleted.Time)));
    }
}

public sealed class AddScrollCommand : IEditorCommand
{
    private readonly EditableScroll _scroll;
    private int _index;

    public AddScrollCommand(ExactBarTime time, double value, V2ScrollCurve? curveToNext = null)
        : this(new EditableScroll { Time = time, Value = value, CurveToNext = curveToNext })
    {
    }

    public AddScrollCommand(EditableScroll scroll)
    {
        ArgumentNullException.ThrowIfNull(scroll);
        _scroll = scroll.Clone();
    }

    public void Execute(EditorDocument document)
    {
        var timeline = document.SelectedChart.ScrollSpeeds;
        if (timeline.Any(item => item.Time == _scroll.Time))
            throw new InvalidOperationException($"Scroll event already exists at {_scroll.Time}.");
        var proposed = timeline.Select(item => item.Clone()).Append(_scroll.Clone())
            .OrderBy(item => item.Time).ToList();
        EditorCommandRules.ValidateScrollTimeline(proposed);
        _index = proposed.FindIndex(item => item.Time == _scroll.Time);
        timeline.Insert(_index, _scroll.Clone());
        document.Select(new EditorSelection(EditorSelectionKind.Scroll,
            ExactBarTimeText.Format(_scroll.Time)));
    }

    public void Undo(EditorDocument document)
    {
        var timeline = document.SelectedChart.ScrollSpeeds;
        var index = timeline.FindIndex(item => item.Time == _scroll.Time);
        if (index < 0)
            throw new InvalidOperationException("Added Scroll event is no longer present.");
        timeline.RemoveAt(index);
        document.Select(EditorSelection.None);
    }
}

public sealed class EditScrollCommand(
    ExactBarTime originalTime,
    ExactBarTime newTime,
    double newValue,
    V2ScrollCurve? newCurveToNext) : IEditorCommand
{
    private EditableScroll? _before;

    public void Execute(EditorDocument document)
    {
        var timeline = document.SelectedChart.ScrollSpeeds;
        var scroll = document.RequireScroll(originalTime);
        if (originalTime == ExactBarTime.Zero && newTime != ExactBarTime.Zero)
            throw new InvalidOperationException("The base Scroll event at bar 0 cannot be moved.");
        if (newTime != originalTime && timeline.Any(item => item.Time == newTime))
            throw new InvalidOperationException($"Scroll event already exists at {newTime}.");

        var proposed = timeline.Select(item => item.Time == originalTime
                ? new EditableScroll { Time = newTime, Value = newValue, CurveToNext = newCurveToNext }
                : item.Clone())
            .OrderBy(item => item.Time).ToList();
        EditorCommandRules.ValidateScrollTimeline(proposed);
        _before ??= scroll.Clone();
        scroll.Time = newTime;
        scroll.Value = newValue;
        scroll.CurveToNext = newCurveToNext;
        timeline.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Scroll,
            ExactBarTimeText.Format(newTime)));
    }

    public void Undo(EditorDocument document)
    {
        if (_before is null)
            throw new InvalidOperationException("Edit Scroll command has not executed.");
        var timeline = document.SelectedChart.ScrollSpeeds;
        var scroll = document.RequireScroll(newTime);
        scroll.Time = _before.Time;
        scroll.Value = _before.Value;
        scroll.CurveToNext = _before.CurveToNext;
        timeline.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Scroll,
            ExactBarTimeText.Format(_before.Time)));
    }
}

public sealed class DeleteScrollCommand(ExactBarTime time) : IEditorCommand
{
    private EditableScroll? _deleted;
    private int _index;
    private V2ScrollCurve? _promotedTailCurve;
    private bool _changedPromotedTail;

    public void Execute(EditorDocument document)
    {
        var timeline = document.SelectedChart.ScrollSpeeds;
        _index = timeline.FindIndex(item => item.Time == time);
        if (_index < 0)
            throw new KeyNotFoundException($"Scroll event at '{time}' was not found.");
        if (_index == 0 && timeline.Count > 1)
            throw new InvalidOperationException("Bar 0 Scroll cannot be deleted while later Scroll events remain.");
        _deleted ??= timeline[_index].Clone();
        _changedPromotedTail = _index == timeline.Count - 1 && _index > 0;
        if (_changedPromotedTail)
        {
            _promotedTailCurve = timeline[_index - 1].CurveToNext;
            timeline[_index - 1].CurveToNext = null;
        }
        timeline.RemoveAt(_index);
        document.Select(EditorSelection.None);
    }

    public void Undo(EditorDocument document)
    {
        if (_deleted is null)
            throw new InvalidOperationException("Delete Scroll command has not executed.");
        var timeline = document.SelectedChart.ScrollSpeeds;
        timeline.Insert(_index, _deleted.Clone());
        if (_changedPromotedTail)
            timeline[_index - 1].CurveToNext = _promotedTailCurve;
        document.Select(new EditorSelection(EditorSelectionKind.Scroll,
            ExactBarTimeText.Format(_deleted.Time)));
    }
}

public sealed class EditChartOffsetCommand(double audioOffsetSec) : IEditorCommand
{
    private double _oldOffset;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        if (!double.IsFinite(audioOffsetSec))
            throw new InvalidOperationException("Chart audio offset must be finite.");
        if (!_captured)
        {
            _oldOffset = document.SelectedChart.AudioOffsetSec;
            _captured = true;
        }
        document.SelectedChart.AudioOffsetSec = audioOffsetSec;
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Chart offset command has not executed.");
        document.SelectedChart.AudioOffsetSec = _oldOffset;
    }
}

public sealed class EditPathTerminalCommand(
    string noteId,
    ExactBarTime time,
    double center,
    double width) : IEditorCommand
{
    private ExactBarTime _oldTime;
    private double _oldCenter;
    private double _oldWidth;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        var terminal = note.Nodes[^1];
        var previousTime = note.Nodes.Count == 1 ? note.Time : note.Nodes[^2].Time;
        EditorCommandRules.ValidateTime(time, "Path terminal time");
        EditorCommandRules.ValidatePoint(center, width, "Path terminal");
        if (time <= previousTime)
            throw new InvalidOperationException("Path terminal must be later than the previous point.");

        var candidate = note.Clone();
        candidate.Nodes[^1].Time = time;
        candidate.Nodes[^1].Center = center;
        candidate.Nodes[^1].Width = width;
        EditorCommandRules.ValidateNote(candidate);
        if (!_captured)
        {
            _oldTime = terminal.Time;
            _oldCenter = terminal.Center;
            _oldWidth = terminal.Width;
            _captured = true;
        }
        terminal.Time = time;
        terminal.Center = center;
        terminal.Width = width;
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Path terminal command has not executed.");
        var terminal = EditorCommandRules.RequirePathNote(document, noteId).Nodes[^1];
        terminal.Time = _oldTime;
        terminal.Center = _oldCenter;
        terminal.Width = _oldWidth;
    }
}

internal static class EditorCommandRules
{
    public static EditableNote RequirePathNote(EditorDocument document, string noteId)
    {
        var note = document.RequireNote(noteId);
        if (note.Type is not (V2NoteType.Hold or V2NoteType.Mixer) || note.Nodes.Count == 0)
            throw new InvalidOperationException($"Note '{noteId}' has no editable path.");
        return note;
    }

    public static void ValidateNote(EditableNote note)
    {
        if (!Enum.IsDefined(note.Type))
            throw new InvalidOperationException($"Unknown v2 note type '{note.Type}'.");
        ValidateId(note.Id, "Note");
        ValidateTime(note.Time, "Note time");
        ValidatePoint(note.Center, note.Width, "Note");
        if (note.CurveToNext is not null && !Enum.IsDefined(note.CurveToNext.Value))
            throw new InvalidOperationException($"Unknown path curve '{note.CurveToNext}'.");
        if (note.Type is not (V2NoteType.Hold or V2NoteType.Mixer))
        {
            if (note.Nodes.Count != 0 || note.CurveToNext is not null)
                throw new InvalidOperationException("Only Hold and Mixer may contain path data.");
            return;
        }
        if (note.Nodes.Count == 0)
            throw new InvalidOperationException("Hold and Mixer require at least one path node.");

        var previous = note.Time;
        for (var index = 0; index < note.Nodes.Count; index++)
        {
            var node = note.Nodes[index];
            ValidateId(node.Id, "Path node");
            ValidateTime(node.Time, "Path node time");
            ValidatePoint(node.Center, node.Width, "Path node");
            if (node.Time <= previous)
                throw new InvalidOperationException("Path node times must be strictly increasing.");
            previous = node.Time;
            if (index == note.Nodes.Count - 1 && node.CurveToNext is not null)
                throw new InvalidOperationException("Tail path node must omit curveToNext.");
            if (node.CurveToNext is not null && !Enum.IsDefined(node.CurveToNext.Value))
                throw new InvalidOperationException($"Unknown path curve '{node.CurveToNext}'.");
            if (note.Type == V2NoteType.Hold && node.Judge is null)
                throw new InvalidOperationException("Hold path nodes must resolve judge to true or false.");
            if (note.Type == V2NoteType.Mixer && node.Judge is not null)
                throw new InvalidOperationException("Mixer path nodes must not carry judge state.");
        }
        if (note.Type == V2NoteType.Hold && note.Nodes[^1].Judge != true)
            throw new InvalidOperationException("Hold tail must keep judge:true.");

        var path = new V2PathNote
        {
            Id = note.Id,
            Type = note.Type,
            Time = note.Time,
            Center = note.Center,
            Width = note.Width,
            CurveToNext = note.CurveToNext,
            Nodes = note.Nodes.Select((node, index) => new V2PathNode
            {
                Id = node.Id,
                Time = node.Time,
                Center = node.Center,
                Width = node.Width,
                CurveToNext = node.CurveToNext,
                Judge = note.Type == V2NoteType.Hold ? node.Judge : null,
                JudgeWasExplicit = note.Type == V2NoteType.Hold &&
                    (node.Judge is not null || index + 1 == note.Nodes.Count),
            }).ToArray(),
        };
        var evaluator = new V2PathEvaluator(path);
        if (!evaluator.IsFiniteAndPositive(out _, out var reason))
            throw new InvalidOperationException(reason);
    }

    public static void ValidateScrollTimeline(IReadOnlyList<EditableScroll> timeline)
    {
        if (timeline.Count > 0 && timeline[0].Time != ExactBarTime.Zero)
            throw new InvalidOperationException("First nonempty Scroll event must be at bar 0.");
        for (var index = 0; index < timeline.Count; index++)
        {
            var scroll = timeline[index];
            ValidateTime(scroll.Time, "Scroll time");
            if (!double.IsFinite(scroll.Value) || scroll.Value <= 0 || scroll.Value > 64)
                throw new InvalidOperationException("Scroll value must be finite and in (0,64].");
            if (index > 0 && scroll.Time <= timeline[index - 1].Time)
                throw new InvalidOperationException("Scroll event times must be strictly increasing.");
            if (scroll.CurveToNext is not null && !Enum.IsDefined(scroll.CurveToNext.Value))
                throw new InvalidOperationException($"Unknown Scroll curve '{scroll.CurveToNext}'.");
            if (index == timeline.Count - 1 && scroll.CurveToNext is not null)
                throw new InvalidOperationException("Tail Scroll event must omit curveToNext.");
        }
    }

    public static void ValidateTime(ExactBarTime time, string label)
    {
        if (!time.IsJsonSafeCanonical)
            throw new InvalidOperationException($"{label} must be a non-negative JSON-safe exact BarTime.");
    }

    public static void ValidatePoint(double center, double width, string label)
    {
        if (!double.IsFinite(center) || !double.IsFinite(width) || width <= 0)
            throw new InvalidOperationException($"{label} center/width is invalid.");
    }

    public static EditablePathNode CloneNode(EditablePathNode node) => new()
    {
        Key = node.Key,
        Id = node.Id,
        Time = node.Time,
        Center = node.Center,
        Width = node.Width,
        CurveToNext = node.CurveToNext,
        Judge = node.Judge,
    };

    public static void ValidateId(string id, string label)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || !IsAsciiAlphaNumeric(id[0]) ||
            id.Skip(1).Any(character => !IsAsciiAlphaNumeric(character) && character is not ('.' or '_' or '-')))
            throw new InvalidOperationException($"{label} ID must match ^[A-Za-z0-9][A-Za-z0-9._-]{{0,63}}$.");
    }

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
