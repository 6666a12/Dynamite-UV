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
