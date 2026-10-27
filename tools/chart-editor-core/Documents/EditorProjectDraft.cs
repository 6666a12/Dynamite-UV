using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Typed user input for a new, not-yet-persisted editor project.</summary>
public sealed record EditorProjectDraftRequest(
    string PackId,
    string Title,
    string Artist,
    string ChartId,
    V2Difficulty Difficulty,
    string? DifficultyKey,
    int? Level,
    bool Unrated,
    string Charter,
    double InitialBpm,
    double AudioOffsetSec,
    int GridDivisor,
    string ExternalAudioSource,
    string? ExternalCoverSource);

/// <summary>
/// A validated in-memory authoring draft together with the external resources needed by its first
/// package creation. Resource destinations are deterministic safe v2 package-relative paths.
/// </summary>
public sealed record EditorProjectDraft(
    EditorProjectDraftRequest Request,
    EditorDocument Document,
    string ExternalAudioSource,
    string? ExternalCoverSource,
    string ChartDestination,
    string AudioDestination,
    string? CoverDestination,
    IV2PackageAudioProbe? AudioProbe = null)
{
    public IReadOnlyDictionary<string, string> ExternalResources { get; } =
        BuildExternalResources(ExternalAudioSource, ExternalCoverSource,
            AudioDestination, CoverDestination);

    public V2PackageCreateRequest BuildPackageCreateRequest(
        string destinationDirectory, bool allowEmptyDestination = false) => new()
    {
        DestinationDirectory = destinationDirectory,
        Pack = Document.BuildPackSnapshot(),
        Charts = Document.BuildAllChartSnapshots(),
        ExternalResources = ExternalResources.Select(resource =>
            new V2ExternalResourceMapping(resource.Value, resource.Key)).ToArray(),
        AudioProbe = AudioProbe,
        AllowEmptyDestination = allowEmptyDestination,
    };

    private static IReadOnlyDictionary<string, string> BuildExternalResources(
        string audioSource, string? coverSource, string audioDestination, string? coverDestination)
    {
        var resources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [audioDestination] = audioSource,
        };
        if (coverSource is not null && coverDestination is not null)
            resources.Add(coverDestination, coverSource);
        return resources;
    }
}

/// <summary>Creates validated empty editor drafts without weakening strict persisted v2 rules.</summary>
public static class EditorProjectDraftFactory
{
    private static readonly IReadOnlyDictionary<string, string> SupportedAudioExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".wav"] = ".wav",
            [".mp3"] = ".mp3",
            [".flac"] = ".flac",
            [".ogg"] = ".ogg",
            [".opus"] = ".opus",
            [".m4a"] = ".m4a",
            [".aac"] = ".aac",
        };

    public static bool IsSupportedAudioPath(string path) =>
        SupportedAudioExtensions.ContainsKey(Path.GetExtension(path));

    public static string AudioDestinationFor(string sourcePath)
    {
        if (!SupportedAudioExtensions.TryGetValue(Path.GetExtension(sourcePath), out var extension))
            throw new V2DiagnosticException("new-project", "/audioSource",
                "audio source must use .wav, .mp3, .flac, .ogg, .opus, .m4a, or .aac");
        return "audio" + extension;
    }

    public static EditorProjectDraft Create(EditorProjectDraftRequest request,
        IV2PackageAudioProbe? audioProbe = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var audioSource = RequireExternalRegularFile(request.ExternalAudioSource,
            "new-project", "/audioSource");
        var audioDestination = AudioDestinationFor(audioSource);
        var coverSource = request.ExternalCoverSource is null
            ? null
            : RequireExternalRegularFile(request.ExternalCoverSource,
                "new-project", "/coverSource");

        if (!double.IsFinite(request.InitialBpm) || request.InitialBpm <= 0.0)
            throw new V2DiagnosticException("new-project", "/initialBpm",
                "initial BPM must be finite and > 0");
        if (!double.IsFinite(request.AudioOffsetSec))
            throw new V2DiagnosticException("new-project", "/audioOffsetSec",
                "audio offset must be finite");
        if (request.GridDivisor <= 0)
            throw new V2DiagnosticException("new-project", "/gridDivisor",
                "grid divisor must be positive");

        var chartDestination = $"charts/{request.ChartId}.json";
        var coverDestination = coverSource is null ? null : CoverDestination(coverSource);
        var entry = new V2ChartEntry
        {
            Id = request.ChartId,
            Difficulty = request.Difficulty,
            DifficultyKey = request.DifficultyKey,
            Level = request.Level,
            Unrated = request.Unrated,
            Charters = [request.Charter],
            File = chartDestination,
        };
        var pack = new V2Pack
        {
            Id = request.PackId,
            Revision = 1,
            Title = request.Title,
            Artist = request.Artist,
            Audio = audioDestination,
            Cover = coverDestination,
            Charts = [entry],
        };

        V2SemanticValidator.ValidatePack(pack, "new-project");
        _ = V2JsonDecoder.DecodePack(V2JsonEncoder.EncodePack(pack), "new-project");
        var chart = new V2Chart
        {
            ChartId = request.ChartId,
            AudioOffsetSec = request.AudioOffsetSec,
            Bpms = [new V2BpmEvent(ExactBarTime.Zero, request.InitialBpm)],
            NotesLeft = [],
            NotesCenter = [],
            NotesRight = [],
        };
        ValidateEmptyDraftChart(chart, chartDestination);
        var document = EditorDocument.FromPackage(null, pack,
            new Dictionary<string, V2Chart>(StringComparer.Ordinal)
            {
                [request.ChartId] = chart,
            });
        document.GridDivisor = request.GridDivisor;

        return new EditorProjectDraft(request, document, audioSource, coverSource,
            chartDestination, audioDestination, coverDestination, audioProbe);
    }

    private static void ValidateEmptyDraftChart(V2Chart chart, string source)
    {
        try
        {
            V2SemanticValidator.ValidateChart(chart, source);
        }
        catch (V2DiagnosticException exception) when (
            exception.JsonPointer == "/" &&
            exception.Reason == "chart must contain at least one main judgement unit")
        {
            // An in-memory draft may be empty. Strict package creation still uses the unchanged v2
            // validator and therefore fails until the author adds a scoring note.
        }
    }

    private static string RequireExternalRegularFile(string path, string source, string pointer)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new V2DiagnosticException(source, pointer,
                "external resource path must not be empty");
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
            var info = new FileInfo(fullPath);
            if (!info.Exists)
                throw new FileNotFoundException("external resource does not exist", fullPath);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
                throw new IOException("external resource may not be a link or reparse point");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new V2DiagnosticException(source, pointer,
                "external resource must be an accessible regular file", exception);
        }
        return fullPath;
    }

    private static string CoverDestination(string source)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension.Length is < 2 or > 16 ||
            extension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
            extension = string.Empty;
        return "cover" + extension;
    }
}
