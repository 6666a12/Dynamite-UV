using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Catalog UI request generations are separate from the application-owned download lifetime.</summary>
public partial class StoreScreen : Node2D
{
    private CatalogAccess? _access, _downloadAccess;
    private readonly CancellationTokenSource _lifetime = new();
    private Task<StoreRelease[]>? _listing;
    private Task<byte[]>? _cover;
    private CancellationTokenSource? _coverCancellation;
    private Guid? _coverId;
    private readonly List<StoreRelease> _releases = new();
    private readonly List<StoreRow> _rows = new();
    private HashSet<Guid> _installed = new();
    private Dictionary<Guid, InstalledRelease> _installations = new();
    private readonly StoreThumbnails _thumbnails = new();
    private ScrollContainer _scroll = null!;
    private Button _task = null!, _cancel = null!;
    private StoreRelease? _selected;
    private VBoxContainer _list = null!;
    private StoreDetailsView _details = null!;
    private LineEdit _search = null!;
    private Label _status = null!;
    private Button _more = null!, _all = null!, _onlyInstalled = null!, _searchButton = null!;
    private Control _menu = null!;
    private bool _installedOnly, _hasMore;
    private int _nextOffset;
    private string _query = "";
    private InstallProgress? _lastInstall;
    public override void _Ready()
    {
        AddChild(new ColorRect { Size = UiLayout.DesignSize, Color = MenuStyle.Background, MouseFilter = Control.MouseFilterEnum.Ignore });
        OnlineWidgets.Link(this, "‹", new(64, 40, 50, 45), 36, () => Navigate(UiRoutes.Main), MenuStyle.Secondary);
        OnlineWidgets.Text(this, "谱面商店", new(124, 42, 600, 40), 32, MenuStyle.Text);
        var state = OnlineSession.Connection.State;
        OnlineWidgets.Text(this, state.Descriptor?.Name ?? "未连接", new(1390, 49, 200, 25), 18, MenuStyle.Secondary);
        OnlineWidgets.Link(this, (state.Account?.Username ?? "账号") + "  ▾", new(1640, 38, 236, 48), 18,
            () => _menu.Visible = !_menu.Visible);
        _search = OnlineWidgets.Input(this, "搜索曲名或谱师", new(96, 130, 550, 62), 80);
        _search.TextSubmitted += _ => Search();
        _searchButton = OnlineWidgets.Link(this, "搜索", new(580, 140, 66, 42), 18, Search);
        OnlineWidgets.Text(this, "最近上架 ↓", new(688, 150, 170, 24), 16, MenuStyle.Secondary);
        _all = OnlineWidgets.Link(this, "全部", new(96, 210, 90, 48), 18, () => Filter(false), MenuStyle.Accent);
        _onlyInstalled = OnlineWidgets.Link(this, "已安装", new(216, 210, 140, 48), 18, () => Filter(true), MenuStyle.Secondary);
        OnlineWidgets.Link(this, "刷新 ↻", new(700, 210, 140, 48), 18, Search, MenuStyle.Secondary);
        _task = OnlineWidgets.Link(this, "", new(376, 258, 310, 28), 16, () =>
        {
            if (OnlineSession.Installer.State.Source == _access?.Identity && OnlineSession.Installer.ActiveRelease is { } r) Select(r);
        }, MenuStyle.Accent);
        _cancel = OnlineWidgets.Link(this, "取消下载", new(700, 258, 160, 28), 16, () =>
        {
            if (_access is not null && OnlineSession.Installer.State.ReleaseId is { } id) OnlineSession.Installer.Cancel(_access.Identity, id);
        }, MenuStyle.Secondary);
        var scroll = _scroll = new ScrollContainer { Position = new(64, 296), Size = new(836, 700),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list = new VBoxContainer { CustomMinimumSize = new(800, 0) }; _list.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(_list); AddChild(scroll);
        _status = OnlineWidgets.Text(this, "", new(96, 1008, 680, 38), 18, MenuStyle.Secondary);
        _more = OnlineWidgets.Link(this, "加载更多 ↓", new(680, 1006, 200, 44), 18, More, MenuStyle.Secondary);
        _details = new StoreDetailsView(); AddChild(_details); _details.Download += Download;
        BuildMenu();
        try
        {
            _access = OnlineSession.CaptureCatalog(); RefreshInstalled(); Search();
        }
        catch (OnlineException ex) { _status.Text = Message(ex.Code); }
        TransitionDirector.ReportSceneReady(() => { });
    }
    private void BuildMenu()
    {
        _menu = new Control { Position = new(1500, 92), Size = new(344, 350), Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        OnlineWidgets.Surface(_menu, new(0, 0, 344, 350));
        OnlineWidgets.Text(_menu, "账号", new(24, 18, 296, 40), 22, MenuStyle.Text);
        OnlineWidgets.Link(_menu, "本人记录（尚未接入）", new(24, 80, 296, 56), 20, () => { }, MenuStyle.Muted).Disabled = true;
        OnlineWidgets.Link(_menu, "切换服务器  ↗", new(24, 144, 296, 56), 22, () => Navigate(UiRoutes.Online));
        OnlineWidgets.Link(_menu, "管理账号 / 退出登录", new(24, 208, 296, 56), 22, () => Navigate(UiRoutes.Online));
        OnlineWidgets.Link(_menu, "关闭", new(24, 280, 296, 48), 20, () => _menu.Visible = false, MenuStyle.Secondary);
        AddChild(_menu);
    }
    private void Navigate(string route)
    {
        // Install completion is consumed on entering a new local library, never mutating a live song list.
        GameSession.RescanPacks(); TransitionDirector.Navigate(route, TransitionKind.Back);
    }
    private void Search()
    {
        if (_access is null || _listing is { IsCompleted: false }) return;
        CancelCover();
        _query = _search.Text.Trim();
        if (_installedOnly) { RefreshInstalled(); Rebuild(); return; }
        _nextOffset = 0; _hasMore = false;
        _releases.Clear(); _selected = null; _details.ShowRelease(null); Rebuild();
        _listing = OnlineSession.Catalog.ListAsync(_access, _query, 0, _lifetime.Token); _status.Text = "正在读取曲库…";
    }
    private void More()
    {
        if (_access is null || _installedOnly || !_hasMore || _listing is { IsCompleted: false }) return;
        _listing = OnlineSession.Catalog.ListAsync(_access, _query, _nextOffset, _lifetime.Token);
    }
    private void Filter(bool installed)
    {
        if (_listing is { IsCompleted: false }) return;
        _installedOnly = installed;
        _query = _search.Text.Trim();
        if (installed) { RefreshInstalled(); Rebuild(); }
        else Search();
    }
    private void RefreshInstalled()
    {
        _installations = OnlineSession.Installer.Installed().Where(e => e.Source == _access?.Identity)
            .GroupBy(e => e.Release.Id).ToDictionary(g => g.Key, g => g.First());
        _installed = _installations.Keys.ToHashSet();
    }
    private void Rebuild()
    {
        foreach (var child in _list.GetChildren()) { _list.RemoveChild(child); child.QueueFree(); }
        _rows.Clear();
        var entries = _installedOnly ? _installations.Values.Select(e => e.Release).Where(r =>
            (r.Title + " " + r.Artist + " " + string.Join(" ", r.Display?.Charts.SelectMany(c => c.Charters) ?? []))
                .Contains(_query, StringComparison.OrdinalIgnoreCase)).ToList() : _releases;
        foreach (var release in entries)
        {
            var row = new StoreRow(release, Select) { Thumbnail = _thumbnails.Get(release.Id) }; _list.AddChild(row); _rows.Add(row); row.Select(release.Id == _selected?.Id);
        }
        if (_selected is not null && entries.All(r => r.Id != _selected.Id) && _selected.Id != OnlineSession.Installer.ActiveRelease?.Id) { _selected = null; _details.ShowRelease(null); }
        _status.Text = entries.Count == 0 ? (_query.Length > 0 ? "没有匹配的谱包" : _installedOnly ? "本服务器尚无已安装谱包" : "服务器暂无已上架谱包") : $"{entries.Count} 个谱包";
        _all.AddThemeColorOverride("font_color", !_installedOnly ? MenuStyle.Accent : MenuStyle.Secondary);
        _onlyInstalled.AddThemeColorOverride("font_color", _installedOnly ? MenuStyle.Accent : MenuStyle.Secondary);
    }
    private void Select(StoreRelease release)
    {
        if (_access is null || !OnlineSession.Connection.IsCurrent(_access)) return;
        CancelCover();
        _selected = release; _details.ShowRelease(release); Rebuild();
        _coverId = release.Id;
        _coverCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _cover = release.Display?.Cover is null ? null : OnlineSession.Catalog.CoverAsync(_access, release, _coverCancellation.Token);
    }
    private void Download(StoreRelease release)
    {
        if (_access is null || !OnlineSession.Connection.IsCurrent(_access)) return;
        if (_installations.TryGetValue(release.Id, out var installed))
        {
            GameSession.RescanPacks();
            var pack = GameSession.Packs.FirstOrDefault(p => p.Installation?.InstallationId == installed.InstallationId);
            if (pack is not null && pack.Charts.Count > 0)
            { GameSession.CommitSelection(pack, pack.Charts[0]); TransitionDirector.Navigate(UiRoutes.SongSelect, TransitionKind.Standard); }
            return;
        }
        _downloadAccess = _access;
        _ = OnlineSession.Installer.InstallAsync(_access, OnlineSession.Connection.State.Descriptor!.Name, release);
    }
    public override void _Process(double delta)
    {
        var current = _access is not null && OnlineSession.Connection.IsCurrent(_access);
        if (!current) { _lifetime.Cancel(); _status.Text = "会话已改变或过期，请返回账号页重新登录"; }
        if (_listing is { IsCompleted: true } listing)
        {
            _listing = null;
            if (listing.IsCompletedSuccessfully && current)
            {
                _nextOffset += listing.Result.Length;
                _hasMore = listing.Result.Length == 50 && _nextOffset <= 10000;
                foreach (var row in listing.Result) if (_releases.All(r => r.Id != row.Id)) _releases.Add(row);
                Rebuild();
                if (_selected is null && !_installedOnly && _releases.Count > 0) Select(_releases[0]);
            }
            else HandleError(listing.Exception);
        }
        if (_cover is { IsCompleted: true } cover)
        {
            _cover = null;
            if (cover.IsCompletedSuccessfully && current && _selected?.Id == _coverId && _selected?.Display?.Cover is { } meta)
                _details.SetCover(cover.Result, meta.MediaType);
            else if (cover.IsFaulted) { _ = cover.Exception; } // Cover failure leaves the neutral placeholder.
        }
        _more.Disabled = !current || _installedOnly || !_hasMore || _listing is not null;
        _searchButton.Disabled = !current || _listing is not null;
        var progress = OnlineSession.Installer.State;
        var activeHere = progress.Source == _access?.Identity && progress.ReleaseId is not null;
        _task.Visible = activeHere;
        var taskState = progress.Status switch { "downloading" => "下载中", "installing" => "安装中", "installed" => "已安装", "failed" => "未完成", _ => "等待" };
        _task.Text = activeHere ? $"下载任务 · {taskState} {progress.Received * 100 / Math.Max(1, progress.Total)}% ↗" : "";
        _cancel.Visible = activeHere && progress.Status is "downloading" or "installing";
        if (current && _access is not null)
        {
            var visibleRows = _rows.Where(r => r.GetGlobalRect().Intersects(_scroll.GetGlobalRect())).ToArray();
            if (_thumbnails.Step(visibleRows.Select(r => r.Release), _access))
                foreach (var row in _rows) { row.Thumbnail = _thumbnails.Get(row.Release.Id); row.QueueRedraw(); }
        }
        if (_lastInstall != progress)
        {
            if (progress.Status == "installed") { RefreshInstalled(); Rebuild(); }
            if (progress.Error == "unauthorized" && _downloadAccess is not null && progress.Source == _downloadAccess.Identity)
                OnlineSession.Connection.RejectCatalogSession(_downloadAccess);
            _lastInstall = progress;
        }
        _details.Refresh(progress, _access?.Identity, _selected is not null && _installed.Contains(_selected.Id), current);
    }
    private void HandleError(AggregateException? error)
    {
        var code = error?.GetBaseException() is OnlineException ex ? ex.Code : "connection_failed";
        if (code == "unauthorized" && _access is not null) OnlineSession.Connection.RejectCatalogSession(_access);
        _status.Text = Message(code);
    }
    private void CancelCover()
    {
        _coverCancellation?.Cancel(); _coverCancellation?.Dispose(); _coverCancellation = null;
        if (_cover is { } pending) _ = pending.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        _cover = null;
    }
    public override void _ExitTree()
    {
        _thumbnails.Dispose();
        CancelCover(); _lifetime.Cancel(); _lifetime.Dispose();
        if (_listing is { } pending) _ = pending.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }
    internal static string Message(string code) => code switch
    {
        "unauthorized" => "会话过期，请重新登录", "not_found" => "发布已下架或不存在，请刷新列表",
        "rate_limited" => "请求过于频繁，请稍后重试", "integrity_failed" => "文件完整性不符",
        "unsupported_profile" => "发布兼容性审核尚未完成", "invalid_response" => "服务器响应无效",
        "incompatible_server" => "服务器曲库协议不兼容", "server_not_saved" => "服务器身份未能保存，请先在账号页确认",
        "storage_full" => "磁盘空间不足", "storage_denied" => "无法写入安装目录，请检查权限",
        "invalid_package" => "谱包损坏或不符合安全格式",
        "installation_failed" => "安装未完成，请检查存储空间后重试", "download_busy" => "已有下载任务",
        "redirect_refused" => "拒绝服务器重定向", "forbidden" => "服务器拒绝访问", "cancelled" => "操作已取消",
        _ => "连接失败，请手动重试",
    };
}
