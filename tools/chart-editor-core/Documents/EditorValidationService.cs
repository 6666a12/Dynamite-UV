using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Unified validation used by the editor property workspace and export command.</summary>
public static class EditorValidationService
{
    public static IReadOnlyList<V2Diagnostic> Validate(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = new List<V2Diagnostic>();
        V2Pack pack;
        IReadOnlyDictionary<string, V2Chart> charts;
        try
        {
            pack = document.BuildPackSnapshot();
            charts = document.BuildAllChartSnapshots();
            V2SemanticValidator.ValidatePack(pack);
        }
        catch (V2DiagnosticException exception)
        {
            return [V2Diagnostic.FromException(exception)];
        }
        catch (Exception exception)
        {
            return [new V2Diagnostic(V2DiagnosticSeverity.Error, "editor", "/", exception.Message)];
        }

        foreach (var entry in pack.Charts)
        {
            try
            {
                V2SemanticValidator.ValidateChart(charts[entry.Id], entry.File);
                AddOverscanWarnings(charts[entry.Id], entry.File, diagnostics);
            }
            catch (V2DiagnosticException exception)
            {
                diagnostics.Add(V2Diagnostic.FromException(exception));
            }
            catch (Exception exception)
            {
                diagnostics.Add(new V2Diagnostic(V2DiagnosticSeverity.Error, entry.File, "/", exception.Message));
            }
        }
        return diagnostics;
    }

    private static void AddOverscanWarnings(V2Chart chart, string source, ICollection<V2Diagnostic> diagnostics)
    {
        foreach (var (track, note) in chart.AllNotes)
        {
            AddWarning(note.Center, note.Width, source, $"/{TrackName(track)}/{note.Id}", diagnostics);
            if (note is not V2PathNote path)
                continue;
            foreach (var node in path.Nodes)
                AddWarning(node.Center, node.Width, source,
                    $"/{TrackName(track)}/{note.Id}/nodes/{node.Id}", diagnostics);
        }
    }

    private static void AddWarning(double center, double width, string source, string pointer,
        ICollection<V2Diagnostic> diagnostics)
    {
        if (center - width / 2.0 < 0.0 || center + width / 2.0 > 5.0)
            diagnostics.Add(new V2Diagnostic(V2DiagnosticSeverity.Warning, source, pointer,
                "object exceeds the recommended [0,5] authoring range; v2 overscan is preserved"));
    }

    private static string TrackName(DynamiteUniverse.Shared.Chart.Track track) => track switch
    {
        DynamiteUniverse.Shared.Chart.Track.Left => "notesLeft",
        DynamiteUniverse.Shared.Chart.Track.Center => "notesCenter",
        _ => "notesRight",
    };
}
