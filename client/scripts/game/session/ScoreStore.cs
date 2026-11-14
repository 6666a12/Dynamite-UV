using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Score;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse;

/// <summary>
/// Godot persistence wrapper around the shared score codec. Legacy packId:diff records remain
/// historical and never answer current four-part identity queries.
/// </summary>
public sealed class ScoreStore
{
    private const string SavePath = "user://scores.json";
    private readonly ModeScoreStore _core = new();
    private ModeScoreFile? _persistence;
    private ModeScoreFile Persistence => _persistence ??= new(
        ProjectSettings.GlobalizePath(SavePath), _core);

    public static string KeyOf(string packId, string diff) => ScoreStoreCore.KeyOf(packId, diff);
    public static string GradeOf(double clearPercent) => ScoreStoreCore.GradeOf(clearPercent);

    public static string CurrentMode => GameSession.Settings.GameplayMode == GameplayMode.Hardcore ? "hardcore" : "standard";
    public ScoreRecord? Get(ScoreIdentity identity, string? mode = null) => _core.For(mode ?? CurrentMode).Get(identity);

    // TryUpdate 不再同步写盘：成功后由调用方 SaveDeferred，避免结算首帧被 IO 阻塞。
    public bool TryUpdate(ScoreIdentity identity, ScoreRecord candidate, string? mode = null) =>
        _core.For(mode ?? CurrentMode).TryUpdate(identity, candidate);

    public ScoreRecord? Get(string packId, string diff, string? mode = null) => _core.For(mode ?? CurrentMode).GetLegacy(packId, diff);

    public bool TryUpdate(string packId, string diff, ScoreRecord candidate, string? mode = null) =>
        _core.For(mode ?? CurrentMode).TryUpdateLegacy(packId, diff, candidate);

    public void Load()
    {
        try
        {
            Persistence.Load();
        }
        catch (Exception e)
        {
            GD.PushWarning($"ScoreStore: 读取 {SavePath} 失败，保留当前成绩；下次保存前备份原文件。{e.Message}");
        }
    }

    public void LoadJson(string json) => _core.LoadJson(json);
    public string SaveJson() => _core.SaveJson();

    public void SaveDeferred() => Callable.From(Save).CallDeferred();

    private void Save()
    {
        try
        {
            Persistence.Save();
        }
        catch (Exception e)
        {
            GD.PushWarning($"ScoreStore: 写入 {SavePath} 失败。{e.Message}");
        }
    }
}
