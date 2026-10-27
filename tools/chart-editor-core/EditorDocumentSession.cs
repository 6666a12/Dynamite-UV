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
            result = V2PackageWriter.Create(
                draft.BuildPackageCreateRequest(destinationDirectory, allowEmptyDestination: true));
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

    public string? ResolveDigestAudio()
    {
        if (Document is not { } document) return null;
        if (document.PackageDirectory is { } root)
        {
            var relative = document.SelectedChart.Audio ?? document.Audio;
            return relative is null ? null : V2PackageWriter.ResolveRegularFile(root, relative);
        }
        return Draft is null ? null : Path.GetFullPath(Draft.ExternalAudioSource);
    }
}
