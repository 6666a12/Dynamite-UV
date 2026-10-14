using Godot;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse;

/// <summary>
/// Godot persistence wrapper around the shared score codec. Legacy packId:diff records remain
/// historical and never answer current four-part identity queries.
/// </summary>
public sealed class ScoreStore
{
    private const string SavePath = "user://scores.json";
    private readonly ScoreStoreCore _core = new();

    public static string KeyOf(string packId, string diff) => ScoreStoreCore.KeyOf(packId, diff);
    public static string GradeOf(double clearPercent) => ScoreStoreCore.GradeOf(clearPercent);

    public ScoreRecord? Get(ScoreIdentity identity) => _core.Get(identity);

    // TryUpdate 不再同步写盘：成功后由调用方 SaveDeferred，避免结算首帧被 IO 阻塞。
    public bool TryUpdate(ScoreIdentity identity, ScoreRecord candidate) =>
        _core.TryUpdate(identity, candidate);

    public ScoreRecord? Get(string packId, string diff) => _core.GetLegacy(packId, diff);

    public bool TryUpdate(string packId, string diff, ScoreRecord candidate) =>
        _core.TryUpdateLegacy(packId, diff, candidate);

    public void Load()
    {
        _core.Clear();
        if (!Godot.FileAccess.FileExists(SavePath))
            return;
        try
        {
            using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
            _core.LoadJson(file.GetAsText());
        }
        catch (Exception e)
        {
            _core.Clear();
            GD.PushWarning($"ScoreStore: 读取 {SavePath} 失败，按空成绩库处理。{e.Message}");
        }
    }

    public void LoadJson(string json) => _core.LoadJson(json);
    public string SaveJson() => _core.SaveJson();

    public void SaveDeferred() => Callable.From(Save).CallDeferred();

    private void Save()
    {
        try
        {
            using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
            file.StoreString(_core.SaveJson());
        }
        catch (Exception e)
        {
            GD.PushWarning($"ScoreStore: 写入 {SavePath} 失败。{e.Message}");
        }
    }
}
