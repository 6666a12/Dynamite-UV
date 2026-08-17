using System.Text;

namespace ChartTool;

internal enum DiagnosticSeverity
{
    Warning,
    Error,
}

internal sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string File,
    string Pointer,
    string Reason)
{
    public override string ToString()
    {
        var location = string.IsNullOrEmpty(Pointer) ? "/" : Pointer;
        return $"{Severity.ToString().ToLowerInvariant()}: {File} {location}: {Reason}";
    }
}

internal sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = [];

    public IReadOnlyList<Diagnostic> Items => _items;
    public bool HasErrors => _items.Any(item => item.Severity == DiagnosticSeverity.Error);

    public void Error(string file, string pointer, string reason) =>
        _items.Add(new Diagnostic(DiagnosticSeverity.Error, file, pointer, reason));

    public void Warning(string file, string pointer, string reason) =>
        _items.Add(new Diagnostic(DiagnosticSeverity.Warning, file, pointer, reason));

    public void Print(TextWriter writer, string indent = "")
    {
        foreach (var item in _items)
            writer.WriteLine(indent + item);
    }
}

internal static class JsonPointer
{
    public static string Property(string pointer, string property) =>
        $"{pointer}/{Escape(property)}";

    public static string Index(string pointer, int index) => $"{pointer}/{index}";

    public static string Escape(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);
}

internal static class TextRules
{
    public const long JsonSafeInteger = 9_007_199_254_740_991;

    public static bool IsId(string value)
    {
        if (value.Length is < 1 or > 64 || !IsAsciiAlphaNumeric(value[0]))
            return false;
        for (var i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!IsAsciiAlphaNumeric(ch) && ch is not ('.' or '_' or '-'))
                return false;
        }
        return true;
    }

    public static bool IsDisplayText(string value, int maxCodePoints = 256)
    {
        if (value.Length == 0 || RuneCount(value) > maxCodePoints ||
            !value.IsNormalized(NormalizationForm.FormC))
            return false;
        var first = value.EnumerateRunes().First();
        var last = value.EnumerateRunes().Last();
        if (Rune.IsWhiteSpace(first) || Rune.IsWhiteSpace(last))
            return false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value is <= 0x1f or 0x7f)
                return false;
        }
        return true;
    }

    public static bool IsSafePackagePath(string value)
    {
        if (value.Length == 0 || RuneCount(value) > 1024 || value.Contains('\\') ||
            value.Contains('\0') || value.StartsWith('/') || value.EndsWith('/') ||
            Uri.TryCreate(value, UriKind.Absolute, out _) ||
            (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':'))
            return false;
        return value.Split('/').All(segment =>
            segment.Length > 0 && segment is not "." and not ".." && !segment.Contains(':'));
    }

    public static bool TryResolveFile(string root, string packagePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (!IsSafePackagePath(packagePath))
            return false;
        var rootFull = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(rootFull,
            packagePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(rootFull, candidate);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return false;
        fullPath = candidate;
        return true;
    }

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private static int RuneCount(string value) => value.EnumerateRunes().Count();
}
