using System.IO.Compression;

namespace DynamiteUniverse.Shared.IO;

/// <summary>Transport-only archive boundary. Does not decode media or change package semantics.</summary>
public static class SafePackageArchive
{
    public const long MaxArchive = 128L * 1024 * 1024;
    public static void Extract(string archive, string destination, CancellationToken cancellation = default)
    {
        if (new FileInfo(archive).Length > MaxArchive || Directory.Exists(destination))
            throw new InvalidDataException("Fresh staging directory required");
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count is 0 or > 1024) throw new InvalidDataException("Invalid entry count");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            var name = entry.FullName; var parts = name.Split('/');
            if (name.Length > 240 || name.Contains('\\') || name.Contains(':') || name.Any(char.IsControl) ||
                parts.Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
                    new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(p.Split('.')[0], StringComparer.OrdinalIgnoreCase)) ||
                !names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) is not (0 or 0x8000) ||
                (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("Unsafe or duplicate entry");
            if (entry.Length > 64L * 1024 * 1024 || (expanded += entry.Length) > 256L * 1024 * 1024)
                throw new InvalidDataException("Expanded package exceeds budget");
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension is not (".json" or ".wav" or ".mp3" or ".ogg" or ".flac" or ".png" or ".jpg" or ".jpeg" or ".webp"))
                throw new InvalidDataException("Unsupported package file");
            if (extension == ".json" && entry.Length > 8 * 1024 * 1024) throw new InvalidDataException("JSON too large");
            var path = Path.Combine(destination, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew);
            var buffer = new byte[65536]; long count = 0; int read;
            while ((read = input.Read(buffer)) > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                if ((count += read) > entry.Length) throw new InvalidDataException("Invalid expanded size");
                output.Write(buffer, 0, read);
            }
            if (count != entry.Length) throw new InvalidDataException("Truncated archive entry");
        }
        if (!names.Contains("meta.json")) throw new InvalidDataException("Missing meta.json");
    }
}
