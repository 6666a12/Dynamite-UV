using System.Globalization;
using System.Text;
using System.Text.Json;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public sealed record ImportedBpmPoint(ExactBarTime Time, double Bpm, double? SourceMilliseconds,
    double SnapMilliseconds);

public sealed record ImportedTiming(IReadOnlyList<ImportedBpmPoint> Points, double? AudioOffsetSec,
    string? AudioFileName, int SnappedCount, double MaximumSnapMilliseconds, bool HasNonFourMeter,
    bool NeedsManualOffset);

/// <summary>Reads timing only. Leaves the caller's stream open and never loads referenced audio.</summary>
public static class TimingFileImport
{
    public const int MaxFileBytes = 16 * 1024 * 1024;
    private const int MaxPoints = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static ImportedTiming Read(Stream input, string extension, bool snapOsu = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(extension);
        string format = extension.Trim().ToLowerInvariant();
        if (format is not (".mc" or "mc" or ".osu" or "osu"))
            throw new InvalidDataException("Choose a Malody .mc or osu! .osu file.");
        var bytes = ReadBounded(input);
        try
        {
            return format is ".mc" or "mc" ? ReadMalody(bytes) : ReadOsu(StrictUtf8.GetString(bytes.Span), snapOsu);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            throw new InvalidDataException("The chart is not valid UTF-8 timing data.", ex);
        }
    }

    private static ReadOnlyMemory<byte> ReadBounded(Stream input)
    {
        using var copy = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            int count = input.Read(chunk, 0, (int)Math.Min(chunk.Length, MaxFileBytes + 1 - copy.Length));
            if (count == 0) break;
            if (copy.Length + count > MaxFileBytes) throw new InvalidDataException("The timing file exceeds 16 MiB.");
            copy.Write(chunk, 0, count);
        }
        var bytes = copy.GetBuffer().AsMemory(0, (int)copy.Length);
        return bytes.Span.StartsWith("\uFEFF"u8) ? bytes[3..] : bytes;
    }

    private static ImportedTiming ReadMalody(ReadOnlyMemory<byte> bytes)
    {
        _ = StrictUtf8.GetCharCount(bytes.Span);
        using var json = JsonDocument.Parse(bytes);
        var chart = json.RootElement;
        if (!TryProperty(chart, "time", out var timing) || timing.ValueKind != JsonValueKind.Array ||
            timing.GetArrayLength() is < 1 or > MaxPoints)
            throw new InvalidDataException($"Malody time must contain 1 to {MaxPoints} BPM points.");
        var points = new List<ImportedBpmPoint>(timing.GetArrayLength());
        foreach (var row in timing.EnumerateArray())
        {
            if (!TryProperty(row, "beat", out var beat) || !TryBeat(beat, out var time) || !time.IsJsonSafeCanonical ||
                !TryNumber(row, "bpm", out double bpm) || !IsUsableBpm(bpm))
                throw new InvalidDataException($"Invalid Malody BPM point {points.Count + 1}.");
            points.Add(new(time, bpm, null, 0));
        }
        points.Sort((left, right) => left.Time.CompareTo(right.Time));
        if (points[0].Time != ExactBarTime.Zero)
            throw new InvalidDataException("Malody timing requires a BPM point at beat 0.");
        var (offset, filename) = FindMalodyAudio(chart);
        double elapsed = offset ?? 0;
        for (int i = 1; i < points.Count; i++)
        {
            var previous = points[i - 1];
            var current = points[i];
            if (current.Time == previous.Time)
                throw new InvalidDataException($"Duplicate Malody BPM point at UV bar {current.Time}.");
            var next = elapsed + (current.Time - previous.Time).ToDouble() * (240.0 / previous.Bpm);
            if (current.Time.ToDouble() <= previous.Time.ToDouble() || next <= elapsed || !double.IsFinite(next))
                throw new InvalidDataException("Malody BPM points do not form a representable playback timeline.");
            elapsed = next;
        }
        return new(points.AsReadOnly(), offset, filename, 0, 0, false, offset is null);
    }

    private static (double? Offset, string? Filename) FindMalodyAudio(JsonElement chart)
    {
        if (!TryProperty(chart, "note", out var notes) || notes.ValueKind != JsonValueKind.Array) return (null, null);
        JsonElement bgm = default;
        foreach (var note in notes.EnumerateArray())
        {
            if (!TryProperty(note, "type", out var type) || type.ValueKind != JsonValueKind.Number ||
                !type.TryGetInt32(out int value) || value != 1) continue;
            if (bgm.ValueKind != JsonValueKind.Undefined) return (null, null);
            bgm = note;
        }
        string? file = TryProperty(bgm, "sound", out var sound) && sound.ValueKind == JsonValueKind.String
            ? AudioHint(sound.GetString()) : null;
        if (!TryProperty(bgm, "beat", out var beat) || !TryBeat(beat, out var time) || !time.IsZero ||
            !TryProperty(bgm, "offset", out var offset) || offset.ValueKind != JsonValueKind.Number ||
            !ChartOffsetUnits.TryParseMilliseconds(offset.GetRawText(), out double seconds)) return (null, file);
        return (seconds == 0 ? 0 : -seconds, file);
    }

    private static bool TryBeat(JsonElement beat, out ExactBarTime time)
    {
        time = ExactBarTime.Zero;
        if (beat.ValueKind != JsonValueKind.Array || beat.GetArrayLength() != 3 ||
            !TryInteger(beat[0], out long whole) || !TryInteger(beat[1], out long numerator) ||
            !TryInteger(beat[2], out long denominator) || denominator == 0) return false;
        // Malody indexes beats rather than measures. Normalize even improper or signed fractions.
        time = new ExactBarTime(whole, numerator, denominator) / 4;
        return true;
    }

    private static bool TryInteger(JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    private static bool TryNumber(JsonElement element, string key, out double value)
    {
        value = 0;
        return TryProperty(element, key, out var child) && child.ValueKind == JsonValueKind.Number && child.TryGetDouble(out value);
    }

    private static bool TryProperty(JsonElement element, string key, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out value);
    }

    private static ImportedTiming ReadOsu(string text, bool snap)
    {
        using var reader = new StringReader(text);
        // Earlier osu! versions omit trailing timing columns. v3/v4 also need osu!stable's
        // historical client-level timing correction before their first red line is selected.
        const string prefix = "osu file format v";
        var header = reader.ReadLine()?.Trim();
        if (header is null || !header.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(header.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int version) ||
            (version is < 3 or > 14 && version != 128))
            throw new InvalidDataException("Supported osu! file formats are v3 through v14 and v128.");
        bool legacy = version < 14;
        var legacyOffset = ExactBarTime.FromFraction(version <= 4 ? 24 : 0, 1);
        var reds = new List<OsuTimingImport.RedPoint>();
        string section = "";
        string? filename = null;
        int lineNumber = 1;
        while (reader.ReadLine() is { } raw)
        {
            lineNumber++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line; continue; }
            if (section == "[General]" && line.StartsWith("AudioFilename:", StringComparison.Ordinal))
                filename = AudioHint(line[14..]);
            if (section != "[TimingPoints]") continue;
            var fields = line.Split(',', 8);
            if (fields.Length < 2 || (!legacy && fields.Length < 7))
                throw new InvalidDataException($"Invalid osu! timing point on line {lineNumber}.");
            if (fields.Length >= 7)
            {
                var flag = fields[6].Trim();
                if (flag is not ("0" or "1"))
                    throw new InvalidDataException($"Invalid osu! timing point flag on line {lineNumber}.");
                if (flag == "0") continue;
            }
            if (!OsuTimingImport.TryDecimal(fields[0].AsSpan().Trim(), out var time) ||
                !OsuTimingImport.TryDecimal(fields[1].AsSpan().Trim(), out var length) ||
                !length.IsNonNegative || length.IsZero)
                throw new InvalidDataException($"Invalid osu! timing point on line {lineNumber}.");

            int meter = 4;
            if (fields.Length >= 3)
            {
                if (!int.TryParse(fields[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out meter) ||
                    meter < 0 || (meter == 0 && !legacy))
                    throw new InvalidDataException($"Invalid osu! meter on line {lineNumber}.");
                if (meter == 0) meter = 4;
            }
            if (reds.Count == MaxPoints) throw new InvalidDataException($"Too many osu! red lines (limit {MaxPoints}).");
            if (!legacyOffset.IsZero) time += legacyOffset;
            double bpm = (ExactBarTime.FromFraction(60000, 1) / length).ToDouble();
            if (!IsUsableBpm(bpm)) throw new InvalidDataException($"Invalid osu! BPM on line {lineNumber}.");
            reds.Add(new(time, bpm, meter, lineNumber));
        }
        return OsuTimingImport.Build(reds, filename, snap);
    }

    private static bool IsUsableBpm(double bpm) => bpm > 0 && double.IsFinite(bpm) && double.IsFinite(240.0 / bpm);

    private static string? AudioHint(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > 1024 || trimmed.Any(char.IsControl) ? null : trimmed;
    }
}
