using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>A fail-closed v2 diagnostic carrying the source name and RFC 6901 JSON Pointer.</summary>
public sealed class V2DiagnosticException : FormatException
{
    public string SourceName { get; }
    public string JsonPointer { get; }
    public string Reason { get; }

    public V2DiagnosticException(string sourceName, string jsonPointer, string reason,
        Exception? innerException = null)
        : base($"{sourceName} {NormalizePointer(jsonPointer)}: {reason}", innerException)
    {
        SourceName = sourceName;
        JsonPointer = NormalizePointer(jsonPointer);
        Reason = reason;
    }

    private static string NormalizePointer(string pointer) =>
        string.IsNullOrEmpty(pointer) ? "/" : pointer;
}

/// <summary>
/// Strict System.Text.Json decoder for the frozen v2 pack and chart contracts. It rejects duplicate
/// properties before model construction and never enables permissive comments, trailing commas,
/// named floating-point values, case-insensitive names, or unknown fields.
/// </summary>
public static class V2JsonDecoder
{
    private const long MaxSafeInteger = 9_007_199_254_740_991L;

    public static V2Pack DecodePackFile(string path) =>
        DecodePack(File.ReadAllBytes(path), path);

    public static V2Chart DecodeChartFile(string path) =>
        DecodeChart(File.ReadAllBytes(path), path);

    public static V2Pack DecodePack(string json, string sourceName = "meta.json") =>
        DecodePack(Encoding.UTF8.GetBytes(json), sourceName);

    public static V2Chart DecodeChart(string json, string sourceName = "chart.json") =>
        DecodeChart(Encoding.UTF8.GetBytes(json), sourceName);

    public static V2Pack DecodePack(byte[] utf8Json, string sourceName = "meta.json") =>
        DecodePack((ReadOnlyMemory<byte>)utf8Json, sourceName);

    public static V2Pack DecodePack(ReadOnlyMemory<byte> utf8Json, string sourceName = "meta.json")
    {
        using var document = Parse(utf8Json, sourceName);
        var root = RequireObject(document.RootElement, sourceName, "");
        var properties = Properties(root, sourceName, "", PackFields);
        RequireLiteral(properties, "format", V2Format.PackFormat, sourceName, "");
        RequireIntegerLiteral(properties, "formatVersion", V2Format.FormatVersion, sourceName, "");

        var pack = new V2Pack
        {
            Id = ReadId(Required(properties, "id", sourceName, ""), sourceName, "/id"),
            Revision = ReadSafeInteger(Required(properties, "revision", sourceName, ""),
                sourceName, "/revision", 1),
            Title = ReadDisplayText(Required(properties, "title", sourceName, ""),
                sourceName, "/title", 256),
            Artist = ReadDisplayText(Required(properties, "artist", sourceName, ""),
                sourceName, "/artist", 256),
            Audio = OptionalPath(properties, "audio", sourceName, ""),
            Cover = OptionalPath(properties, "cover", sourceName, ""),
            Preview = OptionalPreview(properties, "preview", sourceName, ""),
            Charts = ReadChartEntries(Required(properties, "charts", sourceName, ""),
                sourceName, "/charts"),
        };
        V2SemanticValidator.ValidatePack(pack, sourceName);
        return pack;
    }

    public static V2Pack DecodePack(ReadOnlySpan<byte> utf8Json, string sourceName = "meta.json") =>
        DecodePack(utf8Json.ToArray(), sourceName);

    public static V2Chart DecodeChart(byte[] utf8Json, string sourceName = "chart.json") =>
        DecodeChart((ReadOnlyMemory<byte>)utf8Json, sourceName);

    public static V2Chart DecodeChart(ReadOnlyMemory<byte> utf8Json, string sourceName = "chart.json")
    {
        using var document = Parse(utf8Json, sourceName);
        var root = RequireObject(document.RootElement, sourceName, "");
        var properties = Properties(root, sourceName, "", ChartFields);
        RequireLiteral(properties, "format", V2Format.ChartFormat, sourceName, "");
        RequireIntegerLiteral(properties, "formatVersion", V2Format.FormatVersion, sourceName, "");

        var timingElement = Required(properties, "timing", sourceName, "");
        var timing = Properties(RequireObject(timingElement, sourceName, "/timing"),
            sourceName, "/timing", TimingFields);
        var chart = new V2Chart
        {
            ChartId = ReadId(Required(properties, "chartId", sourceName, ""),
                sourceName, "/chartId"),
            AudioOffsetSec = ReadFiniteNumber(
                Required(properties, "audioOffsetSec", sourceName, ""),
                sourceName, "/audioOffsetSec"),
            Bpms = ReadBpms(Required(timing, "bpms", sourceName, "/timing"),
                sourceName, "/timing/bpms"),
            ScrollSpeeds = properties.TryGetValue("scrollSpeeds", out var scroll)
                ? ReadScrollSpeeds(scroll, sourceName, "/scrollSpeeds")
                : [],
            NotesLeft = ReadNotes(Required(properties, "notesLeft", sourceName, ""),
                sourceName, "/notesLeft"),
            NotesCenter = ReadNotes(Required(properties, "notesCenter", sourceName, ""),
                sourceName, "/notesCenter"),
            NotesRight = ReadNotes(Required(properties, "notesRight", sourceName, ""),
                sourceName, "/notesRight"),
        };
        V2SemanticValidator.ValidateChart(chart, sourceName);
        return chart;
    }

    public static V2Chart DecodeChart(ReadOnlySpan<byte> utf8Json, string sourceName = "chart.json") =>
        DecodeChart(utf8Json.ToArray(), sourceName);

    private static JsonDocument Parse(ReadOnlyMemory<byte> utf8Json, string sourceName)
    {
        try
        {
            var document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 128,
            });
            ValidateJsonTree(document.RootElement, sourceName, "");
            return document;
        }
        catch (V2DiagnosticException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            var location = exception.LineNumber is null
                ? "invalid JSON"
                : $"invalid JSON at line {exception.LineNumber + 1}, byte {exception.BytePositionInLine + 1}";
            throw Error(sourceName, exception.Path ?? "", $"{location}: {exception.Message}", exception);
        }
    }

    private static void ValidateJsonTree(JsonElement element, string source, string pointer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    var childPointer = Child(pointer, property.Name);
                    if (!names.Add(property.Name))
                        throw Error(source, childPointer,
                            $"duplicate JSON property name '{property.Name}'");
                    ValidateStringValue(property.Name, source, childPointer);
                    ValidateJsonTree(property.Value, source, childPointer);
                }
                break;
            }
            case JsonValueKind.Array:
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    ValidateJsonTree(item, source, Child(pointer, index++));
                break;
            }
            case JsonValueKind.String:
                ValidateStringValue(element.GetString()!, source, pointer);
                break;
            case JsonValueKind.Number:
                if (!element.TryGetDouble(out var number) || !double.IsFinite(number))
                    throw Error(source, pointer, "number must be finite IEEE 754 binary64");
                break;
        }
    }

    private static void ValidateStringValue(string value, string source, string pointer)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i]))
                continue;
            if (!char.IsHighSurrogate(value[i]) || i + 1 >= value.Length ||
                !char.IsLowSurrogate(value[i + 1]))
                throw Error(source, pointer, "string contains an unpaired UTF-16 surrogate");
            i++;
        }
    }

    private static IReadOnlyList<V2ChartEntry> ReadChartEntries(
        JsonElement element, string source, string pointer)
    {
        var array = RequireArray(element, source, pointer);
        if (array.GetArrayLength() == 0)
            throw Error(source, pointer, "charts must contain at least one entry");
        var result = new List<V2ChartEntry>(array.GetArrayLength());
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var itemPointer = Child(pointer, index++);
            var properties = Properties(RequireObject(item, source, itemPointer),
                source, itemPointer, ChartEntryFields);
            var difficultyElement = Required(properties, "difficulty", source, itemPointer);
            var difficultyText = ReadString(difficultyElement, source,
                Child(itemPointer, "difficulty"));
            if (!Difficulties.TryGetValue(difficultyText, out var difficulty))
                throw Error(source, Child(itemPointer, "difficulty"),
                    $"unsupported difficulty '{difficultyText}'");

            var hasKey = properties.TryGetValue("difficultyKey", out var difficultyKeyElement);
            if (difficulty == V2Difficulty.Custom && !hasKey)
                throw Error(source, Child(itemPointer, "difficultyKey"),
                    "custom difficulty requires difficultyKey");
            if (difficulty != V2Difficulty.Custom && hasKey)
                throw Error(source, Child(itemPointer, "difficultyKey"),
                    "standard difficulty must omit difficultyKey");

            var hasLevel = properties.TryGetValue("level", out var levelElement);
            var hasUnrated = properties.TryGetValue("unrated", out var unratedElement);
            if (hasLevel == hasUnrated)
                throw Error(source, itemPointer,
                    "exactly one of level or unrated:true is required");
            if (hasUnrated && !ReadBoolean(unratedElement, source,
                    Child(itemPointer, "unrated")))
                throw Error(source, Child(itemPointer, "unrated"), "unrated must be true");

            var chartersElement = Required(properties, "charters", source, itemPointer);
            var charterArray = RequireArray(chartersElement, source, Child(itemPointer, "charters"));
            if (charterArray.GetArrayLength() == 0)
                throw Error(source, Child(itemPointer, "charters"),
                    "charters must contain at least one display name");
            var charters = new List<string>(charterArray.GetArrayLength());
            var charterIndex = 0;
            foreach (var charter in charterArray.EnumerateArray())
            {
                charters.Add(ReadDisplayText(charter, source,
                    Child(Child(itemPointer, "charters"), charterIndex++), 256));
            }

            result.Add(new V2ChartEntry
            {
                Id = ReadId(Required(properties, "id", source, itemPointer), source,
                    Child(itemPointer, "id")),
                Difficulty = difficulty,
                DifficultyKey = hasKey
                    ? ReadDisplayText(difficultyKeyElement, source,
                        Child(itemPointer, "difficultyKey"), 24)
                    : null,
                Level = hasLevel
                    ? checked((int)ReadSafeInteger(levelElement, source,
                        Child(itemPointer, "level"), 1, 99))
                    : null,
                Unrated = hasUnrated,
                Charters = charters,
                File = ReadPackagePath(Required(properties, "file", source, itemPointer),
                    source, Child(itemPointer, "file")),
                Audio = OptionalPath(properties, "audio", source, itemPointer),
                Preview = OptionalPreview(properties, "preview", source, itemPointer),
            });
        }
        return result;
    }

    private static IReadOnlyList<V2BpmEvent> ReadBpms(
        JsonElement element, string source, string pointer)
    {
        var array = RequireArray(element, source, pointer);
        if (array.GetArrayLength() == 0)
            throw Error(source, pointer, "bpms must contain at least one event");
        var result = new List<V2BpmEvent>(array.GetArrayLength());
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var itemPointer = Child(pointer, index++);
            var properties = Properties(RequireObject(item, source, itemPointer),
                source, itemPointer, BpmFields);
            result.Add(new V2BpmEvent(
                ReadBarTime(Required(properties, "time", source, itemPointer), source,
                    Child(itemPointer, "time")),
                ReadFiniteNumber(Required(properties, "bpm", source, itemPointer), source,
                    Child(itemPointer, "bpm"))));
        }
        return result;
    }

    private static IReadOnlyList<V2ScrollEvent> ReadScrollSpeeds(
        JsonElement element, string source, string pointer)
    {
        var array = RequireArray(element, source, pointer);
        var result = new List<V2ScrollEvent>(array.GetArrayLength());
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var itemPointer = Child(pointer, index++);
            var properties = Properties(RequireObject(item, source, itemPointer),
                source, itemPointer, ScrollFields);
            result.Add(new V2ScrollEvent
            {
                Time = ReadBarTime(Required(properties, "time", source, itemPointer), source,
                    Child(itemPointer, "time")),
                Value = ReadFiniteNumber(Required(properties, "value", source, itemPointer),
                    source, Child(itemPointer, "value")),
                CurveToNext = properties.TryGetValue("curveToNext", out var curve)
                    ? ReadScrollCurve(curve, source, Child(itemPointer, "curveToNext"))
                    : null,
            });
        }
        return result;
    }

    private static IReadOnlyList<V2Note> ReadNotes(
        JsonElement element, string source, string pointer)
    {
        var array = RequireArray(element, source, pointer);
        var result = new List<V2Note>(array.GetArrayLength());
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var itemPointer = Child(pointer, index++);
            var obj = RequireObject(item, source, itemPointer);
            var typeProperty = obj.EnumerateObject()
                .Where(property => property.NameEquals("type"))
                .ToArray();
            if (typeProperty.Length == 0)
                throw Error(source, Child(itemPointer, "type"), "required property is missing");
            var typeText = ReadString(typeProperty[0].Value, source, Child(itemPointer, "type"));
            if (!NoteTypes.TryGetValue(typeText, out var noteType))
                throw Error(source, Child(itemPointer, "type"),
                    $"unsupported note type '{typeText}'");

            var isPath = noteType is V2NoteType.Hold or V2NoteType.Mixer;
            var properties = Properties(obj, source, itemPointer,
                isPath ? PathNoteFields : BasicNoteFields);
            var common = new
            {
                Id = ReadId(Required(properties, "id", source, itemPointer), source,
                    Child(itemPointer, "id")),
                Time = ReadBarTime(Required(properties, "time", source, itemPointer), source,
                    Child(itemPointer, "time")),
                Center = ReadFiniteNumber(Required(properties, "center", source, itemPointer),
                    source, Child(itemPointer, "center")),
                Width = ReadFiniteNumber(Required(properties, "width", source, itemPointer),
                    source, Child(itemPointer, "width")),
            };

            if (!isPath)
            {
                result.Add(new V2BasicNote
                {
                    Id = common.Id,
                    Type = noteType,
                    Time = common.Time,
                    Center = common.Center,
                    Width = common.Width,
                });
                continue;
            }

            var nodes = ReadPathNodes(Required(properties, "nodes", source, itemPointer),
                source, Child(itemPointer, "nodes"), noteType == V2NoteType.Hold);
            result.Add(new V2PathNote
            {
                Id = common.Id,
                Type = noteType,
                Time = common.Time,
                Center = common.Center,
                Width = common.Width,
                CurveToNext = properties.TryGetValue("curveToNext", out var curve)
                    ? ReadPathCurve(curve, source, Child(itemPointer, "curveToNext"))
                    : null,
                Nodes = nodes,
            });
        }
        return result;
    }

    private static IReadOnlyList<V2PathNode> ReadPathNodes(
        JsonElement element, string source, string pointer, bool hold)
    {
        var array = RequireArray(element, source, pointer);
        if (array.GetArrayLength() == 0)
            throw Error(source, pointer, "path notes require at least one node");
        var result = new List<V2PathNode>(array.GetArrayLength());
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var itemPointer = Child(pointer, index++);
            var properties = Properties(RequireObject(item, source, itemPointer), source,
                itemPointer, hold ? HoldNodeFields : MixerNodeFields);
            var hasJudge = properties.TryGetValue("judge", out var judge);
            result.Add(new V2PathNode
            {
                Id = ReadId(Required(properties, "id", source, itemPointer), source,
                    Child(itemPointer, "id")),
                Time = ReadBarTime(Required(properties, "time", source, itemPointer), source,
                    Child(itemPointer, "time")),
                Center = ReadFiniteNumber(Required(properties, "center", source, itemPointer),
                    source, Child(itemPointer, "center")),
                Width = ReadFiniteNumber(Required(properties, "width", source, itemPointer),
                    source, Child(itemPointer, "width")),
                CurveToNext = properties.TryGetValue("curveToNext", out var curve)
                    ? ReadPathCurve(curve, source, Child(itemPointer, "curveToNext"))
                    : null,
                Judge = hold
                    ? hasJudge ? ReadBoolean(judge, source, Child(itemPointer, "judge")) : true
                    : null,
                JudgeWasExplicit = hold && hasJudge,
            });
        }
        return result;
    }

    private static ExactBarTime ReadBarTime(JsonElement element, string source, string pointer)
    {
        var properties = Properties(RequireObject(element, source, pointer), source, pointer,
            BarTimeFields);
        var bar = ReadSafeInteger(Required(properties, "bar", source, pointer), source,
            Child(pointer, "bar"), 0);
        var numerator = ReadSafeInteger(Required(properties, "numerator", source, pointer), source,
            Child(pointer, "numerator"), 0);
        var denominator = ReadSafeInteger(
            Required(properties, "denominator", source, pointer), source,
            Child(pointer, "denominator"), 1);
        try
        {
            return ExactBarTime.FromJsonComponents(bar, numerator, denominator);
        }
        catch (ArgumentException exception)
        {
            throw Error(source, pointer, $"noncanonical BarTime: {exception.Message}", exception);
        }
    }

    private static V2Preview? OptionalPreview(
        IReadOnlyDictionary<string, JsonElement> parent, string name,
        string source, string parentPointer)
    {
        if (!parent.TryGetValue(name, out var element))
            return null;
        var pointer = Child(parentPointer, name);
        var properties = Properties(RequireObject(element, source, pointer), source,
            pointer, PreviewFields);
        return new V2Preview(
            ReadFiniteNumber(Required(properties, "startSec", source, pointer), source,
                Child(pointer, "startSec")),
            ReadFiniteNumber(Required(properties, "durationSec", source, pointer), source,
                Child(pointer, "durationSec")));
    }

    private static string? OptionalPath(IReadOnlyDictionary<string, JsonElement> parent,
        string name, string source, string parentPointer) =>
        parent.TryGetValue(name, out var element)
            ? ReadPackagePath(element, source, Child(parentPointer, name))
            : null;

    private static string ReadPackagePath(JsonElement element, string source, string pointer)
    {
        var value = ReadString(element, source, pointer);
        if (value.Length == 0)
            throw Error(source, pointer, "package path must not be empty");
        if (CodePointCount(value) > 1024)
            throw Error(source, pointer, "package path exceeds 1024 Unicode code points");
        if (value[0] == '/' || value[^1] == '/' || value.Contains('\\') ||
            value.Contains('\0') || value.Contains(':'))
            throw Error(source, pointer,
                "package path must be a slash-separated relative path without scheme, drive, backslash, or NUL");
        foreach (var segment in value.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
                throw Error(source, pointer,
                    "package path must not contain empty, '.' or '..' segments");
        }
        return value;
    }

    private static string ReadId(JsonElement element, string source, string pointer)
    {
        var value = ReadString(element, source, pointer);
        if (value.Length is < 1 or > 64 || !IsAsciiAlphaNumeric(value[0]) ||
            value.Skip(1).Any(c => !IsAsciiAlphaNumeric(c) && c is not ('.' or '_' or '-')))
            throw Error(source, pointer,
                "ID must match ^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$");
        return value;
    }

    private static string ReadDisplayText(
        JsonElement element, string source, string pointer, int maxCodePoints)
    {
        var value = ReadString(element, source, pointer);
        var length = CodePointCount(value);
        if (length < 1 || length > maxCodePoints)
            throw Error(source, pointer,
                $"display text must contain 1..{maxCodePoints} Unicode code points");
        if (!value.IsNormalized(NormalizationForm.FormC))
            throw Error(source, pointer, "display text must be Unicode NFC");
        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
            throw Error(source, pointer, "display text must not have leading or trailing whitespace");
        if (value.Any(c => c <= '\u001f' || c == '\u007f'))
            throw Error(source, pointer, "display text must not contain C0 controls or DEL");
        return value;
    }

    private static long ReadSafeInteger(JsonElement element, string source, string pointer,
        long minimum, long maximum = MaxSafeInteger)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value))
            throw Error(source, pointer, "value must be an integer");
        if (value < minimum || value > maximum)
            throw Error(source, pointer,
                $"integer must be in the range {minimum}..{maximum}");
        return value;
    }

    private static double ReadFiniteNumber(JsonElement element, string source, string pointer)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value))
            throw Error(source, pointer, "value must be a JSON number");
        if (!double.IsFinite(value))
            throw Error(source, pointer, "number must be finite IEEE 754 binary64");
        return value == 0.0 ? 0.0 : value;
    }

    private static bool ReadBoolean(JsonElement element, string source, string pointer)
    {
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Error(source, pointer, "value must be a boolean");
        return element.GetBoolean();
    }

    private static string ReadString(JsonElement element, string source, string pointer)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw Error(source, pointer, "value must be a string");
        return element.GetString()!;
    }

    private static V2PathCurve ReadPathCurve(JsonElement element, string source, string pointer)
    {
        var text = ReadString(element, source, pointer);
        if (!PathCurves.TryGetValue(text, out var curve))
            throw Error(source, pointer, $"unsupported path curve '{text}'");
        return curve;
    }

    private static V2ScrollCurve ReadScrollCurve(JsonElement element, string source, string pointer)
    {
        var text = ReadString(element, source, pointer);
        if (!ScrollCurves.TryGetValue(text, out var curve))
            throw Error(source, pointer, $"unsupported scroll curve '{text}'");
        return curve;
    }

    private static IReadOnlyDictionary<string, JsonElement> Properties(
        JsonElement element, string source, string pointer, HashSet<string> allowed)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                throw Error(source, Child(pointer, property.Name),
                    $"unknown property '{property.Name}'");
            result.Add(property.Name, property.Value);
        }
        return result;
    }

    private static JsonElement Required(IReadOnlyDictionary<string, JsonElement> properties,
        string name, string source, string pointer)
    {
        if (!properties.TryGetValue(name, out var value))
            throw Error(source, Child(pointer, name), "required property is missing");
        return value;
    }

    private static void RequireLiteral(IReadOnlyDictionary<string, JsonElement> properties,
        string name, string expected, string source, string pointer)
    {
        var actual = ReadString(Required(properties, name, source, pointer), source,
            Child(pointer, name));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw Error(source, Child(pointer, name),
                $"expected '{expected}', found '{actual}'");
    }

    private static void RequireIntegerLiteral(IReadOnlyDictionary<string, JsonElement> properties,
        string name, long expected, string source, string pointer)
    {
        var actual = ReadSafeInteger(Required(properties, name, source, pointer), source,
            Child(pointer, name), 0);
        if (actual != expected)
            throw Error(source, Child(pointer, name),
                $"unsupported format version {actual}; expected {expected}");
    }

    private static JsonElement RequireObject(JsonElement element, string source, string pointer)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Error(source, pointer, "value must be an object");
        return element;
    }

    private static JsonElement RequireArray(JsonElement element, string source, string pointer)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw Error(source, pointer, "value must be an array");
        return element;
    }

    private static int CodePointCount(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
            count++;
        return count;
    }

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    internal static string Child(string pointer, string property) =>
        pointer + "/" + property.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    internal static string Child(string pointer, int index) =>
        pointer + "/" + index.ToString(CultureInfo.InvariantCulture);

    internal static V2DiagnosticException Error(string source, string pointer, string reason,
        Exception? inner = null) => new(source, pointer, reason, inner);

    private static readonly HashSet<string> PackFields =
        ["format", "formatVersion", "id", "revision", "title", "artist", "audio", "cover", "preview", "charts"];
    private static readonly HashSet<string> ChartEntryFields =
        ["id", "difficulty", "difficultyKey", "level", "unrated", "charters", "file", "audio", "preview"];
    private static readonly HashSet<string> PreviewFields = ["startSec", "durationSec"];
    private static readonly HashSet<string> ChartFields =
        ["format", "formatVersion", "chartId", "audioOffsetSec", "timing", "scrollSpeeds", "notesLeft", "notesCenter", "notesRight"];
    private static readonly HashSet<string> TimingFields = ["bpms"];
    private static readonly HashSet<string> BpmFields = ["time", "bpm"];
    private static readonly HashSet<string> ScrollFields = ["time", "value", "curveToNext"];
    private static readonly HashSet<string> BarTimeFields = ["bar", "numerator", "denominator"];
    private static readonly HashSet<string> BasicNoteFields = ["id", "type", "time", "center", "width"];
    private static readonly HashSet<string> PathNoteFields = ["id", "type", "time", "center", "width", "curveToNext", "nodes"];
    private static readonly HashSet<string> HoldNodeFields = ["id", "time", "center", "width", "curveToNext", "judge"];
    private static readonly HashSet<string> MixerNodeFields = ["id", "time", "center", "width", "curveToNext"];

    private static readonly Dictionary<string, V2Difficulty> Difficulties = new(StringComparer.Ordinal)
    {
        ["casual"] = V2Difficulty.Casual,
        ["normal"] = V2Difficulty.Normal,
        ["hard"] = V2Difficulty.Hard,
        ["mega"] = V2Difficulty.Mega,
        ["giga"] = V2Difficulty.Giga,
        ["tech"] = V2Difficulty.Tech,
        ["custom"] = V2Difficulty.Custom,
    };

    private static readonly Dictionary<string, V2NoteType> NoteTypes = new(StringComparer.Ordinal)
    {
        ["tap"] = V2NoteType.Tap,
        ["drag"] = V2NoteType.Drag,
        ["exTap"] = V2NoteType.ExTap,
        ["hold"] = V2NoteType.Hold,
        ["mixer"] = V2NoteType.Mixer,
        ["mine"] = V2NoteType.Mine,
        ["barLine"] = V2NoteType.BarLine,
    };

    private static readonly Dictionary<string, V2PathCurve> PathCurves = new(StringComparer.Ordinal)
    {
        ["linear"] = V2PathCurve.Linear,
        ["hold"] = V2PathCurve.Hold,
        ["easeInQuad"] = V2PathCurve.EaseInQuad,
        ["easeOutQuad"] = V2PathCurve.EaseOutQuad,
        ["easeInOutCubic"] = V2PathCurve.EaseInOutCubic,
        ["smooth"] = V2PathCurve.Smooth,
    };

    private static readonly Dictionary<string, V2ScrollCurve> ScrollCurves = new(StringComparer.Ordinal)
    {
        ["linear"] = V2ScrollCurve.Linear,
        ["hold"] = V2ScrollCurve.Hold,
        ["easeInQuad"] = V2ScrollCurve.EaseInQuad,
        ["easeOutQuad"] = V2ScrollCurve.EaseOutQuad,
        ["easeInOutCubic"] = V2ScrollCurve.EaseInOutCubic,
    };
}
