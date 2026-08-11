using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace DuxCommunity;

/// <summary>单个 谱面×难度 的历史最佳成绩。</summary>
public sealed class ScoreRecord
{
    [JsonPropertyName("score")] public int Score { get; set; }        // 0–1,000,000 归一化分数
    [JsonPropertyName("acc")] public double Acc { get; set; }          // Clear%（0–100）
    [JsonPropertyName("maxCombo")] public int MaxCombo { get; set; }
    [JsonPropertyName("grade")] public string Grade { get; set; } = "C";
    [JsonPropertyName("perfect")] public int Perfect { get; set; }
    [JsonPropertyName("great")] public int Great { get; set; }
    [JsonPropertyName("good")] public int Good { get; set; }
    [JsonPropertyName("miss")] public int Miss { get; set; }
    [JsonPropertyName("date")] public string Date { get; set; } = "";
}

/// <summary>
/// 本地成绩库：user://scores.json，按 "packId:diff" 存最佳成绩。
/// 启动时 Load，结算时 TryUpdate 写回。评级规则（MVP，样式稿已定）：
/// Clear% ≥98 Ω，≥95 S，≥90 A，≥80 B，否则 C。
/// </summary>
public sealed class ScoreStore
{
    private const string SavePath = "user://scores.json";

    private readonly Dictionary<string, ScoreRecord> _records = new();

    public static string KeyOf(string packId, string diff) => $"{packId}:{diff}";

    public static string GradeOf(double clearPercent) => clearPercent switch
    {
        >= 98.0 => "Ω",
        >= 95.0 => "S",
        >= 90.0 => "A",
        >= 80.0 => "B",
        _ => "C",
    };

    public ScoreRecord? Get(string packId, string diff) =>
        _records.TryGetValue(KeyOf(packId, diff), out var r) ? r : null;

    /// <summary>若新归一化分数更高则写回并落盘，返回是否刷新纪录。</summary>
    public bool TryUpdate(string packId, string diff, ScoreRecord candidate)
    {
        var key = KeyOf(packId, diff);
        if (_records.TryGetValue(key, out var old) && old.Score >= candidate.Score)
            return false;
        candidate.Grade = GradeOf(candidate.Acc);
        candidate.Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _records[key] = candidate;
        Save();
        return true;
    }

    public void Load()
    {
        _records.Clear();
        if (!Godot.FileAccess.FileExists(SavePath))
            return;
        try
        {
            using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
            var json = f.GetAsText();
            var data = JsonSerializer.Deserialize<Dictionary<string, ScoreRecord>>(json);
            if (data != null)
                foreach (var (k, v) in data)
                    _records[k] = v;
        }
        catch (Exception e)
        {
            GD.PushWarning($"ScoreStore: 读取 {SavePath} 失败，按空成绩库处理。{e.Message}");
        }
    }

    private void Save()
    {
        try
        {
            using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
            f.StoreString(JsonSerializer.Serialize(_records,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            GD.PushWarning($"ScoreStore: 写入 {SavePath} 失败。{e.Message}");
        }
    }
}
