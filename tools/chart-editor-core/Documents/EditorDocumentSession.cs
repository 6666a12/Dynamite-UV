using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public enum EditorSaveKind { Created, Saved }

public sealed record EditorSaveResult(EditorSaveKind Kind, string DestinationDirectory, int ChartCount);

/// <summary>
/// Owns the open document and the external resources needed for its first save.
/// Page navigation, dialogs, rendering and error presentation belong to the host.
/// Failed opens/creates/writes never replace the current document or mark it saved.
/// </summary>
public sealed class EditorDocumentSession
{
    public EditorDocument? Document { get; private set; }
    public EditorProjectDraft? Draft { get; private set; }

    public EditorDocument Open(string directory)
    {
        var opened = EditorPackageRepository.Open(directory);
        Document = opened;
        Draft = null;
        return opened;
    }

    public EditorDocument CreateDraft(EditorProjectDraftRequest request)
    {
        var created = EditorProjectDraftFactory.Create(request);
        Document = created.Document;
        Draft = created;
        return created.Document;
    }

    public void AddChart(EditableChart chart)
    {
        var document = Document ?? throw new InvalidOperationException("没有打开的包。");
        // Adding a difficulty does not change the package's first-save resource ownership.
        // In particular, an unpersisted draft must retain its external audio and cover.
        document.Execute(new AddChartCommand(chart));
    }

    public EditorSaveResult Save(string destinationDirectory)
    {
        var document = Document ?? throw new InvalidOperationException("没有可保存的文档。");
        V2PackageWriteResult result;
        EditorSaveKind kind;
        if (document.IsUnpersisted)
        {
            var draft = Draft ?? throw new InvalidOperationException("内存草稿缺少外部资源，无法创建包。");
            var request = draft.BuildPackageCreateRequest(destinationDirectory, allowEmptyDestination: true);
            // Resource changes applied to the draft (audio/cover replacements, chart audio
            // overrides) stage their sources as pending replacements. The first save must publish
            // them instead of the draft's original external resources, otherwise the new package
            // silently keeps the pre-settings media.
            var pending = document.PendingResourceReplacements;
            if (pending.Count > 0)
                request = request with { ExternalResources = MergeFirstSaveResources(document, request.ExternalResources, pending) };
            result = V2PackageWriter.Create(request);
            kind = EditorSaveKind.Created;
        }
        else
        {
            result = V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = document.PackageDirectory!,
                DestinationDirectory = destinationDirectory,
                Pack = document.BuildPackSnapshot(),
                Charts = document.BuildAllChartSnapshots(),
                ExternalResources = document.PendingResourceReplacements,
                AllowEmptyDestination = true,
            });
            kind = EditorSaveKind.Saved;
        }

        document.MarkSaved(result.DestinationDirectory);
        Draft = null;
        return new(kind, result.DestinationDirectory, document.Charts.Count);
    }

    /// <summary>
    /// Overlays staged replacements onto the draft's original external resources for the first
    /// save. Pending sources win on a destination conflict; original mappings whose destination is
    /// no longer referenced by the document (e.g. the old pack audio after a format change) drop
    /// out so the new package does not carry orphaned media.
    /// </summary>
    private static IReadOnlyList<V2ExternalResourceMapping> MergeFirstSaveResources(
        EditorDocument document, IReadOnlyList<V2ExternalResourceMapping> original,
        IReadOnlyList<V2ExternalResourceMapping> pending)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.Audio is { } audio) referenced.Add(audio);
        if (document.Cover is { } cover) referenced.Add(cover);
        foreach (var chart in document.Charts)
            if (chart.Audio is { } chartAudio) referenced.Add(chartAudio);
        return original
            .Where(mapping => referenced.Contains(mapping.PackageRelativePath))
            .Where(mapping => pending.All(item =>
                !string.Equals(item.PackageRelativePath, mapping.PackageRelativePath, StringComparison.OrdinalIgnoreCase)))
            .Concat(pending)
            .ToArray();
    }

    public string? ResolveDigestAudio()
    {
        if (Document is not { } document) return null;
        var relative = document.SelectedChart.Audio ?? document.Audio;
        if (relative is not null && document.GetPendingResource(relative) is { } pending)
            return Path.GetFullPath(pending);
        if (document.PackageDirectory is { } root)
            return relative is null ? null : V2PackageWriter.ResolveRegularFile(root, relative);
        return Draft is null ? null : Path.GetFullPath(Draft.ExternalAudioSource);
    }
}
