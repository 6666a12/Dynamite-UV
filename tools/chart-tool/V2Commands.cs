using DuxShared.Chart.V2;
using System.Buffers.Binary;

namespace ChartTool;

internal static class V2Commands
{
    public static int Validate(string directory)
    {
        try
        {
            var package = V2PackageContext.Open(directory);
            var resolved = package.Validate();
            Console.WriteLine($"VALID: {package.Pack.Id} revision={package.Pack.Revision} charts={resolved.Count}");
            foreach (var item in resolved)
            {
                var count = V2SemanticValidator.CountMainJudgements(item.Chart);
                Console.WriteLine($"  {item.Entry.Id}: judgements={count} audio={item.ResolvedAudio}");
            }
            foreach (var warning in package.Warnings)
                Console.WriteLine($"warning: {warning}");
            return 0;
        }
        catch (V2DiagnosticException ex)
        {
            Console.Error.WriteLine($"error: {ex.SourceName} {ex.JsonPointer}: {ex.Reason}");
            return 1;
        }
        catch (PackageDiagnosticException ex)
        {
            Console.Error.WriteLine($"error: {ex.File} {ex.Pointer}: {ex.Reason}");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"error: {directory} /: {ex.Message}");
            return 1;
        }
    }

    public static int Digest(string directory, string chartId)
    {
        try
        {
            var package = V2PackageContext.Open(directory);
            var resolved = package.Validate();
            var selected = resolved.SingleOrDefault(item =>
                string.Equals(item.Entry.Id, chartId, StringComparison.Ordinal));
            if (selected is null)
            {
                Console.Error.WriteLine($"error: meta.json /charts: chart ID '{chartId}' was not found");
                return 1;
            }
            var audio = package.ReadPackageFile(selected.ResolvedAudio);
            var digest = V2GameplayDigest.Compute(selected.Chart, selected.Entry, audio);
            Console.WriteLine($"algorithm: {digest.Algorithm}");
            Console.WriteLine($"packId: {package.Pack.Id}");
            Console.WriteLine($"chartId: {selected.Entry.Id}");
            Console.WriteLine($"rulesetId: {digest.RulesetId}");
            Console.WriteLine($"audioSha256: {digest.AudioSha256}");
            Console.WriteLine($"gameplayDigest: {digest.Sha256}");
            foreach (var warning in package.Warnings)
                Console.WriteLine($"warning: {warning}");
            return 0;
        }
        catch (V2DiagnosticException ex)
        {
            Console.Error.WriteLine($"error: {ex.SourceName} {ex.JsonPointer}: {ex.Reason}");
            return 1;
        }
        catch (PackageDiagnosticException ex)
        {
            Console.Error.WriteLine($"error: {ex.File} {ex.Pointer}: {ex.Reason}");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"error: {directory} /: {ex.Message}");
            return 1;
        }
    }
}

internal sealed class V2PackageContext
{
    private readonly string _root;
    private readonly Dictionary<string, double?> _durationCache =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);

    private V2PackageContext(string root, V2Pack pack)
    {
        _root = root;
        Pack = pack;
    }

    public V2Pack Pack { get; }
    public IEnumerable<string> Warnings => _warnings.Order(StringComparer.Ordinal);

    public static V2PackageContext Open(string directory)
    {
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
            throw new PackageDiagnosticException(directory, "/", "pack directory does not exist");
        EnsureNotReparse(new DirectoryInfo(root), directory, "/");
        RejectCaseCollisionsAndLinks(root);
        var meta = Resolve(root, "meta.json", requireFile: true);
        var pack = V2JsonDecoder.DecodePackFile(meta);
        return new V2PackageContext(root, pack);
    }

    public IReadOnlyList<V2ResolvedChart> Validate() => V2PackageValidator.Validate(Pack,
        new V2PackageValidationHooks
        {
            LoadChart = entry => V2JsonDecoder.DecodeChartFile(
                Resolve(_root, entry.File, requireFile: true)),
            FileExists = packagePath => IsRegularPackageFile(packagePath),
            AudioDurationSeconds = ProbeAudioDuration,
            ValidateResolvedAudio = (entry, audio) =>
            {
                if (Path.GetExtension(audio).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                    ProbeAudioDuration(audio);
            },
        });

    public byte[] ReadPackageFile(string path) =>
        File.ReadAllBytes(Resolve(_root, path, requireFile: true));

    private bool IsRegularPackageFile(string path)
    {
        try
        {
            _ = Resolve(_root, path, requireFile: true);
            return true;
        }
        catch (PackageDiagnosticException)
        {
            return false;
        }
    }

    private double? ProbeAudioDuration(string path)
    {
        if (_durationCache.TryGetValue(path, out var cached))
            return cached;
        var full = Resolve(_root, path, requireFile: true);
        if (!Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            _warnings.Add($"{path}: audio duration was not probed (only RIFF/WAVE probing is built in)");
            _durationCache[path] = null;
            return null;
        }
        var duration = WaveDuration.Read(full, path);
        _durationCache[path] = duration;
        return duration;
    }

    private static void RejectCaseCollisionsAndLinks(string root)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, entry.FullName)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (!paths.Add(relative))
                throw new PackageDiagnosticException(relative, "/",
                    "package paths collide under case-insensitive comparison");
            EnsureNotReparse(entry, relative, "/");
        }
    }

    private static string Resolve(string root, string packagePath, bool requireFile)
    {
        if (!TextRules.TryResolveFile(root, packagePath, out var full))
            throw new PackageDiagnosticException(packagePath, "/", "unsafe package-relative path");
        var relative = Path.GetRelativePath(root, full);
        var current = new DirectoryInfo(root);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            var next = Path.Combine(current.FullName, segment);
            if (Directory.Exists(next))
            {
                var directory = new DirectoryInfo(next);
                EnsureNotReparse(directory, packagePath, "/");
                current = directory;
            }
            else
                break;
        }
        if (requireFile && !File.Exists(full))
            throw new PackageDiagnosticException(packagePath, "/", "referenced regular file does not exist");
        if (File.Exists(full))
            EnsureNotReparse(new FileInfo(full), packagePath, "/");
        return full;
    }

    private static void EnsureNotReparse(FileSystemInfo info, string file, string pointer)
    {
        if (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new PackageDiagnosticException(file, pointer,
                "symlinks/reparse points are not accepted in chart packages");
    }
}

internal sealed class PackageDiagnosticException(string file, string pointer, string reason)
    : Exception(reason)
{
    public string File { get; } = file;
    public string Pointer { get; } = pointer;
    public string Reason { get; } = reason;
}

internal static class WaveDuration
{
    public static double Read(string fullPath, string displayPath)
    {
        using var stream = File.OpenRead(fullPath);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 12 || reader.ReadUInt32() != 0x46464952 ||
            reader.ReadUInt32() + 8L > stream.Length || reader.ReadUInt32() != 0x45564157)
            throw new PackageDiagnosticException(displayPath, "/", "invalid RIFF/WAVE audio file");
        uint? byteRate = null;
        long dataBytes = 0;
        var foundData = false;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = reader.ReadUInt32();
            var size = reader.ReadUInt32();
            var chunkStart = stream.Position;
            var chunkEnd = chunkStart + size;
            if (chunkEnd > stream.Length)
                throw new PackageDiagnosticException(displayPath, "/", "truncated RIFF/WAVE chunk");
            if (id == 0x20746d66)
            {
                if (size < 16)
                    throw new PackageDiagnosticException(displayPath, "/", "invalid WAVE fmt chunk");
                var format = reader.ReadUInt16();
                var channels = reader.ReadUInt16();
                var sampleRate = reader.ReadUInt32();
                var rate = reader.ReadUInt32();
                var blockAlign = reader.ReadUInt16();
                var bits = reader.ReadUInt16();
                if (format is not (1 or 3) || channels == 0 || sampleRate == 0 || rate == 0 ||
                    blockAlign == 0 || bits == 0)
                    throw new PackageDiagnosticException(displayPath, "/",
                        "unsupported or invalid WAVE format (PCM/IEEE-float required)");
                byteRate = rate;
            }
            else if (id == 0x61746164)
            {
                foundData = true;
                dataBytes = checked(dataBytes + size);
            }
            stream.Position = chunkEnd + (size & 1);
        }
        if (byteRate is null || !foundData)
            throw new PackageDiagnosticException(displayPath, "/", "WAVE fmt or data chunk is missing");
        var duration = dataBytes / (double)byteRate.Value;
        if (!double.IsFinite(duration) || duration < 0)
            throw new PackageDiagnosticException(displayPath, "/", "invalid WAVE duration");
        return duration;
    }
}
