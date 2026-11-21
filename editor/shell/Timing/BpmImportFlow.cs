using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Owns the preview target and applies its result only to the captured difficulty.</summary>
internal sealed class BpmImportFlow(EditorDocumentSession session, WorkbenchModalHost host,
    Action refresh, Action<string> report, IWorkbenchLocalizer text)
{
    public void Show()
    {
        if (host.IsOpen || session.Document is not { } document) return;
        var chart = document.SelectedChart;
        var version = document.ChangeVersion;
        host.Show((lifetime, close) => new BpmImportForm(chart.Id, chart.AudioOffsetSec, lifetime, close, (points, offset) =>
        {
            if (session.Document != document || document.SelectedChart.Key != chart.Key || document.ChangeVersion != version)
                throw new InvalidOperationException(text["BpmImport.TargetChanged"]);
            var unchanged = offset == chart.AudioOffsetSec &&
                points.SequenceEqual(chart.Bpms.Select(p => new V2BpmEvent(p.Time, p.Bpm)));
            if (!unchanged)
            {
                document.Execute(new ReplaceBpmTimelineCommand(points, offset));
                refresh();
            }
            report(text.Format(unchanged ? "BpmImport.Unchanged" : "BpmImport.Done", chart.Id, points.Count));
        }));
    }
}
