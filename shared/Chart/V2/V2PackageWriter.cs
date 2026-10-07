using System.Buffers.Binary;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>Input for transactional writes of one fully resolved local v2 package.</summary>
public sealed record V2PackageWriteRequest
{
    /// <summary>Existing package directory whose non-JSON resources are copied to staging.</summary>
    public required string SourceDirectory { get; init; }
    /// <summary>Existing directory for Save, or a non-existent/empty directory for Save As.</summary>
    public required string DestinationDirectory { get; init; }
    public required V2Pack Pack { get; init; }
    /// <summary>Charts keyed by their v2 chartId. Every declared pack entry is required.</summary>
    public required IReadOnlyDictionary<string, V2Chart> Charts { get; init; }
    /// <summary>Optional external resources to replace at package-relative paths during staging.</summary>
    public IReadOnlyList<V2ExternalResourceMapping> ExternalResources { get; init; } = [];
    /// <summary>
    /// Optional host decoder used to validate and measure staged audio. Null preserves the built-in
    /// PCM/IEEE-float RIFF/WAVE-only behavior.
    /// </summary>
    public IV2PackageAudioProbe? AudioProbe { get; init; }
    /// <summary>Allows Save As to target an existing empty directory only.</summary>
    public bool AllowEmptyDestination { get; init; }
}

/// <summary>One external regular file imported into a new package.</summary>
public sealed record V2ExternalResourceMapping(
    string ExternalSourcePath,
    string PackageRelativePath);

/// <summary>Input for transactional first creation of a package without an existing source package.</summary>
public sealed record V2PackageCreateRequest
{
    /// <summary>Non-existent directory, or an explicitly allowed existing empty directory.</summary>
    public required string DestinationDirectory { get; init; }
    public required V2Pack Pack { get; init; }
    /// <summary>Charts keyed by their v2 chartId. Every declared pack entry is required.</summary>
    public required IReadOnlyDictionary<string, V2Chart> Charts { get; init; }
    /// <summary>External regular files copied to safe package-relative resource paths.</summary>
    public required IReadOnlyList<V2ExternalResourceMapping> ExternalResources { get; init; }
    /// <summary>
    /// Optional host decoder used to validate and measure staged audio. Null preserves the built-in
    /// PCM/IEEE-float RIFF/WAVE-only behavior.
    /// </summary>
    public IV2PackageAudioProbe? AudioProbe { get; init; }
    public bool AllowEmptyDestination { get; init; }
}

/// <summary>Validated metadata returned by a package audio decoder.</summary>
public sealed record V2PackageAudioInfo(double DurationSeconds);

/// <summary>
/// Host-supplied staged-audio validator. Implementations must decode enough of the regular file to
/// reject malformed or unsupported content and return a finite non-negative duration.
/// </summary>
public interface IV2PackageAudioProbe
{
    V2PackageAudioInfo Probe(string absolutePath, string packageRelativePath);
}

/// <summary>Verified result of a successful v2 package write.</summary>
public sealed record V2PackageWriteResult(
    string DestinationDirectory,
    IReadOnlyDictionary<string, V2GameplayDigestResult> Digests);

/// <summary>
/// Filesystem writer for local v2 packages. It copies raw package resources to an adjacent staging
/// directory, verifies the staged files through the strict decoder/validator and only then publishes
/// every staged file. Existing targets are backed up and restored if a later replacement fails.
/// </summary>
public static class V2PackageWriter
{
    /// <summary>Test-only seam invoked with each package-relative path before it is published.</summary>
    internal static Action<string>? PublicationInterceptor { get; set; }

    public static V2PackageWriteResult Write(V2PackageWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = RequireDirectory(request.SourceDirectory, "source package directory");
        var destination = RequireDirectoryPath(request.DestinationDirectory, "destination package directory");
        ValidateDestination(source, destination, request.AllowEmptyDestination);
        ValidateSourceTree(source);
        ValidateRequest(request.Pack, request.Charts);
        var sourcePack = V2JsonDecoder.DecodePackFile(Resolve(source, "meta.json", requireFile: true));

        var staging = destination + ".dynamite-universe-staging-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyDirectory(source, staging);
            CopyReplacementResources(staging, request.ExternalResources);
            WriteJsonFiles(staging, request.Pack, request.Charts);
            RemoveObsoleteChartFiles(staging, sourcePack, request.Pack);
            RemoveObsoleteResources(staging, sourcePack, request.Pack);
            var verified = VerifyStaging(staging, request.Pack, request.AudioProbe);

            if (SameDirectory(source, destination))
                PublishOverExisting(staging, destination);
            else
                PublishSaveAs(staging, destination);

            return new V2PackageWriteResult(destination, verified);
        }
        catch
        {
            TryDeleteStaging(staging);
            throw;
        }
    }

    public static V2PackageWriteResult Create(V2PackageCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var destination = RequireDirectoryPath(request.DestinationDirectory,
            "destination package directory");
        ValidateNewDestination(destination, request.AllowEmptyDestination);
        ValidateRequest(request.Pack, request.Charts);
        var resources = ValidateExternalResources(request.Pack, request.ExternalResources);

        var staging = destination + ".dynamite-universe-staging-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            CopyExternalResources(staging, resources);
            WriteJsonFiles(staging, request.Pack, request.Charts);
            var verified = VerifyStaging(staging, request.Pack, request.AudioProbe);
            PublishNewPackage(staging, destination);
            return new V2PackageWriteResult(destination, verified);
        }
        catch
        {
            TryDeleteStaging(staging);
            throw;
        }
    }

    private static void ValidateRequest(V2Pack pack, IReadOnlyDictionary<string, V2Chart> charts)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(charts);
        V2SemanticValidator.ValidatePack(pack);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in pack.Charts)
        {
            if (!charts.TryGetValue(entry.Id, out var chart))
                throw new V2DiagnosticException("meta.json", "/charts",
                    $"write request is missing chart '{entry.Id}'");
            if (!ids.Add(entry.Id) || !string.Equals(chart.ChartId, entry.Id, StringComparison.Ordinal))
                throw new V2DiagnosticException(entry.File, "/chartId",
                    $"write request chartId must equal declared entry ID '{entry.Id}'");
            V2SemanticValidator.ValidateChart(chart, entry.File);
        }
        if (charts.Keys.Any(id => !ids.Contains(id)))
            throw new V2DiagnosticException("meta.json", "/charts",
                "write request contains a chart not declared by the package");
    }

    private static IReadOnlyDictionary<string, V2GameplayDigestResult> VerifyStaging(
        string staging, V2Pack expectedPack, IV2PackageAudioProbe? audioProbe)
    {
        ValidateSourceTree(staging);
        var meta = Resolve(staging, "meta.json", requireFile: true);
        var pack = V2JsonDecoder.DecodePackFile(meta);
        var audioResults = new Dictionary<string, V2PackageAudioInfo>(StringComparer.Ordinal);
        V2PackageAudioInfo Probe(string relative)
        {
            if (audioResults.TryGetValue(relative, out var cached))
                return cached;
            var absolute = Resolve(staging, relative, requireFile: true);
            V2PackageAudioInfo result;
            try
            {
                result = audioProbe?.Probe(absolute, relative) ??
                    new V2PackageAudioInfo(ProbeWaveDuration(staging, relative));
            }
            catch (V2DiagnosticException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new V2DiagnosticException(relative, "/",
                    "audio probe failed to validate the staged file", exception);
            }
            if (result is null || !double.IsFinite(result.DurationSeconds) ||
                result.DurationSeconds < 0.0)
                throw new V2DiagnosticException(relative, "/",
                    "audio probe returned an invalid duration");
            audioResults.Add(relative, result);
            return result;
        }

        var resolved = V2PackageValidator.Validate(pack, new V2PackageValidationHooks
        {
            LoadChart = entry => V2JsonDecoder.DecodeChartFile(Resolve(staging, entry.File, true)),
            FileExists = path => IsRegularFile(staging, path),
            AudioDurationSeconds = path => Probe(path).DurationSeconds,
            ValidateResolvedAudio = (entry, path) => { _ = Probe(path); },
        }, meta);

        if (!string.Equals(pack.Id, expectedPack.Id, StringComparison.Ordinal) ||
            pack.Revision != expectedPack.Revision)
            throw new V2DiagnosticException(meta, "/", "staged package differs from requested package identity");

        var digests = new Dictionary<string, V2GameplayDigestResult>(StringComparer.Ordinal);
        var audioBytesByPath = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in resolved)
        {
            var path = Resolve(staging, item.ResolvedAudio, requireFile: true);
            if (!audioBytesByPath.TryGetValue(path, out var audio))
                audioBytesByPath[path] = audio = File.ReadAllBytes(path);
            digests.Add(item.Entry.Id, V2GameplayDigest.Compute(item.Chart, item.Entry, audio));
        }
        return digests;
    }

    private static void WriteJsonFiles(string root, V2Pack pack, IReadOnlyDictionary<string, V2Chart> charts)
    {
        File.WriteAllBytes(Resolve(root, "meta.json", requireFile: false), V2JsonEncoder.EncodePack(pack));
        foreach (var entry in pack.Charts)
        {
            var path = Resolve(root, entry.File, requireFile: false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, V2JsonEncoder.EncodeChart(charts[entry.Id]));
        }
    }

    private static void PublishOverExisting(string staging, string destination)
    {
        var desired = new HashSet<string>(Directory
            .EnumerateFiles(staging, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(staging, file)), StringComparer.OrdinalIgnoreCase);
        var backups = new List<(string Target, string Backup)>();
        var published = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(destination, file);
                if (desired.Contains(relative))
                    continue;
                var backup = file + ".dynamite-universe-backup-" + Guid.NewGuid().ToString("N");
                File.Move(file, backup);
                backups.Add((file, backup));
            }
            foreach (var relative in desired.OrderBy(path => path, StringComparer.Ordinal))
            {
                var replacement = Resolve(staging, relative, requireFile: true);
                var target = Resolve(destination, relative, requireFile: false);
                if (File.Exists(target))
                {
                    var backup = target + ".dynamite-universe-backup-" + Guid.NewGuid().ToString("N");
                    File.Move(target, backup);
                    backups.Add((target, backup));
                }
                PublicationInterceptor?.Invoke(relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(replacement, target);
                published.Add(target);
            }
        }
        catch
        {
            foreach (var target in published.AsEnumerable().Reverse())
            {
                if (File.Exists(target))
                    File.Delete(target);
            }
            foreach (var (target, backup) in backups.AsEnumerable().Reverse())
            {
                if (File.Exists(backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(backup, target);
                }
            }
            throw;
        }

        // Publication has completed successfully. Cleanup is deliberately outside the rollback
        // boundary: a failed backup/staging delete must leave the newly published package intact.
        var cleanupFailures = new List<Exception>();
        foreach (var (_, backup) in backups)
        {
            try
            {
                // File.Delete is already idempotent for a missing path; calling it directly also
                // preserves access-denied errors that File.Exists would otherwise hide.
                File.Delete(backup);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(exception);
        }
        if (cleanupFailures.Count > 0)
            throw new AggregateException("Package published, but temporary cleanup failed.",
                cleanupFailures);
    }

    private static void PublishSaveAs(string staging, string destination)
    {
        if (!Directory.Exists(destination))
        {
            Directory.Move(staging, destination);
            return;
        }

        var published = new List<string>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(staging, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(staging, directory);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(staging, file);
                var target = Path.Combine(destination, relative);
                File.Move(file, target);
                published.Add(target);
            }
            Directory.Delete(staging, recursive: true);
        }
        catch
        {
            foreach (var path in published.AsEnumerable().Reverse())
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            throw;
        }
    }

    private static void PublishNewPackage(string staging, string destination)
    {
        if (!Directory.Exists(destination))
        {
            Directory.Move(staging, destination);
            return;
        }

        var placeholder = destination + ".dynamite-universe-empty-" + Guid.NewGuid().ToString("N");
        Directory.Move(destination, placeholder);
        try
        {
            Directory.Delete(placeholder);
            Directory.Move(staging, destination);
        }
        catch
        {
            if (!Directory.Exists(destination))
            {
                if (Directory.Exists(placeholder))
                    Directory.Move(placeholder, destination);
                else
                    Directory.CreateDirectory(destination);
            }
            throw;
        }
    }

    private static void RemoveObsoleteChartFiles(string staging, V2Pack sourcePack, V2Pack replacementPack)
    {
        var current = new HashSet<string>(replacementPack.Charts.Select(entry => entry.File),
            StringComparer.Ordinal);
        foreach (var file in sourcePack.Charts.Select(entry => entry.File))
        {
            if (!current.Contains(file))
            {
                var path = Resolve(staging, file, requireFile: false);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    private static void RemoveObsoleteResources(string staging, V2Pack sourcePack, V2Pack replacementPack)
    {
        var current = new HashSet<string>(ReferencedResources(replacementPack),
            StringComparer.OrdinalIgnoreCase);
        foreach (var file in ReferencedResources(sourcePack))
        {
            if (current.Contains(file))
                continue;
            var path = Resolve(staging, file, requireFile: false);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static IEnumerable<string> ReferencedResources(V2Pack pack)
    {
        if (pack.Audio is not null)
            yield return pack.Audio;
        if (pack.Cover is not null)
            yield return pack.Cover;
        foreach (var entry in pack.Charts)
        {
            if (entry.Audio is not null)
                yield return entry.Audio;
        }
    }

    private static bool SameDirectory(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        catch
        {
            // Preserve the original write failure; a unique staging directory can be cleaned manually.
        }
    }

    private static void ValidateDestination(string source, string destination, bool allowEmpty)
    {
        if (SameDirectory(source, destination))
            return;
        ValidateNewDestination(destination, allowEmpty);
    }

    private static void ValidateNewDestination(string destination, bool allowEmpty)
    {
        if (!Directory.Exists(destination))
        {
            var parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                throw new DirectoryNotFoundException("destination parent directory does not exist");
            return;
        }
        if (!allowEmpty || Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("destination must not exist or must be explicitly allowed and empty");
    }

    private static IReadOnlyList<V2ExternalResourceMapping> ValidateExternalResources(
        V2Pack pack, IReadOnlyList<V2ExternalResourceMapping> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var result = new List<V2ExternalResourceMapping>(resources.Count);
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "meta.json",
        };
        foreach (var chartFile in pack.Charts.Select(entry => entry.File))
            reserved.Add(chartFile);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < resources.Count; index++)
        {
            var resource = resources[index] ?? throw new V2DiagnosticException(
                "create-request", $"/externalResources/{index}", "resource mapping cannot be null");
            var destination = resource.PackageRelativePath;
            ValidateRelativeDestination(destination, $"/externalResources/{index}/packageRelativePath");
            if (reserved.Contains(destination) || !destinations.Add(destination))
                throw new V2DiagnosticException("create-request",
                    $"/externalResources/{index}/packageRelativePath",
                    $"duplicate or reserved package path '{destination}'");

            string source;
            try
            {
                source = Path.GetFullPath(resource.ExternalSourcePath);
                var info = new FileInfo(source);
                if (!info.Exists)
                    throw new FileNotFoundException("external resource does not exist", source);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
                    throw new IOException("external resource may not be a link or reparse point");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new V2DiagnosticException("create-request",
                    $"/externalResources/{index}/externalSourcePath",
                    "external resource must be an accessible regular file", exception);
            }
            result.Add(resource with { ExternalSourcePath = source });
        }

        var requiredResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (pack.Audio is not null)
            requiredResources.Add(pack.Audio);
        if (pack.Cover is not null)
            requiredResources.Add(pack.Cover);
        foreach (var entry in pack.Charts)
        {
            if (entry.Audio is not null)
                requiredResources.Add(entry.Audio);
        }
        foreach (var required in requiredResources)
        {
            if (!destinations.Contains(required))
                throw new V2DiagnosticException("create-request", "/externalResources",
                    $"external resource mapping is missing for referenced package file '{required}'");
        }
        return result;
    }

    private static void ValidateRelativeDestination(string relative, string pointer)
    {
        try
        {
            var probe = new V2Pack
            {
                Id = "path-probe",
                Revision = 1,
                Title = "Path Probe",
                Artist = "Path Probe",
                Audio = relative,
                Charts =
                [
                    new V2ChartEntry
                    {
                        Id = "probe",
                        Difficulty = V2Difficulty.Hard,
                        Unrated = true,
                        Charters = ["Path Probe"],
                        File = "charts/probe.json",
                    },
                ],
            };
            V2SemanticValidator.ValidatePack(probe, "create-request");
        }
        catch (V2DiagnosticException exception)
        {
            throw new V2DiagnosticException("create-request", pointer,
                "invalid package-relative resource path", exception);
        }
    }

    private static void CopyExternalResources(string staging,
        IReadOnlyList<V2ExternalResourceMapping> resources)
    {
        foreach (var resource in resources)
        {
            var target = Resolve(staging, resource.PackageRelativePath, requireFile: false);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(resource.ExternalSourcePath, target, overwrite: false);
        }
    }

    private static void CopyReplacementResources(string staging,
        IReadOnlyList<V2ExternalResourceMapping> resources)
    {
        foreach (var resource in resources)
        {
            if (resource is null)
                throw new V2DiagnosticException("save-request", "/externalResources",
                    "resource mapping cannot be null");
            ValidateRelativeDestination(resource.PackageRelativePath, "/externalResources/packageRelativePath");
            var source = Path.GetFullPath(resource.ExternalSourcePath);
            if (!File.Exists(source) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new V2DiagnosticException("save-request", "/externalResources/externalSourcePath",
                    "external resource source must be a regular file");
            var destination = Resolve(staging, resource.PackageRelativePath, requireFile: false);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static string RequireDirectoryPath(string value, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var full = Path.GetFullPath(value);
        if (File.Exists(full))
            throw new IOException($"{label} is a file: {full}");
        return full;
    }

    private static string RequireDirectory(string value, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var full = Path.GetFullPath(value);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"{label} does not exist: {full}");
        return full;
    }

    private static void CopyDirectory(string source, string staging)
    {
        Directory.CreateDirectory(staging);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(staging, relative));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    public static void ValidateDirectoryTree(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"package directory does not exist: {root}");
        var rootInfo = new DirectoryInfo(root);
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0 || rootInfo.LinkTarget is not null)
            throw new IOException("package root may not be a reparse point");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ValidateDirectoryEntries(rootInfo, root, paths);
    }

    private static void ValidateDirectoryEntries(DirectoryInfo directory, string root,
        ISet<string> paths)
    {
        foreach (var item in directory.EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0 || item.LinkTarget is not null)
                throw new IOException($"package links/reparse points are not allowed: {item.FullName}");
            var relative = Path.GetRelativePath(root, item.FullName).Replace('\\', '/');
            if (!paths.Add(relative))
                throw new IOException($"package has case-insensitive path collision: {relative}");
            if (item is DirectoryInfo child)
                ValidateDirectoryEntries(child, root, paths);
        }
    }

    public static string ResolveRegularFile(string root, string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);
        root = Path.GetFullPath(root);
        var path = Resolve(root, relative, requireFile: true);
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new IOException($"package file may not be a reparse point: {relative}");
        return path;
    }

    private static void ValidateSourceTree(string root) => ValidateDirectoryTree(root);

    private static bool IsRegularFile(string root, string relative)
    {
        try
        {
            var path = Resolve(root, relative, requireFile: true);
            var info = new FileInfo(path);
            return (info.Attributes & FileAttributes.ReparsePoint) == 0 && info.LinkTarget is null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Resolve(string root, string relative, bool requireFile)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"package path escapes root: {relative}");
        if (requireFile && !File.Exists(full))
            throw new FileNotFoundException("package file does not exist", relative);
        return full;
    }

    private static double ProbeWaveDuration(string root, string relative)
    {
        if (!Path.GetExtension(relative).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            throw new V2DiagnosticException(relative, "/",
                "editor v2 writer currently requires RIFF/WAVE audio for duration validation");
        var path = Resolve(root, relative, requireFile: true);
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        if (stream.Read(header) != header.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x46464952 ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != 0x45564157)
            throw new V2DiagnosticException(relative, "/", "invalid RIFF/WAVE audio file");

        uint? byteRate = null;
        long dataBytes = 0;
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> format = stackalloc byte[16];
        while (stream.Position + chunkHeader.Length <= stream.Length)
        {
            if (stream.Read(chunkHeader) != chunkHeader.Length)
                break;
            var id = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            var end = stream.Position + size;
            if (end > stream.Length)
                throw new V2DiagnosticException(relative, "/", "truncated RIFF/WAVE chunk");
            if (id == 0x20746d66)
            {
                if (size < 16)
                    throw new V2DiagnosticException(relative, "/", "invalid WAVE fmt chunk");
                format.Clear();
                stream.ReadExactly(format);
                var tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
                var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
                var rate = BinaryPrimitives.ReadUInt32LittleEndian(format[8..]);
                if (tag is not (1 or 3) || channels == 0 || sampleRate == 0 || rate == 0)
                    throw new V2DiagnosticException(relative, "/", "unsupported WAVE format (PCM/IEEE float required)");
                byteRate = rate;
            }
            else if (id == 0x61746164)
            {
                dataBytes = checked(dataBytes + size);
            }
            stream.Position = end + (size & 1);
        }
        if (byteRate is null || dataBytes == 0)
            throw new V2DiagnosticException(relative, "/", "WAVE fmt or data chunk is missing");
        var duration = dataBytes / (double)byteRate.Value;
        if (!double.IsFinite(duration) || duration < 0)
            throw new V2DiagnosticException(relative, "/", "invalid WAVE duration");
        return duration;
    }
}
