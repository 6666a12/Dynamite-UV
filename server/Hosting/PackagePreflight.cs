using DynamiteUniverse.Server.Catalog;

namespace DynamiteUniverse.Server.Hosting;

/// <summary>Offline operator preflight; validates a private copy, never modifies the supplied archive.</summary>
internal static class PackagePreflight
{
    internal static int Run(string path)
    {
        var stage = Path.Combine(Path.GetTempPath(), "du-preflight-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (new FileInfo(path).Length > PackageInspector.MaxArchive) throw new InvalidDataException("Archive exceeds 128 MiB");
            Directory.CreateDirectory(stage);
            var copy = Path.Combine(stage, "input.zip"); File.Copy(path, copy);
            var result = PackageInspector.Inspect(copy, stage);
            Console.WriteLine($"VALID: {result.Pack.Id} / {result.Pack.Title} / {result.Charts.Count} charts / {result.Display.ValidationProfile}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or DynamiteUniverse.Shared.Chart.V2.V2DiagnosticException)
        {
            Console.Error.WriteLine("INVALID: " + new string(ex.Message.Select(c => char.IsControl(c) ? ' ' : c).ToArray()));
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            catch (IOException) { }
        }
    }
}
