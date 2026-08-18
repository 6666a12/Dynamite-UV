using Godot;

namespace DynamiteUniverse.Game;

/// <summary>
/// 跨场景会话状态：当前选中的谱面包/难度、成绩库、包列表。
/// 静态类（Godot 场景切换无法传参，用这里接力）。
/// </summary>
public static class GameSession
{
    public static readonly ScoreStore Scores = new();
    public static readonly GameSettings Settings = new();

    public static List<ChartPack> Packs { get; private set; } = new();
    public static ChartSelection? CurrentSelection { get; private set; }
    public static ChartPreload? CurrentPreload { get; private set; }

    private static bool _init;

    public static void EnsureInit()
    {
        if (_init)
            return;
        _init = true;
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://charts"));
        Settings.Load();
        Scores.Load();
        RescanPacks();
    }

    public static void RescanPacks()
    {
        Packs = ChartPack.ScanAll();
        if (CurrentSelection is { } selection && !Packs.Any(pack =>
                StringComparer.Ordinal.Equals(pack.Id, selection.Pack.Id)))
            ClearSelection();
        if (CurrentPreload is { } preload && !Packs.Any(pack =>
                StringComparer.Ordinal.Equals(pack.Id, preload.Pack.Id)))
            ClearPreload();
    }

    /// <summary>Stores a successful selection-screen preload without changing the committed chart.</summary>
    public static void CachePreload(ChartPack pack, ChartDiff diff, LoadedChart loaded)
    {
        CurrentPreload = new ChartPreload(pack, diff, loaded);
        if (CurrentSelection is { } selection && selection.Matches(pack, diff))
            SetSelection(new ChartSelection(pack, diff, loaded));
    }

    public static LoadedChart? GetPreload(ChartPack pack, ChartDiff diff) =>
        CurrentPreload is { } preload && preload.Matches(pack, diff) ? preload.LoadedChart : null;

    public static void ClearPreload()
    {
        CurrentPreload = null;
        if (CurrentSelection is { } selection)
            SetSelection(selection with { LoadedChart = null });
    }

    /// <summary>Atomically commits the chart that will be used by the next gameplay scene.</summary>
    public static void CommitSelection(ChartPack pack, ChartDiff diff, LoadedChart? loaded = null) =>
        SetSelection(new ChartSelection(pack, diff, loaded ?? GetPreload(pack, diff)));

    public static void ClearSelection() => CurrentSelection = null;

    private static void SetSelection(ChartSelection selection) => CurrentSelection = selection;

    /// <summary>选下一首（环形），难度取该包最低档；返回 false 表示曲库为空。</summary>
    public static bool SelectNextSong()
    {
        var selection = CurrentSelection;
        if (Packs.Count == 0 || selection is null)
            return false;
        var i = Packs.IndexOf(selection.Pack);
        var next = Packs[(i + 1) % Packs.Count];
        if (next.Charts.Count == 0)
            return false;
        CommitSelection(next, next.Charts[0]);
        return true;
    }
}
