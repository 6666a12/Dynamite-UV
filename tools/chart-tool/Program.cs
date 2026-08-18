using System.Globalization;
using System.Security.Cryptography;

namespace ChartTool;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"fatal: {ex.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0)
            return Usage();
        return args[0] switch
        {
            "audit-legacy" => RunAudit(args),
            "convert-legacy" => RunConvert(args),
            "validate-v2" => RunValidate(args),
            "digest" => RunDigest(args),
            "self-test" => RunSelfTest(args),
            "--help" or "-h" or "help" => Usage(0),
            _ => Usage(message: $"unknown command '{args[0]}'"),
        };
    }

    private static int RunAudit(string[] args)
    {
        if (args.Length != 2)
            return Usage(message: "audit-legacy requires exactly <pack-dir>");
        var packDiagnostics = new DiagnosticBag();
        var pack = LegacyParser.ParsePack(args[1], packDiagnostics);
        if (pack is null)
        {
            Console.WriteLine("PACK: blocked");
            packDiagnostics.Print(Console.Out, "  ");
            return 1;
        }
        var converted = LegacyConverter.ConvertPack(pack, 1, packDiagnostics,
            out var chartDiagnostics, out var chartAudits);
        PrintAudit(pack, converted, packDiagnostics, chartDiagnostics, chartAudits);
        return converted is null ? 1 : 0;
    }

    private static int RunConvert(string[] args)
    {
        if (args.Length is < 4 or > 6 || args[2] != "--out")
            return Usage(message:
                "convert-legacy syntax: convert-legacy <pack-dir> --out <dir> [--revision N]");
        long revision = 1;
        if (args.Length > 4)
        {
            if (args.Length != 6 || args[4] != "--revision" ||
                !long.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out revision) ||
                revision is < 1 or > TextRules.JsonSafeInteger)
            {
                return Usage(message: "--revision must be a JSON-safe integer >= 1");
            }
        }

        var packDiagnostics = new DiagnosticBag();
        var pack = LegacyParser.ParsePack(args[1], packDiagnostics);
        if (pack is null)
        {
            Console.Error.WriteLine("conversion blocked:");
            packDiagnostics.Print(Console.Error, "  ");
            return 1;
        }
        var converted = LegacyConverter.ConvertPack(pack, revision, packDiagnostics,
            out var chartDiagnostics, out var chartAudits);
        if (converted is null)
        {
            PrintAudit(pack, null, packDiagnostics, chartDiagnostics, chartAudits);
            Console.Error.WriteLine("conversion blocked; no output was written");
            return 1;
        }
        if (!PackageWriter.Write(args[3], pack, converted, packDiagnostics))
        {
            packDiagnostics.Print(Console.Error);
            return 1;
        }
        PrintAudit(pack, converted, packDiagnostics, chartDiagnostics, chartAudits);
        Console.WriteLine($"WROTE: {Path.GetFullPath(args[3])}");
        return 0;
    }

    private static int RunValidate(string[] args)
    {
        if (args.Length != 2)
            return Usage(message: "validate-v2 requires exactly <pack-dir>");
        return V2Commands.Validate(args[1]);
    }

    private static int RunDigest(string[] args)
    {
        if (args.Length != 4 || args[2] != "--chart" || string.IsNullOrEmpty(args[3]))
            return Usage(message: "digest syntax: digest <pack-dir> --chart <chart-id>");
        return V2Commands.Digest(args[1], args[3]);
    }

    private static int RunSelfTest(string[] args)
    {
        if (args.Length != 1)
            return Usage(message: "self-test takes no arguments");
        return SelfTests.Run();
    }

    private static void PrintAudit(LegacyPack pack, ConvertedPack? converted,
        DiagnosticBag packDiagnostics,
        IReadOnlyDictionary<string, DiagnosticBag> chartDiagnostics,
        IReadOnlyDictionary<string, AuditMetrics> chartAudits)
    {
        Console.WriteLine($"PACK: {pack.Id}");
        packDiagnostics.Print(Console.Out, "  ");
        foreach (var entry in pack.Charts)
        {
            chartDiagnostics.TryGetValue(entry.Diff, out var diagnostics);
            var convertedEntry = converted?.Charts.FirstOrDefault(chart =>
                string.Equals(chart.Id, entry.Diff, StringComparison.Ordinal));
            var status = convertedEntry is not null && diagnostics is { HasErrors: false }
                ? "converted"
                : "blocked";
            Console.WriteLine($"CHART {entry.Diff}: {status}");
            if (chartAudits.TryGetValue(entry.Diff, out var audit))
            {
                Console.WriteLine($"  main judgements: baked={audit.BakedTotal} " +
                    $"derived={(audit.DerivedTotal?.ToString(CultureInfo.InvariantCulture) ?? "unavailable")}");
                Console.WriteLine($"  sync flags: baked={audit.BakedSyncCount} " +
                    $"derivable={audit.DerivableSyncCount} mismatches={audit.SyncMismatchCount}");
                Console.WriteLine($"  BPM continuity errors: {audit.ContinuityErrorCount}");
                Console.WriteLine($"  cross-BPM sustains: {audit.CrossBpmSustainCount}");
            }
            diagnostics?.Print(Console.Out, "  ");
        }
    }

    private static int Usage(int exitCode = 2, string? message = null)
    {
        if (message is not null)
            Console.Error.WriteLine($"argument error: {message}");
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  chart-tool audit-legacy <pack-dir>");
        Console.Error.WriteLine("  chart-tool convert-legacy <pack-dir> --out <dir> [--revision N]");
        Console.Error.WriteLine("  chart-tool validate-v2 <pack-dir>");
        Console.Error.WriteLine("  chart-tool digest <pack-dir> --chart <chart-id>");
        Console.Error.WriteLine("  chart-tool self-test");
        return exitCode;
    }
}

internal static class SelfTests
{
    public static int Run()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures",
            "legacy-v1-synthetic-pack");
        if (!Directory.Exists(fixture))
        {
            Console.Error.WriteLine($"SELF-TEST FAIL: fixture not found: {fixture}");
            return 1;
        }
        var temp = Path.Combine(Path.GetTempPath(), "chart-tool-self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var first = Path.Combine(temp, "first");
            var second = Path.Combine(temp, "second");
            if (!Convert(fixture, first) || !Convert(fixture, second))
                return 1;
            var firstHashes = HashTree(first);
            var secondHashes = HashTree(second);
            var converted = ReadConvertedPack(fixture);
            Check(firstHashes.SequenceEqual(secondHashes), "conversion output is byte-deterministic");
            Check(File.ReadAllBytes(Path.Combine(first, "meta.json")).SequenceEqual(
                    DynamiteUniverse.Shared.Chart.V2.V2JsonEncoder.EncodePack(
                        V2OutputProjection.ToPack(converted))),
                "converted pack bytes use the shared v2 encoder");
            Check(File.ReadAllBytes(Path.Combine(first, "chart_hard.json")).SequenceEqual(
                    DynamiteUniverse.Shared.Chart.V2.V2JsonEncoder.EncodeChart(
                        V2OutputProjection.ToChart(converted.Charts.Single().Chart))),
                "converted chart bytes use the shared v2 encoder");
            Check(V2Commands.Validate(first) == 0, "converted fixture passes shared v2 validation");
            Check(V2Commands.Digest(first, "hard") == 0, "shared Gameplay Digest computes");

            var chart = File.ReadAllText(Path.Combine(first, "chart_hard.json"));
            Check(chart.Contains("\"center\": 2.5", StringComparison.Ordinal),
                "central Mixer center is retained");
            Check(chart.Contains("\"center\": 5.25", StringComparison.Ordinal),
                "overscan center is retained without clamp");
            Check(!chart.Contains("Baked_", StringComparison.Ordinal) &&
                  !chart.Contains("SubNoteId", StringComparison.Ordinal) &&
                  !chart.Contains("TimeEnd", StringComparison.Ordinal) &&
                  !chart.Contains("get_type", StringComparison.Ordinal),
                "legacy and baked fields are omitted");

            var duplicatePack = Path.Combine(temp, "duplicate");
            CopyDirectory(fixture, duplicatePack);
            File.WriteAllText(Path.Combine(duplicatePack, "meta.json"),
                "{\"id\":\"a\",\"id\":\"b\"}");
            var duplicateDiagnostics = new DiagnosticBag();
            _ = LegacyParser.ParsePack(duplicatePack, duplicateDiagnostics);
            Check(duplicateDiagnostics.Items.Any(item =>
                    item.Pointer == "/id" && item.Reason.Contains("duplicate", StringComparison.Ordinal)),
                "duplicate-key diagnostic includes JSON Pointer");

            Console.WriteLine("SELF-TEST PASS");
            return 0;
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    private static ConvertedPack ReadConvertedPack(string fixture)
    {
        var diagnostics = new DiagnosticBag();
        var pack = LegacyParser.ParsePack(fixture, diagnostics);
        if (pack is null)
            throw new InvalidOperationException("SELF-TEST FAIL: fixture pack parsing failed.");
        var converted = LegacyConverter.ConvertPack(pack, 7, diagnostics,
            out var chartDiagnostics, out _);
        if (converted is not null)
            return converted;

        diagnostics.Print(Console.Error);
        foreach (var item in chartDiagnostics.Values)
            item.Print(Console.Error);
        throw new InvalidOperationException("SELF-TEST FAIL: fixture conversion failed.");
    }

    private static bool Convert(string fixture, string output)
    {
        var diagnostics = new DiagnosticBag();
        var pack = LegacyParser.ParsePack(fixture, diagnostics);
        if (pack is null)
        {
            diagnostics.Print(Console.Error);
            return false;
        }
        var converted = LegacyConverter.ConvertPack(pack, 7, diagnostics,
            out var chartDiagnostics, out _);
        if (converted is null)
        {
            diagnostics.Print(Console.Error);
            foreach (var item in chartDiagnostics.Values)
                item.Print(Console.Error);
            return false;
        }
        return PackageWriter.Write(output, pack, converted, diagnostics);
    }

    private static IEnumerable<string> HashTree(string root)
    {
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var hash = System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            yield return relative + ":" + hash;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal));
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("SELF-TEST FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
