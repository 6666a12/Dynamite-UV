using Avalonia.Controls;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor.Shell;

internal static class CoverStudioFlow
{
    public static async void Show(Window owner, EditorDocumentSession session, IWorkbenchLocalizer text, Action<string> report)
    {
        if (session.Document is not { } document) { report(text["Cover.NoDocument"]); return; }
        try
        {
            var window = new CoverStudioWindow(CoverLayout.FromDocument(document), document.PackageDirectory);
            // An owned modal window keeps this snapshot tied to the same package; no Dock changes or stage IPC.
            var closed = window.ShowDialog(owner);
            try
            {
                if (CoverFiles.Resolve(document, session.Draft) is { } path) await window.LoadSourceAsync(path);
            }
            catch (Exception ex) { if (window.IsVisible) window.ReportSourceError(ex); }
            await closed;
        }
        catch (Exception ex) { report(text.Format("Cover.OpenFailed", ex.Message)); }
    }
}
