using DuxCommunity.ChartEditor.Core;
using DuxShared.Chart.V2;

namespace DuxCommunity.ChartEditor.Editor;

/// <summary>Undoable project metadata transaction owned by the Avalonia adapter.</summary>
public sealed class EditProjectCommand(string title, string artist, string? audio) : IEditorCommand
{
    private string? _oldTitle;
    private string? _oldArtist;
    private string? _oldAudio;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        if (!_captured)
        {
            _oldTitle = document.Title;
            _oldArtist = document.Artist;
            _oldAudio = document.Audio;
            _captured = true;
        }
        document.Title = title;
        document.Artist = artist;
        document.Audio = audio;
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Project metadata command has not executed.");
        document.Title = _oldTitle!;
        document.Artist = _oldArtist!;
        document.Audio = _oldAudio;
    }
}
