using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

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
