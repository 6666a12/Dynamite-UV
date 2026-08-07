using Godot;

namespace DuxCommunity.Game;

/// <summary>
/// 跨场景会话状态：当前选中的谱面包/难度、成绩库、包列表。
/// 静态类（Godot 场景切换无法传参，用这里接力）。
/// </summary>
public static class GameSession
{
    public static readonly ScoreStore Scores = new();

    public static List<ChartPack> Packs { get; private set; } = new();
    public static ChartPack? SelectedPack;
    public static ChartDiff? SelectedDiff;

    private static bool _init;

    public static void EnsureInit()
    {
        if (_init)
            return;
        _init = true;
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://charts"));
        Scores.Load();
        RescanPacks();
    }

    public static void RescanPacks() => Packs = ChartPack.ScanAll();

    /// <summary>选下一首（环形），难度取该包最低档；返回 false 表示曲库为空。</summary>
    public static bool SelectNextSong()
    {
        if (Packs.Count == 0 || SelectedPack == null)
            return false;
        var i = Packs.IndexOf(SelectedPack);
        var next = Packs[(i + 1) % Packs.Count];
        if (next.Charts.Count == 0)
            return false;
        SelectedPack = next;
        SelectedDiff = next.Charts[0];
        return true;
    }
}
