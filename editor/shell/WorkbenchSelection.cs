using System.Text.Json;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor.Shell;

internal enum WorkbenchSelectionContext { None, Note, PathNode, Bpm, Scroll }

/// <summary>Shell-side selection mirror. The document owns undo identity; this object owns the
/// visible group, primary edit target, and whether a note command may consume that group.</summary>
internal sealed class WorkbenchSelection
{
    public string[] Parents { get; private set; } = [];
    public string? Primary { get; private set; }
    public WorkbenchSelectionContext Context { get; private set; }
    public bool HasNoteTarget => Context is WorkbenchSelectionContext.Note or WorkbenchSelectionContext.PathNode && Primary is not null;
    public bool CanDeleteNote => HasNoteTarget;
    public bool CanCopyNotes => HasNoteTarget && Parents.Length > 0;

    public string[] StageIds(EditorDocument? document) => HasNoteTarget && document?.Selection is
        { Kind: EditorSelectionKind.PathNode, Id: { } node } ? [node] : HasNoteTarget ? Parents : [];

    public void FromStage(EditorDocument? document, JsonElement data)
    {
        if (document is null) { Clear(); return; }
        var sources = data.GetProperty("ids").EnumerateArray().Select(id => id.GetString()).ToHashSet();
        var notes = document.SelectedChart.Notes.Where(n => sources.Contains(n.Id) || n.Nodes.Any(p => sources.Contains(p.Id))).ToArray();
        Parents = notes.Select(n => n.Id).Distinct().ToArray();
        if (notes.Length == 0 || Parents.Length == 0) { Clear(); document.Select(EditorSelection.None); return; }
        // Only a single real authored handle can be edited as a path point. Groups edit parents.
        var node = data.TryGetProperty("nodeId", out var element) ? element.GetString() : null;
        if (Parents.Length == 1 && sources.Count == 1 && node is not null && sources.Contains(node) &&
            notes.Length == 1 && notes[0].Nodes.Any(p => p.Id == node))
        {
            Primary = Parents[0];
            Context = WorkbenchSelectionContext.PathNode;
            document.Select(new EditorSelection(EditorSelectionKind.PathNode, node));
            return;
        }
        var preferred = data.TryGetProperty("parentId", out var hint) ? hint.GetString() : null;
        Primary = Parents.Contains(preferred) ? preferred : Parents[0];
        Context = WorkbenchSelectionContext.Note;
        document.Select(new EditorSelection(EditorSelectionKind.Note, Primary));
    }

    public void FromParents(EditorDocument document, IEnumerable<string> parents)
    {
        var exists = document.SelectedChart.Notes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        Parents = parents.Where(exists.Contains).Distinct(StringComparer.Ordinal).ToArray();
        Primary = Parents.LastOrDefault();
        Context = Primary is null ? WorkbenchSelectionContext.None : WorkbenchSelectionContext.Note;
        document.Select(Primary is null ? EditorSelection.None : new(EditorSelectionKind.Note, Primary));
    }

    public void SelectNote(EditorDocument document, string id)
    {
        if (!document.SelectedChart.Notes.Any(n => n.Id == id)) { Clear(); document.Select(EditorSelection.None); return; }
        Parents = [id]; Primary = id; Context = WorkbenchSelectionContext.Note;
        document.Select(new EditorSelection(EditorSelectionKind.Note, id));
    }

    public bool SelectPathNode(EditorDocument document, string parentId, string nodeId)
    {
        if (!document.SelectedChart.Notes.Any(n => n.Id == parentId && n.Nodes.Any(node => node.Id == nodeId))) return false;
        Parents = [parentId]; Primary = parentId; Context = WorkbenchSelectionContext.PathNode;
        document.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
        return true;
    }

    public void SelectTiming(EditorDocument document, EditorSelection? selection)
    {
        Parents = []; Primary = null;
        Context = selection?.Kind switch
        {
            EditorSelectionKind.Bpm => WorkbenchSelectionContext.Bpm,
            EditorSelectionKind.Scroll => WorkbenchSelectionContext.Scroll,
            _ => WorkbenchSelectionContext.None,
        };
        document.Select(selection ?? EditorSelection.None);
    }

    public void FromDocument(EditorDocument? document, string[]? previous = null)
    {
        var selected = document?.Selection;
        switch (selected?.Kind)
        {
            case EditorSelectionKind.Note when selected.Id is { } noteId && document!.SelectedChart.Notes.Any(n => n.Id == noteId):
                Primary = noteId;
                Parents = (previous ?? Parents).Contains(noteId)
                    ? (previous ?? Parents).Where(id => document.SelectedChart.Notes.Any(n => n.Id == id)).ToArray()
                    : [noteId];
                Context = WorkbenchSelectionContext.Note;
                break;
            case EditorSelectionKind.PathNode when document!.SelectedChart.Notes.Any(n => n.Nodes.Any(p => p.Id == selected.Id)):
                Primary = document.SelectedChart.Notes.First(n => n.Nodes.Any(p => p.Id == selected.Id)).Id;
                Parents = [Primary];
                Context = WorkbenchSelectionContext.PathNode;
                break;
            case EditorSelectionKind.Bpm:
                Clear(WorkbenchSelectionContext.Bpm);
                break;
            case EditorSelectionKind.Scroll:
                Clear(WorkbenchSelectionContext.Scroll);
                break;
            default:
                Clear();
                break;
        }
    }

    public void Clear(WorkbenchSelectionContext context = WorkbenchSelectionContext.None)
    {
        Parents = []; Primary = null; Context = context;
    }

    public void Show(EditorDocument? document, InspectorView inspector)
    {
        if (Primary is null) inspector.ShowDocument(document);
        else inspector.Select(document, Parents, Primary);
    }
}
