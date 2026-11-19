using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Connects the add-difficulty form to the workbench sheet. A cancellation is
/// not a document change; exactly one successful commit refreshes the workbench.</summary>
internal sealed class DifficultyCreationFlow(EditorDocumentSession session, WorkbenchModalHost host, Action added)
{
    public void Show()
    {
        if (host.IsOpen || session.Document is null) return;
        host.Show((lifetime, close) =>
        {
            var form = new AddDifficultyForm(session, lifetime);
            form.DismissRequested += () =>
            {
                if (lifetime.IsCancellationRequested) return;
                // On success reload the new document before unlocking native stage input.
                try { if (form.Added) added(); }
                finally { close(); }
            };
            return form;
        });
    }

    // Smoke fixtures bypass the picker, but must use the same checks and undo transaction as the form.
    internal async Task AddForVerification(EditorCreateForm form)
    {
        var reference = session.Document!.SelectedChart;
        var prepared = await session.PrepareDifficultyAsync(new AddDifficultyRequest(form, PreviewOverride: reference.Preview));
        session.AddDifficulty(prepared);
        added();
    }
}
