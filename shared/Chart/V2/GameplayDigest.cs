using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// RFC 8785 JSON Canonicalization Scheme for finite binary64 JSON data. Object member names are
/// ordered by UTF-16 code units, strings use the mandated minimal JSON escaping, and numbers use
/// the platform shortest round-trip digits reshaped to ECMAScript fixed/scientific thresholds.
/// </summary>
public static class JsonCanonicalizer
{
    public static string Canonicalize(JsonElement element)
    {
        var builder = new StringBuilder();
        AppendElement(builder, element);
        return builder.ToString();
    }

    /// <summary>
    /// Canonicalizes a JSON-compatible object graph: null, bool, string, finite numeric primitives,
    /// JsonElement, string-keyed dictionaries, and enumerable arrays.
    /// </summary>
    public static string Canonicalize(object? value)
    {
        var builder = new StringBuilder();
        AppendValue(builder, value);
        return builder.ToString();
    }

    public static byte[] CanonicalizeUtf8(object? value) =>
        Encoding.UTF8.GetBytes(Canonicalize(value));

    private static void AppendElement(StringBuilder builder, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.String:
                AppendString(builder, element.GetString()!);
                break;
            case JsonValueKind.Number:
                if (!element.TryGetDouble(out var number) || !double.IsFinite(number))
                    throw new ArgumentException("JCS requires finite IEEE 754 binary64 numbers.",
                        nameof(element));
                builder.Append(FormatNumber(number));
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem)
                        builder.Append(',');
                    firstItem = false;
                    AppendElement(builder, item);
                }
                builder.Append(']');
                break;
            case JsonValueKind.Object:
            {
                var properties = element.EnumerateObject().ToArray();
                var unique = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in properties)
                {
                    if (!unique.Add(property.Name))
                        throw new ArgumentException(
                            $"JCS cannot canonicalize duplicate property '{property.Name}'.",
                            nameof(element));
                }
                Array.Sort(properties,
                    static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
                builder.Append('{');
                for (var i = 0; i < properties.Length; i++)
                {
                    if (i > 0)
                        builder.Append(',');
                    AppendString(builder, properties[i].Name);
                    builder.Append(':');
                    AppendElement(builder, properties[i].Value);
                }
                builder.Append('}');
                break;
            }
            default:
                throw new ArgumentException("Unsupported JSON token for JCS.", nameof(element));
        }
    }

    private static void AppendValue(StringBuilder builder, object? value)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                return;
            case JsonElement element:
                AppendElement(builder, element);
                return;
            case string text:
                AppendString(builder, text);
                return;
            case bool boolean:
                builder.Append(boolean ? "true" : "false");
                return;
            case byte number:
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                return;
            case sbyte number:
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                return;
            case short number:
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                return;
            case ushort number:
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                return;
            case int number:
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                return;
            case uint number:
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                return;
            case long number:
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                return;
            case ulong number:
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                return;
            case float number:
                builder.Append(FormatNumber(number));
                return;
            case double number:
                builder.Append(FormatNumber(number));
                return;
            case decimal:
                throw new ArgumentException(
                    "JCS numbers are IEEE 754 binary64; convert decimal explicitly to double.",
                    nameof(value));
            case IReadOnlyDictionary<string, object?> dictionary:
                AppendDictionary(builder, dictionary);
                return;
            case IDictionary dictionary:
            {
                var converted = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string key)
                        throw new ArgumentException("JCS object keys must be strings.", nameof(value));
                    if (!converted.TryAdd(key, entry.Value))
                        throw new ArgumentException($"Duplicate JCS object key '{key}'.", nameof(value));
                }
                AppendDictionary(builder, converted);
                return;
            }
            case IEnumerable enumerable:
                builder.Append('[');
                var first = true;
                foreach (var item in enumerable)
                {
                    if (!first)
                        builder.Append(',');
                    first = false;
                    AppendValue(builder, item);
                }
                builder.Append(']');
                return;
            default:
                throw new ArgumentException(
                    $"Type '{value.GetType().FullName}' is not a supported JSON value.", nameof(value));
        }
    }

    private static void AppendDictionary(
        StringBuilder builder, IReadOnlyDictionary<string, object?> dictionary)
    {
        var keys = dictionary.Keys.ToArray();
        Array.Sort(keys, StringComparer.Ordinal);
        builder.Append('{');
        for (var i = 0; i < keys.Length; i++)
        {
            if (i > 0)
                builder.Append(',');
            AppendString(builder, keys[i]);
            builder.Append(':');
            AppendValue(builder, dictionary[keys[i]]);
        }
        builder.Append('}');
    }

    private static void AppendString(StringBuilder builder, string value)
    {
        ValidateSurrogates(value);
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\t': builder.Append("\\t"); break;
                case '\n': builder.Append("\\n"); break;
                case '\f': builder.Append("\\f"); break;
                case '\r': builder.Append("\\r"); break;
                default:
                    if (c < 0x20)
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }

    private static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value),
                "JCS requires finite IEEE 754 binary64 numbers.");
        if (value == 0.0)
            return "0";

        var negative = value < 0.0;
        var shortest = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponentMarker = shortest.IndexOfAny(['E', 'e']);
        var mantissa = exponentMarker >= 0 ? shortest[..exponentMarker] : shortest;
        var sourceExponent = exponentMarker >= 0
            ? int.Parse(shortest[(exponentMarker + 1)..],
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
            : 0;
        var decimalPoint = mantissa.IndexOf('.');
        var decimalPosition = decimalPoint >= 0 ? decimalPoint : mantissa.Length;
        var digits = decimalPoint >= 0 ? mantissa.Remove(decimalPoint, 1) : mantissa;

        // ToString("R") already removes dispensable trailing digits. Leading zeroes only occur in
        // fixed values smaller than one and do not belong to the shortest significand.
        var firstNonZero = 0;
        while (firstNonZero < digits.Length && digits[firstNonZero] == '0')
            firstNonZero++;
        decimalPosition -= firstNonZero;
        digits = digits[firstNonZero..];
        var scientificExponent = checked(decimalPosition + sourceExponent - 1);

        string result;
        if (scientificExponent is >= -6 and < 21)
        {
            var fixedPosition = scientificExponent + 1;
            if (fixedPosition <= 0)
                result = "0." + new string('0', -fixedPosition) + digits;
            else if (fixedPosition >= digits.Length)
                result = digits + new string('0', fixedPosition - digits.Length);
            else
                result = digits.Insert(fixedPosition, ".");
        }
        else
        {
            var significand = digits.Length == 1
                ? digits
                : digits[0] + "." + digits[1..];
            result = significand + "e" +
                (scientificExponent >= 0 ? "+" : string.Empty) +
                scientificExponent.ToString(CultureInfo.InvariantCulture);
        }
        return negative ? "-" + result : result;
    }

    private static void ValidateSurrogates(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i]))
                continue;
            if (!char.IsHighSurrogate(value[i]) || i + 1 >= value.Length ||
                !char.IsLowSurrogate(value[i + 1]))
                throw new ArgumentException("JCS strings must not contain unpaired surrogates.",
                    nameof(value));
            i++;
        }
    }
}

/// <summary>Computed Gameplay Digest v1 material.</summary>
public sealed record V2GameplayDigestResult(
    IReadOnlyDictionary<string, object?> Projection,
    string AudioSha256,
    string CanonicalJson,
    string Sha256)
{
    public string Algorithm => V2Format.GameplayDigestAlgorithm;
    public string RulesetId => V2Format.RulesetId;
}

/// <summary>Gameplay-v1 projection, canonicalization, and SHA-256 implementation.</summary>
public static class V2GameplayDigest
{
    public static V2GameplayDigestResult Compute(
        V2Chart chart, V2ChartEntry entry, ReadOnlySpan<byte> resolvedAudioBytes) =>
        Compute(chart, entry.JudgePreset, Sha256Hex(resolvedAudioBytes));

    public static V2GameplayDigestResult Compute(
        V2Chart chart, Judge.JudgePreset judgePreset, string resolvedAudioSha256)
    {
        V2SemanticValidator.ValidateChart(chart);
        if (resolvedAudioSha256.Length != 64 ||
            resolvedAudioSha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException(
                "Resolved audio SHA-256 must be 64 lowercase hexadecimal characters.",
                nameof(resolvedAudioSha256));
        if (judgePreset == Judge.JudgePreset.Tutorial)
            throw new ArgumentException("Tutorial is not a v2 JudgePreset.", nameof(judgePreset));

        var projection = BuildProjection(chart, judgePreset, resolvedAudioSha256);
        var canonical = JsonCanonicalizer.Canonicalize(projection);
        var digest = Sha256Hex(Encoding.UTF8.GetBytes(canonical));
        return new V2GameplayDigestResult(projection, resolvedAudioSha256, canonical, digest);
    }

    public static IReadOnlyDictionary<string, object?> BuildProjection(
        V2Chart chart, Judge.JudgePreset judgePreset, string resolvedAudioSha256)
    {
        var left = ProjectAndSortNotes(chart.NotesLeft);
        var center = ProjectAndSortNotes(chart.NotesCenter);
        var right = ProjectAndSortNotes(chart.NotesRight);
        var scrolls = chart.ScrollSpeeds.Count == 0
            ? new object?[]
            {
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["time"] = ProjectTime(ExactBarTime.Zero),
                    ["value"] = 1.0,
                },
            }
            : chart.ScrollSpeeds.Select((scroll, index) =>
            {
                var item = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["time"] = ProjectTime(scroll.Time),
                    ["value"] = NormalizeZero(scroll.Value),
                };
                if (index < chart.ScrollSpeeds.Count - 1)
                    item["curveToNext"] = ScrollCurveName(
                        scroll.CurveToNext ?? V2ScrollCurve.Linear);
                return (object?)item;
            }).ToArray();

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["rulesetId"] = V2Format.RulesetId,
            ["judgePreset"] = JudgePresetName(judgePreset),
            ["audio"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sha256"] = resolvedAudioSha256,
            },
            ["timing"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["audioOffsetSec"] = NormalizeZero(chart.AudioOffsetSec),
                ["bpms"] = chart.Bpms.Select(bpm => (object?)
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["time"] = ProjectTime(bpm.Time),
                        ["bpm"] = NormalizeZero(bpm.Bpm),
                    }).ToArray(),
            },
            ["scrollSpeeds"] = scrolls,
            ["notes"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["left"] = left,
                ["center"] = center,
                ["right"] = right,
            },
        };
    }

    public static string Sha256Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static object?[] ProjectAndSortNotes(IReadOnlyList<V2Note> notes)
    {
        var projected = notes.Select(note => new ProjectedNote(
            note.Time, TypeOrder(note.Type), ProjectNote(note))).ToArray();
        Array.Sort(projected, static (left, right) =>
        {
            var comparison = left.Time.CompareTo(right.Time);
            if (comparison != 0)
                return comparison;
            comparison = left.TypeOrder.CompareTo(right.TypeOrder);
            if (comparison != 0)
                return comparison;
            return CompareBytes(left.CanonicalUtf8, right.CanonicalUtf8);
        });
        return projected.Select(item => (object?)item.Value).ToArray();
    }

    private static IReadOnlyDictionary<string, object?> ProjectNote(V2Note note)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = NoteTypeName(note.Type),
            ["time"] = ProjectTime(note.Time),
            ["center"] = NormalizeZero(note.Center),
            ["width"] = NormalizeZero(note.Width),
        };
        if (note is not V2PathNote path)
            return result;

        result["curveToNext"] = PathCurveName(path.CurveToNext ?? V2PathCurve.Linear);
        result["nodes"] = path.Nodes.Select((node, index) =>
        {
            var projected = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["time"] = ProjectTime(node.Time),
                ["center"] = NormalizeZero(node.Center),
                ["width"] = NormalizeZero(node.Width),
            };
            if (index < path.Nodes.Count - 1)
                projected["curveToNext"] = PathCurveName(
                    node.CurveToNext ?? V2PathCurve.Linear);
            if (path.Type == V2NoteType.Hold)
                projected["judge"] = node.Judge == true;
            return (object?)projected;
        }).ToArray();
        return result;
    }

    private static IReadOnlyDictionary<string, object?> ProjectTime(ExactBarTime time) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["bar"] = (long)time.Bar,
            ["numerator"] = (long)time.Numerator,
            ["denominator"] = (long)time.Denominator,
        };

    private static double NormalizeZero(double value) => value == 0.0 ? 0.0 : value;

    private static int TypeOrder(V2NoteType type) => type switch
    {
        V2NoteType.Tap => 0,
        V2NoteType.Drag => 1,
        V2NoteType.ExTap => 2,
        V2NoteType.Hold => 3,
        V2NoteType.Mixer => 4,
        V2NoteType.Mine => 5,
        V2NoteType.BarLine => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    internal static string NoteTypeName(V2NoteType type) => type switch
    {
        V2NoteType.Tap => "tap",
        V2NoteType.Drag => "drag",
        V2NoteType.ExTap => "exTap",
        V2NoteType.Hold => "hold",
        V2NoteType.Mixer => "mixer",
        V2NoteType.Mine => "mine",
        V2NoteType.BarLine => "barLine",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    internal static string PathCurveName(V2PathCurve curve) => curve switch
    {
        V2PathCurve.Linear => "linear",
        V2PathCurve.Hold => "hold",
        V2PathCurve.EaseInQuad => "easeInQuad",
        V2PathCurve.EaseOutQuad => "easeOutQuad",
        V2PathCurve.EaseInOutCubic => "easeInOutCubic",
        V2PathCurve.Smooth => "smooth",
        _ => throw new ArgumentOutOfRangeException(nameof(curve)),
    };

    internal static string ScrollCurveName(V2ScrollCurve curve) => curve switch
    {
        V2ScrollCurve.Linear => "linear",
        V2ScrollCurve.Hold => "hold",
        V2ScrollCurve.EaseInQuad => "easeInQuad",
        V2ScrollCurve.EaseOutQuad => "easeOutQuad",
        V2ScrollCurve.EaseInOutCubic => "easeInOutCubic",
        _ => throw new ArgumentOutOfRangeException(nameof(curve)),
    };

    private static string JudgePresetName(Judge.JudgePreset preset) => preset switch
    {
        Judge.JudgePreset.Casual => "casual",
        Judge.JudgePreset.Normal => "normal",
        Judge.JudgePreset.Hard => "hard",
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    private static int CompareBytes(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var common = Math.Min(left.Length, right.Length);
        for (var i = 0; i < common; i++)
        {
            var comparison = left[i].CompareTo(right[i]);
            if (comparison != 0)
                return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }

    private sealed record ProjectedNote(
        ExactBarTime Time, int TypeOrder, IReadOnlyDictionary<string, object?> Value)
    {
        public byte[] CanonicalUtf8 { get; } = JsonCanonicalizer.CanonicalizeUtf8(Value);
    }
}
