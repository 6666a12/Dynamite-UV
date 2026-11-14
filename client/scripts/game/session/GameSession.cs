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
    private const int MaxPreloads = 4;
    private static readonly Dictionary<(string PackId, string ChartId), ChartPreload> Preloads = new();
    private static readonly LinkedList<(string PackId, string ChartId)> PreloadOrder = new();

    public static ChartPreload? CurrentPreload => Preloads.Values.FirstOrDefault();

    private static bool _init;
    private static long _installationRevision = -1;
    public static void RefreshInstalledPacks()
    {
        if (_installationRevision != OnlineSession.Installer.Revision) RescanPacks();
    }

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
        var revision = OnlineSession.Installer.Revision;
        Packs = ChartPack.ScanAll();
        _installationRevision = revision;
        ClearPreload();
        if (CurrentSelection is { } selection)
        {
            var pack = Packs.FirstOrDefault(p => p.LibraryId == selection.Pack.LibraryId);
            var diff = pack?.ChartOf(selection.Diff.ChartId);
            if (pack is null || diff is null) ClearSelection();
            else SetSelection(new(pack, diff, null));
        }
    }

    /// <summary>Stores a successful selection-screen preload without changing the committed chart.</summary>
    public static void CachePreload(ChartPack pack, ChartDiff diff, LoadedChart loaded)
    {
        var key = (pack.LibraryId, diff.ChartId);
        if (Preloads.ContainsKey(key))
            PreloadOrder.Remove(key);
        Preloads[key] = new ChartPreload(pack, diff, loaded);
        PreloadOrder.AddLast(key);
        while (PreloadOrder.Count > MaxPreloads)
        {
            var oldest = PreloadOrder.First!.Value;
            PreloadOrder.RemoveFirst();
            Preloads.Remove(oldest);
        }
        if (CurrentSelection is { } selection && selection.Matches(pack, diff))
            SetSelection(new ChartSelection(pack, diff, loaded));
    }

    public static LoadedChart? GetPreload(ChartPack pack, ChartDiff diff)
    {
        var key = (pack.LibraryId, diff.ChartId);
        // Mode changes keep the cache, but cannot reuse a different judge preset.
        if (!Preloads.TryGetValue(key, out var preload) ||
            preload.LoadedChart.Preset != V2Integration.PresetFor(diff.Difficulty))
            return null;
        PreloadOrder.Remove(key);
        PreloadOrder.AddLast(key);
        return preload.LoadedChart;
    }

    public static void ClearPreload()
    {
        Preloads.Clear();
        PreloadOrder.Clear();
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
        var i = Packs.FindIndex(pack => pack.LibraryId == selection.Pack.LibraryId);
        var next = Packs[(i + 1) % Packs.Count];
        if (next.Charts.Count == 0)
            return false;
        CommitSelection(next, next.Charts[0]);
        return true;
    }
}
