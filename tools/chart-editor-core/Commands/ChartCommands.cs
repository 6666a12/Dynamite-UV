using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

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
