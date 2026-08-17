using System.Numerics;
using System.Text;

namespace DuxShared.Chart.V2;

/// <summary>Semantic validation not expressible by the v2 JSON schemas.</summary>
public static class V2SemanticValidator
{
    public static void ValidatePack(V2Pack pack, string sourceName = "meta.json")
    {
        ArgumentNullException.ThrowIfNull(pack);
        if (pack.Revision < 1 || pack.Revision > (long)ExactBarTime.MaxSafeInteger)
            throw Error(sourceName, "/revision", "revision must be a JSON-safe integer >= 1");
        ValidateDisplayText(pack.Title, sourceName, "/title", 256);
        ValidateDisplayText(pack.Artist, sourceName, "/artist", 256);
        if (pack.Audio is not null)
            ValidatePackagePath(pack.Audio, sourceName, "/audio");
        if (pack.Cover is not null)
            ValidatePackagePath(pack.Cover, sourceName, "/cover");
        if (pack.Preview is not null)
            ValidatePreview(pack.Preview, sourceName, "/preview");
        if (pack.Charts is null || pack.Charts.Count == 0)
            throw Error(sourceName, "/charts", "pack must contain at least one chart entry");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        var customKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < pack.Charts.Count; i++)
        {
            var entry = pack.Charts[i];
            var pointer = $"/charts/{i}";
            ValidateId(entry.Id, sourceName, pointer + "/id");
            if (!ids.Add(entry.Id))
                throw Error(sourceName, pointer + "/id", $"duplicate chart ID '{entry.Id}'");
            ValidatePackagePath(entry.File, sourceName, pointer + "/file");
            if (!files.Add(entry.File))
                throw Error(sourceName, pointer + "/file",
                    $"duplicate chart file reference '{entry.File}'");
            if (entry.Audio is not null)
                ValidatePackagePath(entry.Audio, sourceName, pointer + "/audio");
            if (entry.Audio is null && pack.Audio is null)
                throw Error(sourceName, pointer + "/audio",
                    "chart has no entry-level or pack-level resolved audio");
            if (entry.Preview is not null)
                ValidatePreview(entry.Preview, sourceName, pointer + "/preview");
            if (entry.Charters is null || entry.Charters.Count == 0)
                throw Error(sourceName, pointer + "/charters",
                    "charters must contain at least one display name");
            for (var charterIndex = 0; charterIndex < entry.Charters.Count; charterIndex++)
                ValidateDisplayText(entry.Charters[charterIndex], sourceName,
                    $"{pointer}/charters/{charterIndex}", 256);

            if (entry.Difficulty == V2Difficulty.Custom)
            {
                if (entry.DifficultyKey is null)
                    throw Error(sourceName, pointer + "/difficultyKey",
                        "custom difficulty requires difficultyKey");
                ValidateDisplayText(entry.DifficultyKey, sourceName,
                    pointer + "/difficultyKey", 24);
                var folded = DefaultCaseFold(entry.DifficultyKey.Normalize(NormalizationForm.FormC));
                if (!customKeys.Add(folded))
                    throw Error(sourceName, pointer + "/difficultyKey",
                        "custom difficultyKey duplicates another key after NFC and case folding");
            }
            else if (entry.DifficultyKey is not null)
            {
                throw Error(sourceName, pointer + "/difficultyKey",
                    "standard difficulty must omit difficultyKey");
            }

            if ((entry.Level is null) == !entry.Unrated)
                throw Error(sourceName, pointer,
                    "exactly one of level or unrated:true is required");
            if (entry.Level is < 1 or > 99)
                throw Error(sourceName, pointer + "/level", "level must be in the range 1..99");
        }
    }

    public static void ValidateChart(V2Chart chart, string sourceName = "chart.json")
    {
        ArgumentNullException.ThrowIfNull(chart);
        ValidateId(chart.ChartId, sourceName, "/chartId");
        RequireFinite(chart.AudioOffsetSec, sourceName, "/audioOffsetSec", "audioOffsetSec");

        if (chart.Bpms is null || chart.Bpms.Count == 0)
            throw Error(sourceName, "/timing/bpms", "BPM timeline must not be empty");
        if (chart.Bpms[0].Time != ExactBarTime.Zero)
            throw Error(sourceName, "/timing/bpms/0/time", "first BPM event must be at BarTime 0");
        for (var i = 0; i < chart.Bpms.Count; i++)
        {
            var bpm = chart.Bpms[i];
            ValidateBarTime(bpm.Time, sourceName, $"/timing/bpms/{i}/time");
            if (!double.IsFinite(bpm.Bpm) || bpm.Bpm <= 0.0)
                throw Error(sourceName, $"/timing/bpms/{i}/bpm", "BPM must be finite and > 0");
            if (i > 0 && bpm.Time <= chart.Bpms[i - 1].Time)
                throw Error(sourceName, $"/timing/bpms/{i}/time",
                    "BPM event times must be strictly increasing");
        }

        if (chart.ScrollSpeeds is null)
            throw Error(sourceName, "/scrollSpeeds", "scrollSpeeds cannot be null");
        if (chart.ScrollSpeeds.Count > 0 && chart.ScrollSpeeds[0].Time != ExactBarTime.Zero)
            throw Error(sourceName, "/scrollSpeeds/0/time",
                "first nonempty scroll event must be at BarTime 0");
        for (var i = 0; i < chart.ScrollSpeeds.Count; i++)
        {
            var scroll = chart.ScrollSpeeds[i];
            ValidateBarTime(scroll.Time, sourceName, $"/scrollSpeeds/{i}/time");
            if (!double.IsFinite(scroll.Value) || scroll.Value <= 0.0 || scroll.Value > 64.0)
                throw Error(sourceName, $"/scrollSpeeds/{i}/value",
                    "scroll value must be finite and in (0,64]");
            if (i > 0 && scroll.Time <= chart.ScrollSpeeds[i - 1].Time)
                throw Error(sourceName, $"/scrollSpeeds/{i}/time",
                    "scroll event times must be strictly increasing");
            if (i == chart.ScrollSpeeds.Count - 1 && scroll.CurveToNext is not null)
                throw Error(sourceName, $"/scrollSpeeds/{i}/curveToNext",
                    "tail scroll event must omit curveToNext");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        BigInteger judgementCount = BigInteger.Zero;
        ValidateTrack(chart.NotesLeft, "/notesLeft", sourceName, ids, ref judgementCount);
        ValidateTrack(chart.NotesCenter, "/notesCenter", sourceName, ids, ref judgementCount);
        ValidateTrack(chart.NotesRight, "/notesRight", sourceName, ids, ref judgementCount);
        if (judgementCount.IsZero)
            throw Error(sourceName, "/", "chart must contain at least one main judgement unit");
    }

    /// <summary>Returns the exact main-judgement count without allocating runtime units.</summary>
    public static BigInteger CountMainJudgements(V2Chart chart)
    {
        BigInteger count = BigInteger.Zero;
        foreach (var (_, note) in chart.AllNotes)
        {
            count += note.Type switch
            {
                V2NoteType.BarLine => BigInteger.Zero,
                V2NoteType.Hold => BigInteger.One +
                    ((V2PathNote)note).Nodes.Count(node => node.Judge == true),
                V2NoteType.Mixer => BigInteger.One + V2MixerTicks.TickCount((V2PathNote)note),
                _ => BigInteger.One,
            };
        }
        return count;
    }

    private static void ValidateTrack(IReadOnlyList<V2Note>? notes, string pointer,
        string sourceName, HashSet<string> ids, ref BigInteger judgementCount)
    {
        if (notes is null)
            throw Error(sourceName, pointer, "note array cannot be null");
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            var notePointer = $"{pointer}/{i}";
            if (i > 0 && note.Time < notes[i - 1].Time)
                throw Error(sourceName, notePointer + "/time",
                    "note array must be sorted by exact BarTime");
            ValidateId(note.Id, sourceName, notePointer + "/id");
            if (!ids.Add(note.Id))
                throw Error(sourceName, notePointer + "/id",
                    $"duplicate chart-wide parent/node ID '{note.Id}'");
            ValidateBarTime(note.Time, sourceName, notePointer + "/time");
            ValidatePoint(note.Center, note.Width, sourceName, notePointer);

            if (note is not V2PathNote path)
            {
                if (note.Type is V2NoteType.Hold or V2NoteType.Mixer)
                    throw Error(sourceName, notePointer, "Hold and Mixer must use path-note semantics");
                judgementCount += note.Type == V2NoteType.BarLine ? BigInteger.Zero : BigInteger.One;
                continue;
            }
            if (path.Type is not (V2NoteType.Hold or V2NoteType.Mixer))
                throw Error(sourceName, notePointer, "only Hold and Mixer may contain nodes");
            if (path.Nodes is null || path.Nodes.Count == 0)
                throw Error(sourceName, notePointer + "/nodes",
                    "Hold and Mixer require at least one path node");

            var previousTime = path.Time;
            for (var nodeIndex = 0; nodeIndex < path.Nodes.Count; nodeIndex++)
            {
                var node = path.Nodes[nodeIndex];
                var nodePointer = $"{notePointer}/nodes/{nodeIndex}";
                ValidateId(node.Id, sourceName, nodePointer + "/id");
                if (!ids.Add(node.Id))
                    throw Error(sourceName, nodePointer + "/id",
                        $"duplicate chart-wide parent/node ID '{node.Id}'");
                ValidateBarTime(node.Time, sourceName, nodePointer + "/time");
                if (node.Time <= previousTime)
                    throw Error(sourceName, nodePointer + "/time",
                        "path node time must be strictly later than the previous point");
                previousTime = node.Time;
                ValidatePoint(node.Center, node.Width, sourceName, nodePointer);
                if (nodeIndex == path.Nodes.Count - 1 && node.CurveToNext is not null)
                    throw Error(sourceName, nodePointer + "/curveToNext",
                        "tail path node must omit curveToNext");
                if (path.Type == V2NoteType.Hold)
                {
                    if (node.Judge is null)
                        throw Error(sourceName, nodePointer + "/judge",
                            "Hold node must resolve judge to true or false");
                    if (node.Judge == true)
                        judgementCount++;
                }
                else if (node.Judge is not null || node.JudgeWasExplicit)
                {
                    throw Error(sourceName, nodePointer + "/judge",
                        "Mixer nodes must not carry judge state");
                }
            }

            var tail = path.Nodes[^1];
            if (path.Type == V2NoteType.Hold && (!tail.JudgeWasExplicit || tail.Judge != true))
                throw Error(sourceName, $"{notePointer}/nodes/{path.Nodes.Count - 1}/judge",
                    "Hold tail must explicitly set judge:true");

            var evaluator = new V2PathEvaluator(path);
            if (!evaluator.IsFiniteAndPositive(out var failingInterval, out var reason))
                throw Error(sourceName, failingInterval < 0
                    ? notePointer
                    : failingInterval == 0
                        ? notePointer + "/curveToNext"
                        : $"{notePointer}/nodes/{failingInterval - 1}/curveToNext", reason);

            judgementCount += path.Type == V2NoteType.Hold
                ? BigInteger.One
                : BigInteger.One + V2MixerTicks.TickCount(path);
        }
    }

    private static void ValidatePoint(double center, double width,
        string sourceName, string pointer)
    {
        RequireFinite(center, sourceName, pointer + "/center", "center");
        if (!double.IsFinite(width) || width <= 0.0)
            throw Error(sourceName, pointer + "/width", "width must be finite and > 0");
    }

    private static void ValidatePreview(V2Preview preview, string sourceName, string pointer)
    {
        if (!double.IsFinite(preview.StartSec) || preview.StartSec < 0.0)
            throw Error(sourceName, pointer + "/startSec", "preview startSec must be finite and >= 0");
        if (!double.IsFinite(preview.DurationSec) || preview.DurationSec <= 0.0)
            throw Error(sourceName, pointer + "/durationSec", "preview durationSec must be finite and > 0");
        if (!double.IsFinite(preview.StartSec + preview.DurationSec))
            throw Error(sourceName, pointer, "preview end time must be finite");
    }

    private static void RequireFinite(double value, string sourceName, string pointer, string label)
    {
        if (!double.IsFinite(value))
            throw Error(sourceName, pointer, $"{label} must be finite");
    }

    private static void ValidateBarTime(ExactBarTime value, string sourceName, string pointer)
    {
        if (!value.IsJsonSafeCanonical)
            throw Error(sourceName, pointer,
                "BarTime must be non-negative canonical mixed rational with JSON-safe components");
    }

    private static void ValidateId(string? value, string sourceName, string pointer)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsAsciiAlphaNumeric(value[0]) ||
            value.Skip(1).Any(c => !IsAsciiAlphaNumeric(c) && c is not ('.' or '_' or '-')))
            throw Error(sourceName, pointer,
                "ID must match ^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$");
    }

    private static void ValidatePackagePath(string? value, string sourceName, string pointer)
    {
        if (string.IsNullOrEmpty(value) || value.EnumerateRunes().Count() > 1024 ||
            value[0] == '/' || value[^1] == '/' || value.Contains('\\') ||
            value.Contains('\0') || value.Contains(':') ||
            value.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw Error(sourceName, pointer,
                "invalid package-relative file path");
    }

    private static void ValidateDisplayText(string? value, string sourceName,
        string pointer, int maxCodePoints)
    {
        if (string.IsNullOrEmpty(value) || value.EnumerateRunes().Count() > maxCodePoints ||
            !value.IsNormalized(NormalizationForm.FormC) ||
            char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]) ||
            value.Any(c => c <= '\u001f' || c == '\u007f'))
            throw Error(sourceName, pointer,
                $"display text must be NFC, 1..{maxCodePoints} code points, trimmed, and control-free");
    }

    // Invariant upper+lower handles the platform's multi-character casing and yields a stable,
    // culture-independent caseless key for package-local collision checks.
    private static string DefaultCaseFold(string value) =>
        value.ToUpperInvariant().ToLowerInvariant();

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private static V2DiagnosticException Error(string source, string pointer, string reason) =>
        new(source, pointer, reason);
}

/// <summary>Optional I/O hooks used by cross-file package validation.</summary>
public sealed record V2PackageValidationHooks
{
    /// <summary>Returns a decoded chart for the entry. Decoder/semantic errors are propagated.</summary>
    public required Func<V2ChartEntry, V2Chart> LoadChart { get; init; }
    /// <summary>Checks that a package-relative regular file exists after safe package resolution.</summary>
    public Func<string, bool>? FileExists { get; init; }
    /// <summary>Returns decoded audio duration, or null when duration probing is delegated elsewhere.</summary>
    public Func<string, double?>? AudioDurationSeconds { get; init; }
    /// <summary>Called for each resolved audio path after ordinary existence checks.</summary>
    public Action<V2ChartEntry, string>? ValidateResolvedAudio { get; init; }
}

/// <summary>Result of cross-file validation for a chart entry.</summary>
public sealed record V2ResolvedChart(
    V2ChartEntry Entry, V2Chart Chart, string ResolvedAudio,
    V2Preview? ResolvedPreview, V2BpmTimeline BpmTimeline);

/// <summary>Cross-file pack/chart/audio validation with host-supplied filesystem and decoder hooks.</summary>
public static class V2PackageValidator
{
    public static IReadOnlyList<V2ResolvedChart> Validate(
        V2Pack pack, V2PackageValidationHooks hooks, string sourceName = "meta.json")
    {
        ArgumentNullException.ThrowIfNull(hooks);
        V2SemanticValidator.ValidatePack(pack, sourceName);
        if (pack.Cover is not null && hooks.FileExists is not null && !hooks.FileExists(pack.Cover))
            throw new V2DiagnosticException(sourceName, "/cover",
                $"referenced cover '{pack.Cover}' does not exist as a regular package file");
        var result = new List<V2ResolvedChart>(pack.Charts.Count);
        for (var i = 0; i < pack.Charts.Count; i++)
        {
            var entry = pack.Charts[i];
            var pointer = $"/charts/{i}";
            if (hooks.FileExists is not null && !hooks.FileExists(entry.File))
                throw new V2DiagnosticException(sourceName, pointer + "/file",
                    $"referenced chart file '{entry.File}' does not exist as a regular package file");
            var chart = hooks.LoadChart(entry) ??
                throw new V2DiagnosticException(sourceName, pointer + "/file",
                    "chart loader returned null");
            V2SemanticValidator.ValidateChart(chart, entry.File);
            if (!string.Equals(chart.ChartId, entry.Id, StringComparison.Ordinal))
                throw new V2DiagnosticException(entry.File, "/chartId",
                    $"chartId '{chart.ChartId}' does not match pack entry ID '{entry.Id}'");

            var audio = pack.ResolveAudio(entry);
            if (hooks.FileExists is not null && !hooks.FileExists(audio))
                throw new V2DiagnosticException(sourceName,
                    entry.Audio is null ? "/audio" : pointer + "/audio",
                    $"resolved audio '{audio}' does not exist as a regular package file");
            hooks.ValidateResolvedAudio?.Invoke(entry, audio);

            var duration = hooks.AudioDurationSeconds?.Invoke(audio);
            if (duration is not null)
            {
                if (!double.IsFinite(duration.Value) || duration.Value < 0.0)
                    throw new V2DiagnosticException(sourceName, pointer + "/audio",
                        "audio duration hook returned an invalid duration");
                ValidateAudioBounds(pack, entry, chart, duration.Value, sourceName, pointer);
            }

            result.Add(new V2ResolvedChart(entry, chart, audio,
                pack.ResolvePreview(entry), new V2BpmTimeline(chart.Bpms, chart.AudioOffsetSec)));
        }
        return result;
    }

    private static void ValidateAudioBounds(V2Pack pack, V2ChartEntry entry, V2Chart chart,
        double audioDuration, string sourceName, string pointer)
    {
        var preview = pack.ResolvePreview(entry);
        if (preview is not null && preview.StartSec + preview.DurationSec > audioDuration)
            throw new V2DiagnosticException(sourceName,
                entry.Preview is null ? "/preview" : pointer + "/preview",
                "resolved preview extends beyond decoded audio duration");

        var timeline = new V2BpmTimeline(chart.Bpms, chart.AudioOffsetSec);
        double? latest = null;
        foreach (var (_, note) in chart.AllNotes)
        {
            IEnumerable<ExactBarTime> times = note.Type switch
            {
                V2NoteType.BarLine => [],
                V2NoteType.Hold => HoldTimes((V2PathNote)note),
                V2NoteType.Mixer => MixerTimes((V2PathNote)note),
                _ => [note.Time],
            };
            foreach (var time in times)
            {
                var second = timeline.ToSeconds(time);
                if (second < 0.0)
                    throw new V2DiagnosticException(entry.File, "/",
                        $"main judgement at {time} maps before audio start ({second:R}s)");
                latest = latest is null ? second : Math.Max(latest.Value, second);
            }
        }
        if (latest > audioDuration + 0.050)
            throw new V2DiagnosticException(entry.File, "/",
                $"last main judgement at {latest:R}s exceeds audio duration {audioDuration:R}s plus 0.050s tolerance");
    }

    private static IEnumerable<ExactBarTime> HoldTimes(V2PathNote hold)
    {
        yield return hold.Time;
        foreach (var node in hold.Nodes)
        {
            if (node.Judge == true)
                yield return node.Time;
        }
    }

    private static IEnumerable<ExactBarTime> MixerTimes(V2PathNote mixer)
    {
        yield return mixer.Time;
        foreach (var tick in V2MixerTicks.Enumerate(mixer))
            yield return tick;
    }
}
