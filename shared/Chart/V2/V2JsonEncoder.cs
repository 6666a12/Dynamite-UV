using System.Text;
using System.Text.Json;

namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>
/// Deterministic writer for the frozen v2 package and chart contracts. It deliberately maps the
/// semantic records to disk fields rather than relying on reflection-based serialization.
/// </summary>
public static class V2JsonEncoder
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = false,
    };

    public static byte[] EncodePack(V2Pack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        V2SemanticValidator.ValidatePack(pack);
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("format", V2Format.PackFormat);
            writer.WriteNumber("formatVersion", V2Format.FormatVersion);
            writer.WriteString("id", pack.Id);
            writer.WriteNumber("revision", pack.Revision);
            writer.WriteString("title", pack.Title);
            writer.WriteString("artist", pack.Artist);
            if (pack.Audio is not null)
                writer.WriteString("audio", pack.Audio);
            if (pack.Cover is not null)
                writer.WriteString("cover", pack.Cover);
            if (pack.Preview is not null)
            {
                writer.WritePropertyName("preview");
                WritePreview(writer, pack.Preview);
            }

            writer.WritePropertyName("charts");
            writer.WriteStartArray();
            foreach (var entry in pack.Charts)
                WriteChartEntry(writer, entry);
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    public static byte[] EncodeChart(V2Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        V2SemanticValidator.ValidateChart(chart);
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("format", V2Format.ChartFormat);
            writer.WriteNumber("formatVersion", V2Format.FormatVersion);
            writer.WriteString("chartId", chart.ChartId);
            writer.WriteNumber("audioOffsetSec", CanonicalZero(chart.AudioOffsetSec));
            writer.WritePropertyName("timing");
            writer.WriteStartObject();
            writer.WritePropertyName("bpms");
            writer.WriteStartArray();
            foreach (var bpm in chart.Bpms)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("time");
                WriteTime(writer, bpm.Time);
                writer.WriteNumber("bpm", CanonicalZero(bpm.Bpm));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();

            if (chart.ScrollSpeeds.Count > 0)
            {
                writer.WritePropertyName("scrollSpeeds");
                writer.WriteStartArray();
                for (var index = 0; index < chart.ScrollSpeeds.Count; index++)
                {
                    var scroll = chart.ScrollSpeeds[index];
                    writer.WriteStartObject();
                    writer.WritePropertyName("time");
                    WriteTime(writer, scroll.Time);
                    writer.WriteNumber("value", CanonicalZero(scroll.Value));
                    if (index + 1 < chart.ScrollSpeeds.Count)
                        writer.WriteString("curveToNext", ScrollCurveName(scroll.CurveToNext ?? V2ScrollCurve.Linear));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            WriteTrack(writer, "notesLeft", chart.NotesLeft);
            WriteTrack(writer, "notesCenter", chart.NotesCenter);
            WriteTrack(writer, "notesRight", chart.NotesRight);
            writer.WriteEndObject();
        });
    }

    private static byte[] Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
            write(writer);
        var body = stream.ToArray();
        var result = new byte[body.Length + 1];
        body.CopyTo(result, 0);
        result[^1] = (byte)'\n';
        return result;
    }

    private static void WritePreview(Utf8JsonWriter writer, V2Preview preview)
    {
        writer.WriteStartObject();
        writer.WriteNumber("startSec", CanonicalZero(preview.StartSec));
        writer.WriteNumber("durationSec", CanonicalZero(preview.DurationSec));
        writer.WriteEndObject();
    }

    private static void WriteChartEntry(Utf8JsonWriter writer, V2ChartEntry entry)
    {
        writer.WriteStartObject();
        writer.WriteString("id", entry.Id);
        writer.WriteString("difficulty", DifficultyName(entry.Difficulty));
        if (entry.DifficultyKey is not null)
            writer.WriteString("difficultyKey", entry.DifficultyKey);
        if (entry.Unrated)
            writer.WriteBoolean("unrated", true);
        else
            writer.WriteNumber("level", entry.Level!.Value);
        writer.WritePropertyName("charters");
        writer.WriteStartArray();
        foreach (var charter in entry.Charters)
            writer.WriteStringValue(charter);
        writer.WriteEndArray();
        writer.WriteString("file", entry.File);
        if (entry.Audio is not null)
            writer.WriteString("audio", entry.Audio);
        if (entry.Preview is not null)
        {
            writer.WritePropertyName("preview");
            WritePreview(writer, entry.Preview);
        }
        writer.WriteEndObject();
    }

    private static void WriteTrack(Utf8JsonWriter writer, string name, IReadOnlyList<V2Note> source)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var note in source.OrderBy(note => note.Time).ThenBy(note => note.Id, StringComparer.Ordinal))
            WriteNote(writer, note);
        writer.WriteEndArray();
    }

    private static void WriteNote(Utf8JsonWriter writer, V2Note note)
    {
        writer.WriteStartObject();
        writer.WriteString("id", note.Id);
        writer.WriteString("type", V2GameplayDigest.NoteTypeName(note.Type));
        writer.WritePropertyName("time");
        WriteTime(writer, note.Time);
        writer.WriteNumber("center", CanonicalZero(note.Center));
        writer.WriteNumber("width", CanonicalZero(note.Width));
        if (note is V2PathNote path)
        {
            if (path.Nodes.Count == 0)
                throw new InvalidOperationException($"Path note '{path.Id}' requires nodes.");
            writer.WriteString("curveToNext", PathCurveName(path.CurveToNext ?? V2PathCurve.Linear));
            writer.WritePropertyName("nodes");
            writer.WriteStartArray();
            for (var index = 0; index < path.Nodes.Count; index++)
                WritePathNode(writer, path.Nodes[index], path.Type, index + 1 == path.Nodes.Count);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    private static void WritePathNode(Utf8JsonWriter writer, V2PathNode node, V2NoteType type, bool isTail)
    {
        writer.WriteStartObject();
        writer.WriteString("id", node.Id);
        writer.WritePropertyName("time");
        WriteTime(writer, node.Time);
        writer.WriteNumber("center", CanonicalZero(node.Center));
        writer.WriteNumber("width", CanonicalZero(node.Width));
        if (!isTail)
            writer.WriteString("curveToNext", PathCurveName(node.CurveToNext ?? V2PathCurve.Linear));
        if (type == V2NoteType.Hold)
        {
            if (node.Judge is null)
                throw new InvalidOperationException($"Hold path node '{node.Id}' must resolve judge.");
            writer.WriteBoolean("judge", isTail ? true : node.Judge.Value);
        }
        writer.WriteEndObject();
    }

    private static void WriteTime(Utf8JsonWriter writer, ExactBarTime time)
    {
        if (!time.IsJsonSafeCanonical)
            throw new InvalidOperationException("v2 output requires JSON-safe canonical BarTime.");
        writer.WriteStartObject();
        writer.WriteNumber("bar", (long)time.Bar);
        writer.WriteNumber("numerator", (long)time.Numerator);
        writer.WriteNumber("denominator", (long)time.Denominator);
        writer.WriteEndObject();
    }

    private static double CanonicalZero(double value) => value == 0.0 ? 0.0 : value;

    private static string DifficultyName(V2Difficulty difficulty) => difficulty switch
    {
        V2Difficulty.Casual => "casual",
        V2Difficulty.Normal => "normal",
        V2Difficulty.Hard => "hard",
        V2Difficulty.Mega => "mega",
        V2Difficulty.Giga => "giga",
        V2Difficulty.Tech => "tech",
        V2Difficulty.Custom => "custom",
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };

    private static string PathCurveName(V2PathCurve curve) => curve switch
    {
        V2PathCurve.Linear => "linear",
        V2PathCurve.Hold => "hold",
        V2PathCurve.EaseInQuad => "easeInQuad",
        V2PathCurve.EaseOutQuad => "easeOutQuad",
        V2PathCurve.EaseInOutCubic => "easeInOutCubic",
        V2PathCurve.Smooth => "smooth",
        _ => throw new ArgumentOutOfRangeException(nameof(curve)),
    };

    private static string ScrollCurveName(V2ScrollCurve curve) => curve switch
    {
        V2ScrollCurve.Linear => "linear",
        V2ScrollCurve.Hold => "hold",
        V2ScrollCurve.EaseInQuad => "easeInQuad",
        V2ScrollCurve.EaseOutQuad => "easeOutQuad",
        V2ScrollCurve.EaseInOutCubic => "easeInOutCubic",
        _ => throw new ArgumentOutOfRangeException(nameof(curve)),
    };
}
