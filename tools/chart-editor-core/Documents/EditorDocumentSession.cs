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

    public Task<EditorDocument> OpenAsync(string directory) => ReplaceAsync(() =>
        (EditorPackageRepository.Open(directory), (EditorProjectDraft?)null));

    public Task<EditorDocument> CreateDraftAsync(EditorProjectDraftRequest request) => ReplaceAsync(() =>
    {
        var draft = EditorProjectDraftFactory.Create(request);
        return (draft.Document, (EditorProjectDraft?)draft);
    });

    private async Task<EditorDocument> ReplaceAsync(Func<(EditorDocument Document, EditorProjectDraft? Draft)> prepare)
    {
        var previous = Document;
        var version = previous?.ChangeVersion;
        var result = await Task.Run(prepare);
        // Adopt on the owner's context only if no edits or replacement occurred during preparation.
        if (!ReferenceEquals(Document, previous) || previous?.ChangeVersion != version)
            throw new InvalidOperationException("文档在加载期间已更改，已保留当前文档，请重试。");
        Document = result.Document;
        Draft = result.Draft;
        return Document;
    }

    public void AddChart(EditableChart chart)
    {
        var document = Document ?? throw new InvalidOperationException("没有打开的包。");
        // Adding a difficulty preserves the draft's first-save external audio/cover ownership.
        document.Execute(new AddChartCommand(chart));
    }

    public EditorSaveResult Save(string destinationDirectory)
    {
        var save = PrepareSave(destinationDirectory);
        return save.Complete(save.Write());
    }

    public async Task<EditorSaveResult> SaveAsync(string destinationDirectory)
    {
        var save = PrepareSave(destinationDirectory); // Snapshot on the document owner's thread.
        var result = await Task.Run(save.Write);
        return save.Complete(result); // Adopt on the captured UI synchronization context.
    }

    private (Func<V2PackageWriteResult> Write, Func<V2PackageWriteResult, EditorSaveResult> Complete)
        PrepareSave(string destinationDirectory)
    {
        var document = Document ?? throw new InvalidOperationException("没有可保存的文档。");
        var version = document.ChangeVersion;
        var resources = document.PendingResourceReplacements;
        Func<V2PackageWriteResult> write;
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
            write = () => V2PackageWriter.Create(request);
            kind = EditorSaveKind.Created;
        }
        else
        {
            var request = new V2PackageWriteRequest
            {
                SourceDirectory = document.PackageDirectory!,
                DestinationDirectory = destinationDirectory,
                Pack = document.BuildPackSnapshot(),
                Charts = document.BuildAllChartSnapshots(),
                ExternalResources = resources,
                AllowEmptyDestination = true,
            };
            write = () => V2PackageWriter.Write(request);
            kind = EditorSaveKind.Saved;
        }

        return (write, result =>
        {
            document.CompleteSave(result.DestinationDirectory, version, resources);
            if (ReferenceEquals(Document, document)) Draft = null;
            return new(kind, result.DestinationDirectory, document.Charts.Count);
        });
    }

    /// <summary>Pending resources override original draft media; unreferenced mappings are omitted.</summary>
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
