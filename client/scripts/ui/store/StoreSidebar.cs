using Godot;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Native search, source identity, filters and app-owned task. Never bound to the expanded card.</summary>
internal sealed class StoreSidebar
{
    private readonly Button _submit, _refresh, _cancel;
    private readonly Button[] _filters;
    private readonly Label _taskTitle, _taskInfo;
    private readonly ColorRect _rail, _fill;
    private readonly Control _menu;
    public LineEdit Search { get; }
    public event Action? SearchRequested, RefreshRequested, CancelRequested;
    public event Action<StoreFilter>? FilterRequested;

    public StoreSidebar(Node parent, string server, string account, Action openAccount)
    {
        parent.AddChild(new ColorRect { Position = new(68, 133), Size = new(4, 24), Color = NetworkDeskChrome.Teal, MouseFilter = Control.MouseFilterEnum.Ignore });
        OnlineWidgets.Text(parent, "浏览", new(85, 117, 340, 55), 37, NetworkDeskChrome.Ink);
        Search = OnlineWidgets.Input(parent, "搜索曲名或谱师", new(68, 182, 328, 62), 80); Search.Name = "StoreSearch";
        NetworkDeskChrome.InputStyle(Search, 27); Search.ClearButtonEnabled = true; Search.TextSubmitted += _ => SearchRequested?.Invoke();
        _submit = OnlineWidgets.Link(parent, "搜索", new(404, 180, 88, 58), 23, () => SearchRequested?.Invoke()); _submit.Name = "StoreSubmit";
        _filters = new Button[3];
        (StoreFilter Filter, string Name, string Text)[] filters = [(StoreFilter.All, "StoreFilterAll", "全部"), (StoreFilter.MissingLoaded, "StoreFilterMissing", "未安装 · 已加载"), (StoreFilter.Installed, "StoreFilterInstalled", "已安装")];
        for (var i = 0; i < filters.Length; i++)
        {
            var entry = filters[i]; var b = OnlineWidgets.Link(parent, entry.Text, new(68, 302 + 76 * i, 418, 64), 28, () => FilterRequested?.Invoke(entry.Filter));
            b.Name = entry.Name; _filters[i] = b;
        }
        _refresh = OnlineWidgets.Link(parent, "刷新 ↻", new(68, 549, 174, 54), 22, () => RefreshRequested?.Invoke()); _refresh.Name = "StoreRefresh";
        var origin = OnlineWidgets.Text(parent, server, new(68, 647, 420, 38), 20, MenuStyle.Secondary); origin.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; origin.TooltipText = server;
        var profile = OnlineWidgets.Link(parent, account + "  ▾", new(68, 688, 418, 58), 26, ToggleMenu); profile.Name = "StoreAccount"; profile.ClipText = true;
        _taskTitle = OnlineWidgets.Text(parent, "", new(68, 809, 420, 42), 23, MenuStyle.Text); _taskTitle.Name = "StoreTask"; _taskTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _taskInfo = OnlineWidgets.Text(parent, "", new(68, 853, 420, 43), 18, MenuStyle.Secondary); _taskInfo.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _rail = new ColorRect { Position = new(68, 909), Size = new(419, 4), Color = MenuStyle.Surface, MouseFilter = Control.MouseFilterEnum.Ignore }; parent.AddChild(_rail);
        _fill = new ColorRect { Color = MenuStyle.Accent, MouseFilter = Control.MouseFilterEnum.Ignore }; _rail.AddChild(_fill);
        _cancel = OnlineWidgets.Link(parent, "取消下载", new(68, 929, 182, 54), 20, () => CancelRequested?.Invoke()); _cancel.Name = "StoreCancel";
        _menu = new Control { Name = "StoreAccountMenu", Position = new(68, 738), Size = new(420, 237), Visible = false, ZIndex = 4, MouseFilter = Control.MouseFilterEnum.Stop };
        OnlineWidgets.Surface(_menu, new(0, 0, 420, 237));
        OnlineWidgets.Link(_menu, "切换服务器  ↗", new(23, 19, 370, 63), 24, openAccount);
        OnlineWidgets.Link(_menu, "管理账号 / 退出登录", new(23, 88, 370, 63), 24, openAccount);
        OnlineWidgets.Link(_menu, "关闭", new(23, 157, 370, 60), 21, ToggleMenu, MenuStyle.Secondary);
        parent.AddChild(_menu); ShowTask(null, null, null);
    }
    public bool MenuOpen => _menu.Visible;
    public void CloseMenu() => _menu.Visible = false;
    private void ToggleMenu() => _menu.Visible = !_menu.Visible;
    public void RefreshFilters(StoreFilter filter, bool enabled)
    {
        _submit.Disabled = _refresh.Disabled = !enabled; Search.Editable = enabled;
        for (var i = 0; i < _filters.Length; i++)
        {
            _filters[i].Disabled = !enabled;
            _filters[i].AddThemeColorOverride("font_color", i == (int)filter ? MenuStyle.Accent : MenuStyle.Secondary);
            _filters[i].AddThemeStyleboxOverride("normal", i == (int)filter
                ? new StyleBoxFlat { BgColor = MenuStyle.Active, BorderWidthLeft = 3, BorderColor = MenuStyle.Accent, ContentMarginLeft = 12 }
                : new StyleBoxEmpty { ContentMarginLeft = 12 });
        }
    }
    public void ShowTask(InstallProgress? progress, CatalogIdentity? source, StoreRelease? active)
    {
        var here = progress is not null && source is not null && progress.Source == source && progress.ReleaseId is not null;
        var busy = here && progress!.Status is ("downloading" or "installing");
        _taskTitle.Visible = _taskInfo.Visible = here; _rail.Visible = _cancel.Visible = busy; _cancel.Disabled = !busy;
        if (!here) return;
        var job = progress!;
        _taskTitle.Text = active is not null && active.Id == job.ReleaseId ? active.Title : "下载任务";
        _taskTitle.TooltipText = _taskTitle.Text;
        _taskInfo.Text = job.Status switch
        {
            "downloading" => $"下载中  {job.Received:N0} / {job.Total:N0} B",
            "installing" => "正在校验并安装…", "installed" => "已安装",
            "failed" => StoreScreen.Message(job.Error ?? "installation_failed"), _ => job.Status,
        };
        _taskInfo.TooltipText = _taskInfo.Text;
        _fill.Size = new(419 * Mathf.Clamp((float)job.Received / Math.Max(1, job.Total), 0, 1), 4);
    }
}
