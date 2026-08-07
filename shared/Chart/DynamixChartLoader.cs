using System.Text.Json;
using System.Text.RegularExpressions;

namespace DuxShared.Chart;

/// <summary>
/// 解析逆向提取的谱面 JSON（结构见规格书 §1）。
/// 容错：忽略 get_type 字段；空时间线直接用 Baked_Second（§1.3）；
/// 悬空 SubNoteId 不报错（§5.1，由判定计划构建时处理）。
/// </summary>
public static class DynamixChartLoader
{
    public static Chart LoadFile(string path) => Load(File.ReadAllText(path));

    public static Chart Load(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var name = GetString(root, "name", "");
        var (difficulty, title) = ParseName(name);

        var sections = new List<BarSection>();
        if (root.TryGetProperty("TimeLine", out var tl) &&
            tl.ValueKind == JsonValueKind.Object &&
            tl.TryGetProperty("BakedBarSections", out var arr) &&
            arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in arr.EnumerateArray())
            {
                sections.Add(new BarSection
                {
                    Bpm = GetDouble(s, "BPM", 150.0),
                    BarTime = GetDouble(s, "BarTime", 0.0),
                    Seconds = GetDouble(s, "Seconds", 0.0),
                });
            }
            sections.Sort((a, b) => a.BarTime.CompareTo(b.BarTime));
        }

        // 变速事件（§8.3）：[{BarTime, Value}]，阶跃倍率
        var dropSpeeds = new List<(double, double)>();
        if (root.TryGetProperty("NoteSystem__DropSpeeds", out var ds) &&
            ds.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in ds.EnumerateArray())
                dropSpeeds.Add((GetDouble(e, "BarTime", 0.0), GetDouble(e, "Value", 1.0)));
            dropSpeeds.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        }

        var chart = new Chart
        {
            Name = name,
            Title = title,
            Difficulty = difficulty,
            TotalMainNote = GetInt(root, "Baked_TotalMainNote", 0),
            Sections = sections,
            NotesLeft = LoadTrack(root, "NotesLeft", Track.Left),
            NotesCenter = LoadTrack(root, "NotesCenter", Track.Center),
            NotesRight = LoadTrack(root, "NotesRight", Track.Right),
            DropSpeeds = dropSpeeds,
        };

        // 命中秒：有时间线按 §3.2 公式换算（权威），无时间线回退 Baked_Second（§1.3）。
        if (sections.Count > 0)
        {
            foreach (var n in chart.AllNotes)
                n.Second = chart.BarTimeToSeconds(n.BarTime);
        }

        return chart;
    }

    private static List<Note> LoadTrack(JsonElement root, string prop, Track track)
    {
        var notes = new List<Note>();
        if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return notes;

        foreach (var e in arr.EnumerateArray())
        {
            var typeRaw = GetInt(e, "Type", 1);
            if (typeRaw < 1 || typeRaw > 9)
                continue; // 未知类型跳过（规格书 §4 全集为 1–9）

            var baked = GetDouble(e, "Baked_Second", 0.0);
            notes.Add(new Note
            {
                Id = GetInt(e, "Id", 0),
                SubNoteId = GetInt(e, "SubNoteId", -1),
                Type = (NoteType)typeRaw,
                Track = track,
                BarTime = GetDouble(e, "BarTime", 0.0),
                Position = GetDouble(e, "Position", 0.0),
                Width = GetDouble(e, "Width", 1.0),
                Second = baked, // 先填烘焙值；有时间线时 Load 末尾统一重算
                BakedSecond = baked,
            });
        }
        notes.Sort((a, b) => a.Second.CompareTo(b.Second));
        return notes;
    }

    // 从 "Map_0005.3 - The Villager [Hard]" 解析难度号与曲名；解析失败给默认值。
    private static (int difficulty, string title) ParseName(string name)
    {
        var m = Regex.Match(name, @"\.(\d+)\s*-\s*(.+)$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var diff))
            return (diff, m.Groups[2].Value.Trim());
        return (0, name);
    }

    private static string GetString(JsonElement e, string prop, string fallback) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;

    private static int GetInt(JsonElement e, string prop, int fallback)
    {
        if (!e.TryGetProperty(prop, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (int)d;
        return fallback;
    }

    private static double GetDouble(JsonElement e, string prop, double fallback) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetDouble(out var d)
            ? d
            : fallback;
}
