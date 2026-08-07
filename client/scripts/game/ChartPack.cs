using System.Text.Json;
using Godot;

namespace DuxCommunity.Game;

/// <summary>谱面包内一个难度条目（meta.json 的 charts[]）。</summary>
public sealed class ChartDiff
{
    public required string Diff { get; init; }   // casual/normal/hard/mega/giga
    public required int Level { get; init; }
    public required string File { get; init; }
}

/// <summary>
/// 谱面包：一个目录，含 meta.json + chart_<diff>.json + 音频 + 封面。
/// 运行时扫描 user://charts/（玩家导入，优先）与 res://testdata/packs/（开发用）。
/// </summary>
public sealed class ChartPack
{
    public static readonly string[] DiffOrder = { "casual", "normal", "hard", "mega", "giga" };

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required string Charter { get; init; }
    public required string DirPath { get; init; }
    public string Audio { get; init; } = "";
    public string Cover { get; init; } = "";
    public List<ChartDiff> Charts { get; init; } = new();

    public string ChartPathFor(ChartDiff d) => $"{DirPath}/{d.File}";
    public string AudioPath => $"{DirPath}/{Audio}";
    public string? CoverPath => Cover.Length > 0 ? $"{DirPath}/{Cover}" : null;

    public ChartDiff? DiffOf(string diff) => Charts.FirstOrDefault(c => c.Diff == diff);

    /// <summary>谱面时长（≈最后 note 秒 + 2s 余量），用于选曲界面展示。</summary>
    public double LoadDurationSec(ChartDiff d)
    {
        var chart = DuxShared.Chart.DynamixChartLoader.Load(
            Godot.FileAccess.GetFileAsString(ChartPathFor(d)));
        var last = chart.AllNotes.Select(n => n.Second).DefaultIfEmpty(0).Max();
        return last + 2.0;
    }

    public int LoadNoteCount(ChartDiff d) =>
        DuxShared.Chart.DynamixChartLoader.Load(
            Godot.FileAccess.GetFileAsString(ChartPathFor(d))).TotalMainNote;

    public static List<ChartPack> ScanAll()
    {
        var byId = new Dictionary<string, ChartPack>();
        // 先 res://（开发用），后 user://（玩家导入可覆盖同 id）
        foreach (var root in new[] { "res://testdata/packs", "user://charts" })
        {
            using var dir = DirAccess.Open(root);
            if (dir == null)
                continue;
            foreach (var sub in dir.GetDirectories())
            {
                var pack = Load($"{root}/{sub}");
                if (pack != null)
                    byId[pack.Id] = pack;
            }
        }
        return byId.Values.OrderBy(p => p.Title).ToList();
    }

    public static ChartPack? Load(string dirPath)
    {
        var metaPath = $"{dirPath}/meta.json";
        if (!Godot.FileAccess.FileExists(metaPath))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(metaPath));
            var root = doc.RootElement;
            var charts = new List<ChartDiff>();
            foreach (var c in root.GetProperty("charts").EnumerateArray())
            {
                charts.Add(new ChartDiff
                {
                    Diff = c.GetProperty("diff").GetString() ?? "hard",
                    Level = c.GetProperty("level").GetInt32(),
                    File = c.GetProperty("file").GetString() ?? "",
                });
            }
            // 按难度档排序
            charts = charts.OrderBy(c => Array.IndexOf(DiffOrder, c.Diff)).ToList();
            return new ChartPack
            {
                Id = root.GetProperty("id").GetString() ?? "",
                Title = root.GetProperty("title").GetString() ?? "?",
                Artist = root.TryGetProperty("artist", out var a) ? a.GetString() ?? "-" : "-",
                Charter = root.TryGetProperty("charter", out var ch) ? ch.GetString() ?? "-" : "-",
                DirPath = dirPath,
                Audio = root.TryGetProperty("audio", out var au) ? au.GetString() ?? "" : "",
                Cover = root.TryGetProperty("cover", out var co) ? co.GetString() ?? "" : "",
                Charts = charts,
            };
        }
        catch (Exception e)
        {
            GD.PushWarning($"ChartPack: 解析 {metaPath} 失败：{e.Message}");
            return null;
        }
    }
}
