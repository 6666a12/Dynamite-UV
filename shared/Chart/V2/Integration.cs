using System.Text;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// Typed client-facing bridge result. It contains the compatibility runtime graph and the stable v2
/// identity/evaluator data required by gameplay, rendering, and score persistence.
/// </summary>
public sealed record V2LoadedPackageChart
{
    public required DynamiteUniverse.Shared.Chart.Chart RuntimeChart { get; init; }
    public required string RulesetId { get; init; }
    public required string GameplayDigest { get; init; }
    public required JudgePreset Preset { get; init; }
    public required IReadOnlySet<int> SyncAccentRuntimeIds { get; init; }
    public required IReadOnlyDictionary<int, string> SourceIdsByRuntimeId { get; init; }
    public required string PackId { get; init; }
    public required string ChartId { get; init; }
    public required string ResolvedAudioPath { get; init; }
    public required V2Pack SemanticPack { get; init; }
    public required V2Chart SemanticChart { get; init; }
    public required V2GameplayDigestResult DigestResult { get; init; }
    public int NoteCount => RuntimeChart.TotalMainNote;

    // Memoized per RuntimeChart instance so `with` copies stay correct: replacing RuntimeChart
    // invalidates the guard, while an unchanged reference reuses the cached value.
    private Chart? _durationCacheChart;
    private double _durationCacheSec;

    public double DurationSec
    {
        get
        {
            if (ReferenceEquals(_durationCacheChart, RuntimeChart))
                return _durationCacheSec;
            var unitEnd = RuntimeChart.AllNotes
                .Where(note => note.Type != NoteType.BarLine)
                .Select(note => note.Second)
                .DefaultIfEmpty(0.0)
                .Max();
            var sustainEnd = RuntimeChart.AllNotes
                .Where(note => note.Type is NoteType.HoldNode or NoteType.MixerNode &&
                               note.SubNoteId == -1)
                .Select(note => note.Second)
                .DefaultIfEmpty(0.0)
                .Max();
            _durationCacheSec = Math.Max(unitEnd, sustainEnd);
            _durationCacheChart = RuntimeChart;
            return _durationCacheSec;
        }
    }
}

/// <summary>
/// Strict package/file integration plus clean legacy conversion. Filesystem loading rejects package
/// escapes and reparse points and validates all declared chart/audio references before adapting the
/// selected chart.
/// </summary>
public static class V2Integration
{
    public static V2LoadedPackageChart LoadPackageChart(
        string packageDirectory, string chartId) => LoadStrict(packageDirectory, chartId);

    public static V2LoadedPackageChart LoadV2PackageChart(
        string packageDirectory, string chartId) => LoadStrict(packageDirectory, chartId);

    public static V2LoadedPackageChart LoadStrict(string packageDirectory, string chartId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(chartId);
        var root = Path.GetFullPath(packageDirectory);
        ValidatePackageTree(root);
        var metaPath = ResolvePackagePath(root, "meta.json");
        var pack = V2JsonDecoder.DecodePackFile(metaPath);
        var entry = pack.Charts.FirstOrDefault(item =>
            string.Equals(item.Id, chartId, StringComparison.Ordinal)) ??
            throw new V2DiagnosticException(metaPath, "/charts",
                $"chart ID '{chartId}' is not declared by the pack");

        var resolved = V2PackageValidator.Validate(pack, new V2PackageValidationHooks
        {
            FileExists = relative => IsRegularPackageFile(root, relative),
            LoadChart = declaration => V2JsonDecoder.DecodeChartFile(
                ResolvePackagePath(root, declaration.File)),
            ValidateResolvedAudio = (declaration, relative) =>
            {
                ResolvePackagePath(root, relative);
            },
        }, metaPath);
        var selected = resolved.Single(item => ReferenceEquals(item.Entry, entry));
        var audioPath = ResolvePackagePath(root, selected.ResolvedAudio);
        var audioBytes = File.ReadAllBytes(audioPath);
        return BuildLoaded(pack, entry, selected.Chart, audioBytes, audioPath);
    }

    /// <summary>Strict in-memory entry point suitable for virtual/package filesystems.</summary>
    public static V2LoadedPackageChart LoadStrict(
        string metaJson, string chartJson, string chartId,
        ReadOnlySpan<byte> resolvedAudioBytes, string resolvedAudioPath = "audio")
    {
        var pack = V2JsonDecoder.DecodePack(metaJson);
        var entry = pack.Charts.FirstOrDefault(item =>
            string.Equals(item.Id, chartId, StringComparison.Ordinal)) ??
            throw new V2DiagnosticException("meta.json", "/charts",
                $"chart ID '{chartId}' is not declared by the pack");
        var chart = V2JsonDecoder.DecodeChart(chartJson, entry.File);
        if (!string.Equals(chart.ChartId, entry.Id, StringComparison.Ordinal))
            throw new V2DiagnosticException(entry.File, "/chartId",
                $"chartId '{chart.ChartId}' does not match pack entry ID '{entry.Id}'");
        return BuildLoaded(pack, entry, chart, resolvedAudioBytes, resolvedAudioPath);
    }

    public static V2LoadedPackageChart LoadLegacyPackageChart(
        string legacyChartPath, ReadOnlySpan<byte> resolvedAudioBytes,
        string packId = "legacy.pack", string? chartId = null,
        JudgePreset preset = JudgePreset.Hard) =>
        ConvertLegacyPackageChart(DynamixChartLoader.LoadFile(legacyChartPath),
            resolvedAudioBytes, packId, chartId, preset, legacyChartPath);

    public static V2LoadedPackageChart LoadLegacyPackageChart(
        string legacyChartPath, string packId = "legacy.pack", string? chartId = null,
        JudgePreset preset = JudgePreset.Hard) =>
        LoadLegacyPackageChart(legacyChartPath, ReadOnlySpan<byte>.Empty,
            packId, chartId, preset);

    public static V2LoadedPackageChart LoadLegacyAsV2(
        DynamiteUniverse.Shared.Chart.Chart legacyChart, ReadOnlySpan<byte> resolvedAudioBytes,
        string packId = "legacy.pack", string? chartId = null,
        JudgePreset preset = JudgePreset.Hard) =>
        ConvertLegacyPackageChart(legacyChart, resolvedAudioBytes,
            packId, chartId, preset);

    public static V2LoadedPackageChart ConvertLegacyPackageChart(
        DynamiteUniverse.Shared.Chart.Chart legacyChart, ReadOnlySpan<byte> resolvedAudioBytes,
        string packId = "legacy.pack", string? chartId = null,
        JudgePreset preset = JudgePreset.Hard, string resolvedAudioPath = "audio")
    {
        var semanticChart = ConvertLegacyChart(legacyChart, chartId ?? SafeId(legacyChart.Name, "chart"));
        var difficulty = preset switch
        {
            JudgePreset.Casual => V2Difficulty.Casual,
            JudgePreset.Normal => V2Difficulty.Normal,
            JudgePreset.Hard => V2Difficulty.Hard,
            // Tutorial 取 Casual 数值；Hardcore 是 Hard 的窗口变体，语义难度按 Hard 归档。
            JudgePreset.Tutorial => V2Difficulty.Casual,
            JudgePreset.Hardcore => V2Difficulty.Hard,
            _ => throw new ArgumentException(
                $"Unsupported legacy-to-v2 conversion preset '{preset}'.", nameof(preset)),
        };
        var entry = new V2ChartEntry
        {
            Id = semanticChart.ChartId,
            Difficulty = difficulty,
            Unrated = true,
            Charters = ["Legacy conversion"],
            File = "chart.json",
            Audio = "audio",
        };
        var pack = new V2Pack
        {
            Id = SafeId(packId, "legacy.pack"),
            Revision = 1,
            Title = ValidDisplay(legacyChart.Title, "Legacy chart"),
            Artist = "Legacy conversion",
            Charts = [entry],
        };
        V2SemanticValidator.ValidatePack(pack);
        return BuildLoaded(pack, entry, semanticChart,
            resolvedAudioBytes, resolvedAudioPath);
    }

    public static V2LoadedPackageChart ConvertLegacyPackageChart(
        DynamiteUniverse.Shared.Chart.Chart legacyChart, string packId = "legacy.pack",
        string? chartId = null, JudgePreset preset = JudgePreset.Hard) =>
        ConvertLegacyPackageChart(legacyChart, ReadOnlySpan<byte>.Empty,
            packId, chartId, preset);

    /// <summary>Converts a legacy runtime graph into canonical semantic v2 structures.</summary>
    public static V2Chart ConvertLegacyChart(
        DynamiteUniverse.Shared.Chart.Chart legacyChart, string chartId = "legacy.chart")
    {
        ArgumentNullException.ThrowIfNull(legacyChart);
        if (legacyChart.Sections.Count == 0)
            throw new InvalidDataException("Legacy-to-v2 conversion requires a BPM timeline.");
        var notes = new Dictionary<Track, List<V2Note>>
        {
            [Track.Left] = [], [Track.Center] = [], [Track.Right] = [],
        };
        var allNotes = legacyChart.AllNotes.ToArray();
        var allById = new Dictionary<int, Note>();
        foreach (var note in allNotes)
        {
            if (!allById.TryAdd(note.Id, note))
                throw new InvalidDataException($"Duplicate legacy note ID '{note.Id}'.");
        }
        var incoming = new Dictionary<int, int>();
        foreach (var note in allNotes)
        {
            if (note.SubNoteId == -1)
                continue;
            if (!allById.ContainsKey(note.SubNoteId))
                throw new InvalidDataException(
                    $"Legacy note '{note.Id}' has dangling SubNoteId '{note.SubNoteId}'.");
            incoming[note.SubNoteId] = incoming.GetValueOrDefault(note.SubNoteId) + 1;
            if (incoming[note.SubNoteId] > 1)
                throw new InvalidDataException(
                    $"Legacy note '{note.SubNoteId}' is shared by multiple sustain chains.");
        }
        var consumedPathNodeIds = new HashSet<int>();
        foreach (var track in new[] { Track.Left, Track.Center, Track.Right })
        {
            var source = legacyChart.NotesOf(track);
            foreach (var note in source)
            {
                if (note.Track != track)
                    throw new InvalidDataException(
                        $"Legacy note '{note.Id}' is stored on the wrong track.");
                if (note.Type is NoteType.HoldNode or NoteType.MixerNode)
                    continue;
                notes[track].Add(ConvertLegacyNote(note, allById, track,
                    consumedPathNodeIds));
            }
            notes[track].Sort(static (left, right) =>
            {
                var comparison = left.Time.CompareTo(right.Time);
                return comparison != 0
                    ? comparison
                    : StringComparer.Ordinal.Compare(left.Id, right.Id);
            });
        }
        foreach (var node in allNotes.Where(note =>
                     note.Type is NoteType.HoldNode or NoteType.MixerNode))
        {
            if (!consumedPathNodeIds.Contains(node.Id))
                throw new InvalidDataException(
                    $"Legacy path node '{node.Id}' is not reachable from a matching head.");
        }

        var orderedSections = legacyChart.Sections.ToArray();
        for (var i = 0; i < orderedSections.Length; i++)
        {
            var section = orderedSections[i];
            if (!double.IsFinite(section.Bpm) || section.Bpm <= 0.0)
                throw new InvalidDataException($"Legacy BPM section {i} must be finite and positive.");
            if (!double.IsFinite(section.Seconds))
                throw new InvalidDataException($"Legacy BPM section {i} Seconds must be finite.");
            if (i > 0 && section.BarTime <= orderedSections[i - 1].BarTime)
                throw new InvalidDataException(
                    "Legacy BPM BarTime values must be strictly increasing.");
            if (i > 0)
            {
                var previous = orderedSections[i - 1];
                var expected = previous.Seconds +
                    (section.BarTime - previous.BarTime) * 240.0 / previous.Bpm;
                if (!double.IsFinite(expected) || Math.Abs(expected - section.Seconds) > 1e-9)
                {
                    throw new InvalidDataException(
                        $"Legacy BPM section {i} Seconds continuity mismatch: " +
                        $"stored={section.Seconds:R}, expected={expected:R}.");
                }
            }
        }

        var firstSection = orderedSections[0];
        var firstTime = SafeBarTime(firstSection.BarTime);
        var offset = firstSection.Seconds - firstSection.BarTime * 240.0 / firstSection.Bpm;
        if (!double.IsFinite(offset))
            throw new InvalidDataException("Legacy bar-0 audio offset is not finite.");
        var bpms = orderedSections
            .Select(section => new V2BpmEvent(SafeBarTime(section.BarTime), section.Bpm))
            .ToList();
        if (firstTime > ExactBarTime.Zero)
            bpms.Insert(0, new V2BpmEvent(ExactBarTime.Zero, firstSection.Bpm));

        // Source order is authoritative for duplicate legacy DropSpeed events: the later event wins.
        var scrollsByTime = new SortedDictionary<ExactBarTime, V2ScrollEvent>();
        foreach (var item in legacyChart.DropSpeeds)
        {
            var time = SafeBarTime(item.BarTime);
            scrollsByTime[time] = new V2ScrollEvent
            {
                Time = time,
                Value = item.Mult,
                CurveToNext = V2ScrollCurve.Linear,
            };
        }
        var scrolls = scrollsByTime.Values.ToList();
        if (scrolls.Count > 0 && scrolls[0].Time > ExactBarTime.Zero)
        {
            scrolls.Insert(0, new V2ScrollEvent
            {
                Time = ExactBarTime.Zero,
                Value = scrolls[0].Value,
                CurveToNext = V2ScrollCurve.Linear,
            });
        }
        if (scrolls.Count > 0)
            scrolls[^1] = scrolls[^1] with { CurveToNext = null };

        var result = new V2Chart
        {
            ChartId = SafeId(chartId, "legacy.chart"),
            AudioOffsetSec = offset,
            Bpms = bpms,
            ScrollSpeeds = scrolls,
            NotesLeft = notes[Track.Left],
            NotesCenter = notes[Track.Center],
            NotesRight = notes[Track.Right],
        };
        V2SemanticValidator.ValidateChart(result);
        return result;
    }

    private static V2LoadedPackageChart BuildLoaded(V2Pack pack, V2ChartEntry entry,
        V2Chart chart, ReadOnlySpan<byte> audioBytes, string resolvedAudioPath)
    {
        var digest = V2GameplayDigest.Compute(chart, entry, audioBytes);
        var adapted = V2RuntimeAdapter.Adapt(chart, pack.Title, DifficultyNumber(entry.Difficulty));
        return new V2LoadedPackageChart
        {
            RuntimeChart = adapted.RuntimeChart,
            RulesetId = V2Format.RulesetId,
            GameplayDigest = digest.Sha256,
            Preset = entry.JudgePreset,
            SyncAccentRuntimeIds = adapted.SyncAccentRuntimeIds,
            SourceIdsByRuntimeId = adapted.SourceIdsByRuntimeId,
            PackId = pack.Id,
            ChartId = chart.ChartId,
            ResolvedAudioPath = resolvedAudioPath,
            SemanticPack = pack,
            SemanticChart = chart,
            DigestResult = digest,
        };
    }

    private static V2Note ConvertLegacyNote(Note note, IReadOnlyDictionary<int, Note> byId,
        Track track, HashSet<int> consumedPathNodeIds)
    {
        var id = LegacyNoteId(note.Id);
        var time = SafeBarTime(note.BarTime);
        var center = CanonicalZero(note.Position + note.Width / 2.0);
        if (note.Type is not (NoteType.HoldHead or NoteType.MixerHead))
        {
            return new V2BasicNote
            {
                Id = id,
                Type = note.Type switch
                {
                    NoteType.Tap => V2NoteType.Tap,
                    NoteType.Drag => V2NoteType.Drag,
                    NoteType.ExTap => V2NoteType.ExTap,
                    NoteType.Mine => V2NoteType.Mine,
                    NoteType.BarLine => V2NoteType.BarLine,
                    _ => throw new InvalidDataException($"Unsupported legacy note type {note.Type}."),
                },
                Time = time,
                Center = center,
                Width = note.Width,
            };
        }

        var type = note.Type == NoteType.HoldHead ? V2NoteType.Hold : V2NoteType.Mixer;
        var expectedNodeType = note.Type == NoteType.HoldHead
            ? NoteType.HoldNode : NoteType.MixerNode;
        var nodes = new List<V2PathNode>();
        var seen = new HashSet<int> { note.Id };
        var next = note.SubNoteId;
        var previousTime = time;
        while (next != -1)
        {
            if (!seen.Add(next) || !byId.TryGetValue(next, out var node))
                throw new InvalidDataException($"Legacy sustain '{note.Id}' is cyclic or dangling.");
            if (node.Track != track)
                throw new InvalidDataException($"Legacy sustain '{note.Id}' crosses tracks.");
            if (node.Type != expectedNodeType)
                throw new InvalidDataException(
                    $"Legacy sustain '{note.Id}' links to wrong node type {node.Type}.");
            if (!consumedPathNodeIds.Add(node.Id))
                throw new InvalidDataException(
                    $"Legacy path node '{node.Id}' is shared by multiple sustain chains.");
            var nodeTime = SafeBarTime(node.BarTime);
            if (nodeTime <= previousTime)
                throw new InvalidDataException(
                    $"Legacy sustain '{note.Id}' node times must strictly increase.");
            nodes.Add(new V2PathNode
            {
                Id = LegacyNoteId(node.Id),
                Time = nodeTime,
                Center = CanonicalZero(node.Position + node.Width / 2.0),
                Width = node.Width,
                CurveToNext = V2PathCurve.Linear,
                Judge = type == V2NoteType.Hold ? true : null,
                JudgeWasExplicit = type == V2NoteType.Hold,
            });
            previousTime = nodeTime;
            next = node.SubNoteId;
        }
        if (nodes.Count == 0)
            throw new InvalidDataException($"Legacy sustain '{note.Id}' has no tail node.");
        nodes[^1] = nodes[^1] with { CurveToNext = null };
        return new V2PathNote
        {
            Id = id,
            Type = type,
            Time = time,
            Center = center,
            Width = note.Width,
            CurveToNext = V2PathCurve.Linear,
            Nodes = nodes,
        };
    }

    private static ExactBarTime SafeBarTime(double value)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new InvalidDataException("Legacy BarTime must be finite and non-negative.");
        var exact = ExactBarTime.FromDouble(value);
        if (!exact.IsJsonSafeCanonical)
        {
            throw new InvalidDataException(
                $"Legacy BarTime {value:R} cannot be represented within v2 JSON-safe components " +
                $"(bar={exact.Bar}, numerator={exact.Numerator}, denominator={exact.Denominator}).");
        }
        return ExactBarTime.FromJsonComponents(exact.Bar, exact.Numerator, exact.Denominator);
    }

    private static double CanonicalZero(double value) => value == 0.0 ? 0.0 : value;

    private static string LegacyNoteId(int id) => $"legacy-{id}";

    private static string SafeId(string? value, string fallback)
    {
        if (!string.IsNullOrEmpty(value) && value.Length <= 64 &&
            IsAsciiAlphaNumeric(value[0]) &&
            value.Skip(1).All(c => IsAsciiAlphaNumeric(c) || c is '.' or '_' or '-'))
            return value;
        return fallback;
    }

    private static string ValidDisplay(string? value, string fallback)
    {
        if (string.IsNullOrEmpty(value))
            return fallback;
        var normalized = value.Normalize(NormalizationForm.FormC).Trim();
        if (normalized.Length == 0 || normalized.Any(c => c <= '\u001f' || c == '\u007f') ||
            normalized.EnumerateRunes().Count() > 256)
            return fallback;
        return normalized;
    }

    private static int DifficultyNumber(V2Difficulty difficulty) => difficulty switch
    {
        V2Difficulty.Casual => 1,
        V2Difficulty.Normal => 2,
        V2Difficulty.Hard => 3,
        V2Difficulty.Mega => 4,
        V2Difficulty.Giga => 5,
        V2Difficulty.Tech => 6,
        _ => 0,
    };

    private static void ValidatePackageTree(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Package entry is a reparse point: {path}");
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (!paths.Add(relative))
                throw new InvalidDataException(
                    $"Package contains a case-insensitive path collision: {relative}");
        }
    }

    private static bool IsRegularPackageFile(string root, string relative)
    {
        try
        {
            var path = ResolvePackagePath(root, relative);
            if (!File.Exists(path))
                return false;
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
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

    private static string ResolvePackagePath(string root, string relative)
    {
        var platformRelative = relative.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, platformRelative));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                         Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Package path escapes root: {relative}");
        return full;
    }

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}

/// <summary>Compatibility facade with the client-requested package-loader naming.</summary>
public static class V2PackageLoader
{
    public static V2LoadedPackageChart LoadPackageChart(string packageDirectory, string chartId) =>
        V2Integration.LoadPackageChart(packageDirectory, chartId);

    public static V2LoadedPackageChart LoadV2PackageChart(string packageDirectory, string chartId) =>
        V2Integration.LoadV2PackageChart(packageDirectory, chartId);

    public static V2LoadedPackageChart LoadStrict(string packageDirectory, string chartId) =>
        V2Integration.LoadStrict(packageDirectory, chartId);

    public static V2LoadedPackageChart LoadLegacyPackageChart(string legacyChartPath,
        string packId = "legacy.pack", string? chartId = null,
        JudgePreset preset = JudgePreset.Hard) =>
        V2Integration.LoadLegacyPackageChart(legacyChartPath, packId, chartId, preset);

    public static V2LoadedPackageChart LoadLegacyAsV2(DynamiteUniverse.Shared.Chart.Chart chart,
        string packId = "legacy.pack", string? chartId = null,
        JudgePreset preset = JudgePreset.Hard) =>
        V2Integration.ConvertLegacyPackageChart(chart, packId, chartId, preset);

    public static V2LoadedPackageChart ConvertLegacyPackageChart(DynamiteUniverse.Shared.Chart.Chart chart,
        string packId = "legacy.pack", string? chartId = null,
        JudgePreset preset = JudgePreset.Hard) =>
        V2Integration.ConvertLegacyPackageChart(chart, packId, chartId, preset);
}
