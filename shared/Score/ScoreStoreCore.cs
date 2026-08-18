using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamiteUniverse.Shared.Score;

/// <summary>Complete gameplay score identity; members are compared independently.</summary>
public readonly struct ScoreIdentity : IEquatable<ScoreIdentity>
{
    [JsonPropertyName("packId")]
    [JsonPropertyOrder(0)]
    public string PackId { get; }

    [JsonPropertyName("chartId")]
    [JsonPropertyOrder(1)]
    public string ChartId { get; }

    [JsonPropertyName("rulesetId")]
    [JsonPropertyOrder(2)]
    public string RulesetId { get; }

    [JsonPropertyName("gameplayDigest")]
    [JsonPropertyOrder(3)]
    public string GameplayDigest { get; }

    [JsonConstructor]
    public ScoreIdentity(string packId, string chartId, string rulesetId, string gameplayDigest)
    {
        PackId = RequirePart(packId, nameof(packId));
        ChartId = RequirePart(chartId, nameof(chartId));
        RulesetId = RequirePart(rulesetId, nameof(rulesetId));
        GameplayDigest = RequirePart(gameplayDigest, nameof(gameplayDigest));
    }

    public bool Equals(ScoreIdentity other) =>
        StringComparer.Ordinal.Equals(PackId, other.PackId) &&
        StringComparer.Ordinal.Equals(ChartId, other.ChartId) &&
        StringComparer.Ordinal.Equals(RulesetId, other.RulesetId) &&
        StringComparer.Ordinal.Equals(GameplayDigest, other.GameplayDigest);

    public override bool Equals(object? obj) => obj is ScoreIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PackId, StringComparer.Ordinal);
        hash.Add(ChartId, StringComparer.Ordinal);
        hash.Add(RulesetId, StringComparer.Ordinal);
        hash.Add(GameplayDigest, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public static bool operator ==(ScoreIdentity left, ScoreIdentity right) => left.Equals(right);
    public static bool operator !=(ScoreIdentity left, ScoreIdentity right) => !left.Equals(right);

    public bool IsValid =>
        !string.IsNullOrEmpty(PackId) &&
        !string.IsNullOrEmpty(ChartId) &&
        !string.IsNullOrEmpty(RulesetId) &&
        !string.IsNullOrEmpty(GameplayDigest);

    private static string RequirePart(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Score identity members must not be empty.", parameterName);
        return value;
    }
}

/// <summary>A chart's local best result.</summary>
public sealed class ScoreRecord
{
    [JsonPropertyName("score")]
    [JsonPropertyOrder(0)]
    public int Score { get; set; }

    [JsonPropertyName("acc")]
    [JsonPropertyOrder(1)]
    public double Acc { get; set; }

    [JsonPropertyName("maxCombo")]
    [JsonPropertyOrder(2)]
    public int MaxCombo { get; set; }

    [JsonPropertyName("grade")]
    [JsonPropertyOrder(3)]
    public string Grade { get; set; } = "C";

    [JsonPropertyName("perfect")]
    [JsonPropertyOrder(4)]
    public int Perfect { get; set; }

    [JsonPropertyName("great")]
    [JsonPropertyOrder(5)]
    public int Great { get; set; }

    [JsonPropertyName("good")]
    [JsonPropertyOrder(6)]
    public int Good { get; set; }

    [JsonPropertyName("miss")]
    [JsonPropertyOrder(7)]
    public int Miss { get; set; }

    [JsonPropertyName("date")]
    [JsonPropertyOrder(8)]
    public string Date { get; set; } = "";
}

/// <summary>
/// Godot-independent score codec and record book. Legacy packId:diff records remain historical and
/// never answer current four-part identity queries.
/// </summary>
public sealed class ScoreStoreCore
{
    public const string StoreFormat = "dynamite-uv-score-store";
    public const int StoreFormatVersion = 2;

    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true,
    };

    private readonly Dictionary<ScoreIdentity, ScoreRecord> _records = new();
    private readonly Dictionary<string, ScoreRecord> _legacyRecords = new(StringComparer.Ordinal);

    public static string KeyOf(string packId, string diff) => $"{packId}:{diff}";

    public static string GradeOf(double clearPercent) => clearPercent switch
    {
        >= 98.0 => "Ω",
        >= 95.0 => "S",
        >= 90.0 => "A",
        >= 80.0 => "B",
        _ => "C",
    };

    public ScoreRecord? Get(ScoreIdentity identity) =>
        _records.TryGetValue(identity, out var record) ? record : null;

    public bool TryUpdate(ScoreIdentity identity, ScoreRecord candidate,
        DateTime? acceptedAt = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!identity.IsValid)
            throw new ArgumentException("Score identity members must not be empty.", nameof(identity));
        if (_records.TryGetValue(identity, out var old) && old.Score >= candidate.Score)
            return false;

        PrepareAcceptedRecord(candidate, acceptedAt ?? DateTime.Now);
        _records[identity] = candidate;
        return true;
    }

    public ScoreRecord? GetLegacy(string packId, string diff) =>
        _legacyRecords.TryGetValue(KeyOf(packId, diff), out var record) ? record : null;

    public bool TryUpdateLegacy(string packId, string diff, ScoreRecord candidate,
        DateTime? acceptedAt = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var key = KeyOf(packId, diff);
        if (_legacyRecords.TryGetValue(key, out var old) && old.Score >= candidate.Score)
            return false;

        PrepareAcceptedRecord(candidate, acceptedAt ?? DateTime.Now);
        _legacyRecords[key] = candidate;
        return true;
    }

    public void LoadJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Score store root must be an object.");

        var records = new Dictionary<ScoreIdentity, ScoreRecord>();
        var legacyRecords = new Dictionary<string, ScoreRecord>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("format", out _))
            DecodeVersioned(json, records, legacyRecords);
        else
            DecodeLegacy(json, legacyRecords);

        Clear();
        foreach (var (identity, result) in records)
            _records[identity] = result;
        foreach (var (key, result) in legacyRecords)
            _legacyRecords[key] = result;
    }

    public string SaveJson()
    {
        var records = _records
            .OrderBy(pair => pair.Key.PackId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.ChartId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.RulesetId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.GameplayDigest, StringComparer.Ordinal)
            .Select(pair => new StoredRecord
            {
                Identity = pair.Key,
                Result = pair.Value,
            })
            .ToList();

        var legacyRecords = new Dictionary<string, ScoreRecord?>(StringComparer.Ordinal);
        foreach (var (key, result) in _legacyRecords.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            legacyRecords[key] = result;

        return JsonSerializer.Serialize(new StoreData
        {
            Format = StoreFormat,
            FormatVersion = StoreFormatVersion,
            Records = records,
            LegacyRecords = legacyRecords,
        }, SaveOptions);
    }

    public void Clear()
    {
        _records.Clear();
        _legacyRecords.Clear();
    }

    private static void DecodeVersioned(string json,
        Dictionary<ScoreIdentity, ScoreRecord> records,
        Dictionary<string, ScoreRecord> legacyRecords)
    {
        var data = JsonSerializer.Deserialize<StoreData>(json)
            ?? throw new JsonException("Score store is empty.");
        if (!StringComparer.Ordinal.Equals(data.Format, StoreFormat) ||
            data.FormatVersion != StoreFormatVersion)
        {
            throw new JsonException(
                $"Unsupported score store format/version: {data.Format ?? "<null>"} " +
                $"v{data.FormatVersion}.");
        }
        if (data.Records == null || data.LegacyRecords == null)
            throw new JsonException("v2 score store requires records and legacyRecords.");

        foreach (var stored in data.Records)
        {
            if (stored?.Identity is not ScoreIdentity identity || !identity.IsValid ||
                stored.Result == null)
                throw new JsonException("records contains an incomplete identity or result.");
            if (!records.TryGetValue(identity, out var old) || PreferDuplicate(stored.Result, old))
                records[identity] = stored.Result;
        }

        foreach (var (key, result) in data.LegacyRecords)
        {
            if (result == null)
                throw new JsonException($"legacyRecords[{key}] must not be null.");
            legacyRecords[key] = result;
        }
    }

    private static void DecodeLegacy(string json,
        Dictionary<string, ScoreRecord> legacyRecords)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, ScoreRecord?>>(json)
            ?? throw new JsonException("Legacy score store is empty.");
        foreach (var (key, result) in data)
        {
            if (result == null)
                throw new JsonException($"Legacy score[{key}] must not be null.");
            legacyRecords[key] = result;
        }
    }

    private static bool PreferDuplicate(ScoreRecord candidate, ScoreRecord current)
    {
        var comparison = candidate.Score.CompareTo(current.Score);
        if (comparison != 0)
            return comparison > 0;
        comparison = candidate.Acc.CompareTo(current.Acc);
        if (comparison != 0)
            return comparison > 0;
        comparison = candidate.MaxCombo.CompareTo(current.MaxCombo);
        if (comparison != 0)
            return comparison > 0;
        comparison = StringComparer.Ordinal.Compare(candidate.Grade ?? "", current.Grade ?? "");
        if (comparison != 0)
            return comparison > 0;
        comparison = candidate.Perfect.CompareTo(current.Perfect);
        if (comparison != 0)
            return comparison > 0;
        comparison = candidate.Great.CompareTo(current.Great);
        if (comparison != 0)
            return comparison > 0;
        comparison = candidate.Good.CompareTo(current.Good);
        if (comparison != 0)
            return comparison > 0;
        comparison = candidate.Miss.CompareTo(current.Miss);
        if (comparison != 0)
            return comparison > 0;
        return StringComparer.Ordinal.Compare(candidate.Date ?? "", current.Date ?? "") > 0;
    }

    private static void PrepareAcceptedRecord(ScoreRecord candidate, DateTime acceptedAt)
    {
        candidate.Grade = GradeOf(candidate.Acc);
        candidate.Date = acceptedAt.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private sealed class StoreData
    {
        [JsonPropertyName("format")]
        [JsonPropertyOrder(0)]
        public string? Format { get; set; }

        [JsonPropertyName("formatVersion")]
        [JsonPropertyOrder(1)]
        public int FormatVersion { get; set; }

        [JsonPropertyName("records")]
        [JsonPropertyOrder(2)]
        public List<StoredRecord>? Records { get; set; }

        [JsonPropertyName("legacyRecords")]
        [JsonPropertyOrder(3)]
        public Dictionary<string, ScoreRecord?>? LegacyRecords { get; set; }
    }

    private sealed class StoredRecord
    {
        [JsonPropertyName("identity")]
        [JsonPropertyOrder(0)]
        public ScoreIdentity? Identity { get; set; }

        [JsonPropertyName("result")]
        [JsonPropertyOrder(1)]
        public ScoreRecord? Result { get; set; }
    }
}
