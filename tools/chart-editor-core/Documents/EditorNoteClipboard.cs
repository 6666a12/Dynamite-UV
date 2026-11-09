using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Session clipboard of complete parent notes. No UI, OS clipboard or document ownership.</summary>
public sealed class EditorNoteClipboard
{
    private EditableNote[] _notes = [];
    public int Count => _notes.Length;

    public int Copy(EditorDocument document, IEnumerable<string> parentIds)
    {
        var ids = parentIds.ToHashSet(StringComparer.Ordinal);
        var notes = document.SelectedChart.Notes.Where(n => ids.Contains(n.Id)).OrderBy(n => n.Time).ToArray();
        if (notes.Length == 0) return 0; // Empty selection must not erase a useful clipboard.
        _notes = notes.Select(n => n.Clone()).ToArray();
        return Count;
    }

    public string[] Paste(EditorDocument document, ExactBarTime anchor)
    {
        if (_notes.Length == 0) return [];
        if (anchor < ExactBarTime.Zero) throw new ArgumentOutOfRangeException(nameof(anchor));
        var delta = anchor - _notes[0].Time;
        var copies = new List<EditableNote>();
        foreach (var source in _notes)
        {
            var note = source.Clone();
            note.Id = document.AllocateId("paste");
            note.Key = EditorEntityKey.New();
            note.Time += delta;
            foreach (var node in note.Nodes)
            {
                node.Id = document.AllocateId("paste-node");
                node.Key = EditorEntityKey.New();
                node.Time += delta;
            }
            EditorCommandRules.ValidateNote(note);
            copies.Add(note);
        }
        document.Execute(new CompositeEditorCommand(copies.Select(n => (IEditorCommand)new AddNoteCommand(n)).ToArray()));
        return copies.Select(n => n.Id).ToArray();
    }
}
