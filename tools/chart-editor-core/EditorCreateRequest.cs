using System.Text;
using System.Text.RegularExpressions;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public enum EditorCreateMode { NewPackage, AddDifficulty }

public sealed record EditorCreateForm(
    string PackId, string Title, string Artist, string AudioPath,
    string ChartId, int DifficultyIndex, string CustomKey, int? Level, bool Unrated,
    string Charter, double Bpm, double OffsetSec, string? CoverPath);

public sealed record EditorCreateChartSummary(V2Difficulty Difficulty, string? DifficultyKey);

public sealed record EditorCreatePackageSummary(IReadOnlyList<string> ChartIds,
    IReadOnlyList<EditorCreateChartSummary> Charts);

public sealed record EditorCreateResult(EditorProjectDraftRequest? DraftRequest, EditableChart? Chart);

public static class EditorCreateRequest
{
    public static EditorCreateResult Build(EditorCreateMode mode, EditorCreateForm form, int gridDivisor)
    {
        var id = form.ChartId;
        if (!Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"))
            throw new InvalidOperationException("Chart ID 只能包含字母、数字、点、下划线和短横线。");
        var difficulty = (V2Difficulty)form.DifficultyIndex;
        var key = difficulty == V2Difficulty.Custom ? form.CustomKey : null;
        if (difficulty == V2Difficulty.Custom && string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("CUSTOM 难度必须填写 difficulty key。");
        var unrated = form.Unrated;
        int? level = null;
        if (!unrated)
        {
            if (form.Level is not { } parsed || parsed is < 1 or > 99)
                throw new InvalidOperationException("等级必须是 1–99，或勾选 UNRATED。");
            level = parsed;
        }
        var charter = form.Charter;
        if (charter.Length == 0) throw new InvalidOperationException("请填写 Charter。");

        if (mode == EditorCreateMode.NewPackage)
            return new EditorCreateResult(new EditorProjectDraftRequest(form.PackId, form.Title,
                form.Artist, id, difficulty, key, level, unrated, charter, form.Bpm, form.OffsetSec,
                gridDivisor, form.AudioPath, form.CoverPath), null);

        var chart = new EditableChart
        {
            Id = id, Difficulty = difficulty, DifficultyKey = key, Level = level, Unrated = unrated,
            File = $"charts/{id}.json", Audio = null, Preview = null, AudioOffsetSec = 0,
        };
        chart.Charters.Add(charter);
        chart.Bpms.Add(new EditableBpm { Time = ExactBarTime.Zero, Bpm = 150 });
        return new EditorCreateResult(null, chart);
    }
}

public static class EditorCreatePreflight
{
    public static string? Validate(EditorCreateMode mode, EditorCreateForm form,
        EditorCreatePackageSummary? package, bool audioAvailable, bool coverAvailable)
    {
        if (!Regex.IsMatch(form.ChartId, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"))
            return "Chart ID 只能包含字母、数字、点、下划线和短横线。";
        if (form.DifficultyIndex == (int)V2Difficulty.Custom)
        {
            if (form.CustomKey.Length == 0) return "CUSTOM 难度必须填写 difficulty key。";
            if (form.CustomKey.Length > 24) return "difficulty key 最多 24 个字符。";
        }
        if (!form.Unrated && form.Level is null) return "等级必须是 1–99，或勾选 UNRATED。";
        if (form.Charter.Length == 0) return "请填写 Charter。";

        if (mode == EditorCreateMode.NewPackage)
        {
            if (form.PackId.Length == 0) return "请填写 Pack ID。";
            if (form.Title.Length == 0) return "请填写 Title。";
            if (form.AudioPath.Length == 0) return "新建包需要一个可访问的外部音频文件。";
            if (!audioAvailable) return "音频文件不存在或不可访问。";
            if (!EditorProjectDraftFactory.IsSupportedAudioPath(form.AudioPath))
                return "音频扩展名不被支持：需要 .wav/.mp3/.flac/.ogg/.opus/.m4a/.aac。";
            if (form.CoverPath is not null && !coverAvailable) return "封面文件不存在或不可访问。";
            return null;
        }

        if (package is null) return "没有打开的包。";
        if (package.ChartIds.Contains(form.ChartId, StringComparer.Ordinal))
            return $"Chart ID '{form.ChartId}' 已存在。";
        if (form.DifficultyIndex == (int)V2Difficulty.Custom && package.Charts.Any(chart =>
                chart.Difficulty == V2Difficulty.Custom && chart.DifficultyKey is { } existing &&
                FoldDifficultyKey(existing) == FoldDifficultyKey(form.CustomKey)))
            return $"difficulty key '{form.CustomKey}' 在本包内已存在。";
        return null;
    }

    private static string FoldDifficultyKey(string key)
    {
        try
        {
            return key.Normalize(NormalizationForm.FormC).ToUpperInvariant().ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return key.ToUpperInvariant().ToLowerInvariant();
        }
    }
}
