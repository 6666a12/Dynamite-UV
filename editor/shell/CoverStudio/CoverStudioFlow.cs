using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor.Shell;

internal static class CoverStudioFlow
{
    public static async Task Show(WorkbenchModalHost modal, EditorDocumentSession session, IWorkbenchLocalizer text, Action<string> report)
    {
        if (session.Document is not { } document) { report(text["Cover.NoDocument"]); return; }
        try
        {
            var window = new CoverStudioWindow(CoverLayout.FromDocument(document), document.PackageDirectory);
            // Source decode can outlive the window. The modal host releases on dialog close,
            // not when the optional bitmap finishes loading.
            window.Opened += async (_, _) =>
            {
                try
                {
                    if (CoverFiles.Resolve(document, session.Draft) is { } path) await window.LoadSourceAsync(path);
                }
                catch (Exception ex) { if (window.IsVisible) window.ReportSourceError(ex); }
            };
            await modal.ShowDialog(window);
        }
        catch (Exception ex) { report(text.Format("Cover.OpenFailed", ex.Message)); }
    }
}
