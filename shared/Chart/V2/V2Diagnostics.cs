namespace DuxShared.Chart.V2;

/// <summary>Severity used by editor-facing v2 validation and package-write diagnostics.</summary>
public enum V2DiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>A stable editor-facing diagnostic. Paths are package-relative where possible.</summary>
public sealed record V2Diagnostic(
    V2DiagnosticSeverity Severity,
    string SourceName,
    string JsonPointer,
    string Reason)
{
    public static V2Diagnostic FromException(V2DiagnosticException exception) => new(
        V2DiagnosticSeverity.Error,
        exception.SourceName,
        exception.JsonPointer,
        exception.Reason);
}
