using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Remote release browsing. The catalogue snapshot, expandable card and page requests live here;
/// downloading, installation receipts, authentication and local song selection keep their existing owners.</summary>
public partial class StoreScreen : Node2D
{
    private readonly StoreBrowseState _browse = new();
    private readonly StoreRequest<StoreRelease[]> _listing = new();
    private readonly StoreRequest<byte[]> _cover = new();
    private readonly StoreThumbnails _thumbnails = new();
    private StoreDeck _deck = null!;
    private StoreSidebar _sidebar = null!;
    private CatalogAccess? _access, _downloadAccess;
    private StoreRelease? _bound;
    private Guid? _coverId;
    private int _requestedOffset;
    private bool _firstPageReady, _invalidated;
    private string? _listError, _accessError;
    private InstallProgress? _lastInstall;
    private long _installRevision = -1;
    private double _thumbnailTick;

    public override void _Ready()
    {
        NetworkDeskChrome.Install(this, "谱面商店", "StoreBack", () => Navigate(UiRoutes.Main), store: true);
        var state = OnlineSession.Connection.State;
        _sidebar = new StoreSidebar(this, state.Descriptor?.Name ?? "未连接", state.Account?.Username ?? "账号", () => Navigate(UiRoutes.Online));
        _deck = new StoreDeck(this);
        _deck.Picked += Select; _deck.Primary += () => { if (_browse.Expanded is { } r) Download(r); };
        _deck.More += More; _deck.ShowAll += () => { _sidebar.Search.Text = ""; Browse(StoreFilter.All, false); };
        _sidebar.SearchRequested += () => Browse(_browse.Filter, true); _sidebar.RefreshRequested += () => Browse(_browse.Filter, true);
        _sidebar.FilterRequested += mode => Browse(mode, false); _sidebar.CancelRequested += CancelTask;
        try { _access = OnlineSession.CaptureCatalog(); Browse(StoreFilter.All, true); }
        catch (OnlineException ex) { Invalidate(Message(ex.Code)); }
        TransitionDirector.ReportSceneReady(() => { });
    }
    private bool Current => !_invalidated && _access is not null && OnlineSession.Connection.IsCurrent(_access);
    private void Navigate(string route)
    {
        if (TransitionDirector.IsBusy) return;
        GameSession.RescanPacks(); TransitionDirector.Navigate(route, TransitionKind.Back);
    }
    private void Browse(StoreFilter next, bool refresh)
    {
        if (!Current) return;
        _sidebar.CloseMenu(); string q = _sidebar.Search.Text.Trim();
        bool reset = refresh || q != _browse.Query;
        _listing.Clear(); _listError = null;
        if (reset) _firstPageReady = false;
        RefreshReceipts(); _browse.Begin(q, next, reset);
        if (next != StoreFilter.Installed && !_firstPageReady) RequestPage(0);
        Rebind();
    }
    private void More()
    {
        if (!Current || _listing.Pending || !_browse.HasMore || _browse.Filter == StoreFilter.Installed) return;
        _listError = null; RequestPage(_browse.NextOffset); UpdateStatus();
    }
    private void RequestPage(int offset)
    {
        var access = _access!; string query = _browse.Query; _requestedOffset = offset;
        _listing.Start(access, ct => OnlineSession.Catalog.ListAsync(access, query, offset, ct));
    }
    private void RefreshReceipts()
    {
        if (_access is null) return;
        _installRevision = OnlineSession.Installer.Revision;
        _browse.SetReceipts(_access.Identity, OnlineSession.Installer.Installed());
    }
    private void Rebind()
    {
        var pick = _browse.Expanded;
        if (_bound != pick || (_deck.Closing && pick is null)) Unbind();
        _deck.SyncRows(_browse.Visible);
        if (pick is not null && _bound is null) BindCard(pick, interactive: false);
        _sidebar.RefreshFilters(_browse.Filter, Current); UpdateStatus(); UpdateCommand();
    }
    private void Unbind()
    {
        _cover.Clear(); _coverId = null; _bound = null; _deck.ClearSelection();
    }
    private void Select(StoreRelease release)
    {
        if (!Current || TransitionDirector.IsBusy || !_browse.Choose(release.Id) || _browse.Find(release.Id) is not { } visible) return;
        if (_browse.Expanded is null)
        {
            _bound = null; _cover.Clear(); _coverId = null; _deck.Collapse(release.Id);
        }
        else BindCard(visible, interactive: true);
        // Selection retains the existing native row objects and scroll position.
        UpdateCommand();
    }
    private void Download(StoreRelease release)
    {
        if (!Current || TransitionDirector.IsBusy || _browse.Expanded?.Id != release.Id || _bound?.Id != release.Id) return;
        if (_browse.Receipt(release.Id) is { } installed)
        {
            GameSession.RescanPacks();
            var pack = GameSession.Packs.FirstOrDefault(p => p.Installation?.InstallationId == installed.InstallationId);
            if (pack is not null && pack.Charts.Count > 0)
            { GameSession.CommitSelection(pack, pack.Charts[0]); TransitionDirector.Navigate(UiRoutes.SongSelect, TransitionKind.Standard); }
            return;
        }
        if (OnlineSession.Installer.State.Status is "downloading" or "installing" || release.Display?.ValidationProfile != StoreContract.Profile) return;
        _downloadAccess = _access;
        // Deliberately no page cancellation token here. Only explicit Cancel acts on an installation.
        _ = OnlineSession.Installer.InstallAsync(_access!, OnlineSession.Connection.State.Descriptor!.Name, release);
        SyncTask();
    }
    private void BindCard(StoreRelease r, bool interactive)
    {
        _cover.Clear(); _bound = r; _coverId = r.Id; _deck.Expand(r, interactive);
        if (r.Display?.Cover is not null && _access is { } access) _cover.Start(access, ct => OnlineSession.Catalog.CoverAsync(access, r, ct));
    }
    private void CancelTask()
    {
        if (!Current || _access is not { } access) return;
        var job = OnlineSession.Installer.State;
        if (job.Source == access.Identity && job.ReleaseId is { } id && job.Status is ("downloading" or "installing")) OnlineSession.Installer.Cancel(access.Identity, id);
    }
    private void UpdateCommand() => _deck.ShowCommand(_browse.Expanded, _bound is not null && _browse.Receipt(_bound.Id) is not null,
        OnlineSession.Installer.State, _access?.Identity, Current);
    private void SyncTask()
    {
        var job = OnlineSession.Installer.State;
        if (_lastInstall == job) return;
        _lastInstall = job; _sidebar.ShowTask(job, Current ? _access?.Identity : null, OnlineSession.Installer.ActiveRelease);
        if (job.Status == "failed" && job.Error == "unauthorized" && _downloadAccess is { } captured && captured.Identity == job.Source)
            OnlineSession.Connection.RejectCatalogSession(captured);
        UpdateCommand();
    }
    private void UpdateStatus()
    {
        int count = _browse.Visible.Count; bool local = _browse.Filter == StoreFilter.Installed;
        string status = _accessError ?? _listError ?? (_listing.Pending ? (_requestedOffset == 0 ? "正在读取目录…" : "正在加载更多…") :
            local ? $"本服务器 · {count} 个已安装谱包" : $"已加载 {_browse.NextOffset} 条 · 当前显示 {count}");
        string empty = count > 0 ? "" : _accessError ?? _listError ?? (_listing.Pending ? "正在读取目录…" :
            local ? (_browse.Query.Length == 0 ? "本服务器尚无已安装谱包" : "已安装谱包中没有匹配项") :
            _browse.Filter == StoreFilter.MissingLoaded ? (_browse.HasMore ? "本批没有未安装谱包，可加载更多" : "已加载结果中没有未安装谱包") :
            _browse.Query.Length > 0 ? "没有匹配的谱包" : "服务器暂无已上架谱包");
        if (_browse.PageLimitReached && !local && _listError is null) status += " · 可缩小搜索范围";
        _deck.ShowListing(status, empty, Current && (_browse.Filter != StoreFilter.All || _browse.Query.Length > 0) && !_listing.Pending,
            Current && !local && _browse.HasMore, _listing.Pending);
    }
    private void Invalidate(string error)
    {
        _invalidated = true; _accessError = error; _listing.Clear(); _browse.Clear(); Unbind();
        _deck.SyncRows(_browse.Visible); _sidebar.RefreshFilters(_browse.Filter, false); _sidebar.ShowTask(null, null, null); UpdateStatus();
    }
    public override void _Process(double delta)
    {
        if (_invalidated) return;
        if (!Current) { Invalidate("会话已改变或过期，请返回账号页重新登录"); return; }
        var access = _access!;
        if (_listing.TryTake(access, out var page, out var error))
        {
            if (page is not null) { _browse.AcceptPage(_requestedOffset, page); _firstPageReady = true; Rebind(); }
            else if (error == "unauthorized") { OnlineSession.Connection.RejectCatalogSession(access); Invalidate(Message(error)); return; }
            else { _listError = Message(error ?? "connection_failed"); UpdateStatus(); }
        }
        if (_cover.TryTake(access, out var bytes, out var coverError))
        {
            if (coverError == "unauthorized") { OnlineSession.Connection.RejectCatalogSession(access); Invalidate(Message(coverError)); return; }
            if (bytes is not null && _bound is { } bound && _coverId == bound.Id && _browse.Expanded?.Id == bound.Id && bound.Display?.Cover is { } cover)
                _deck.SetCover(bytes, cover.MediaType);
        }
        SyncTask();
        if (Current && _installRevision != OnlineSession.Installer.Revision) { RefreshReceipts(); Rebind(); }
        if ((_thumbnailTick -= delta) <= 0 && _browse.Visible.Count != 0)
        {
            _thumbnailTick = .16;
            var onScreen = _deck.OnScreen(); _thumbnails.Step(onScreen.Select(r => r.Release), access);
            foreach (var row in onScreen) row.Thumbnail = _thumbnails.Get(row.Release.Id);
        }
    }
    public override void _UnhandledInput(InputEvent e)
    {
        if (TransitionDirector.IsBusy || e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        if (_sidebar.MenuOpen) _sidebar.CloseMenu();
        else if (_sidebar.Search.HasFocus()) _sidebar.Search.ReleaseFocus();
        else Navigate(UiRoutes.Main);
        GetViewport().SetInputAsHandled();
    }
    public override void _ExitTree() { _listing.Dispose(); _cover.Dispose(); _thumbnails.Dispose(); }
    internal static string Message(string code) => code switch
    {
        "unauthorized" => "会话过期，请重新登录", "not_found" => "发布已下架或不存在，请刷新列表",
        "rate_limited" => "请求过于频繁，请稍后重试", "integrity_failed" => "文件完整性不符",
        "unsupported_profile" => "发布兼容性审核尚未完成", "invalid_response" => "服务器响应无效",
        "incompatible_server" => "服务器曲库协议不兼容", "server_not_saved" => "服务器身份未能保存，请先在账号页确认",
        "storage_full" => "磁盘空间不足", "storage_denied" => "无法写入安装目录，请检查权限",
        "invalid_package" => "谱包损坏或不符合安全格式", "installation_failed" => "安装未完成，请检查存储空间后重试",
        "download_busy" => "已有下载任务", "redirect_refused" => "拒绝服务器重定向", "forbidden" => "服务器拒绝访问",
        "cancelled" => "操作已取消", _ => "连接失败，请手动重试",
    };
}
