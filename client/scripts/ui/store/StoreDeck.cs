using Godot;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Right-hand viewport and command bar. Page filters/requests provide data; this object never selects a record implicitly.</summary>
internal sealed class StoreDeck
{
    public const float RowHeight = 96;
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _list;
    private readonly StoreDetailsView _card;
    private readonly Dictionary<Guid, StoreRow> _rows = [];
    private readonly Label _empty, _count, _caption, _status;
    private readonly Button _all, _more, _primary;
    private readonly ColorRect _commandMark;
    private readonly Action<Color> _primaryTint;
    private Guid? _folding;
    private bool _keyboardFold;
    public event Action<StoreRelease>? Picked;
    public event Action? ShowAll, More, Primary;
    private readonly List<StoreRow> _order = [];
    public bool Closing => _card.Closing;
    public StoreDeck(Node parent)
    {
        OnlineWidgets.Text(parent, "发行目录", new(550, 117, 820, 55), 37, NetworkDeskChrome.Ink);
        _count = OnlineWidgets.Text(parent, "", new(1390, 115, 465, 44), 20, MenuStyle.Secondary); _count.HorizontalAlignment = HorizontalAlignment.Right;
        _scroll = new ScrollContainer { Name = "StoreCatalog", Position = new(550, 180), Size = new(1306, 714), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list = new VBoxContainer { Name = "StoreRows", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _list.AddThemeConstantOverride("separation", 22);
        _scroll.AddChild(_list); parent.AddChild(_scroll); CatalogueDrum.Attach(_scroll);
        _card = new StoreDetailsView(); _list.AddChild(_card);
        _card.FoldRequested += () => { if (_card.ReleaseId is { } id && _rows.TryGetValue(id, out var row)) Picked?.Invoke(row.Release); };
        _card.Folded += FinishFold;
        _empty = OnlineWidgets.Text(parent, "", new(608, 423, 1180, 78), 31, MenuStyle.Text); _empty.Name = "StoreEmpty";
        _all = OnlineWidgets.Link(parent, "查看全部  →", new(608, 516, 260, 64), 24, () => ShowAll?.Invoke(), MenuStyle.Accent); _all.Name = "StoreShowAll";
        _more = OnlineWidgets.Link(parent, "加载更多  ↓", new(1655, 900, 200, 52), 22, () => More?.Invoke()); _more.Name = "StoreMore";
        _commandMark = new ColorRect { Position = new(550, 981), Size = new(4, 58), Color = NetworkDeskChrome.Purple, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false }; parent.AddChild(_commandMark);
        _caption = OnlineWidgets.Text(parent, "", new(577, 970, 646, 77), 27, MenuStyle.Text); _caption.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _status = OnlineWidgets.Text(parent, "", new(1217, 980, 283, 55), 17, MenuStyle.Secondary); _status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _primary = OnlineWidgets.Link(parent, "下载谱包", new(1511, 975, 324, 67), 25, () => Primary?.Invoke()); _primary.Name = "StorePrimary";
        _primaryTint = NetworkDeskChrome.KeyStyle(_primary, NetworkDeskChrome.Purple, MenuStyle.Ink);
        _primary.Hide(); _primary.Disabled = true; _all.Hide(); _more.Hide(); _scroll.Hide();
    }
    public void SyncRows(IReadOnlyList<StoreRelease> list)
    {
        _order.Clear();
        var ids = list.Select(r => r.Id).ToHashSet();
        foreach (var (id, row) in _rows.ToArray())
            if (!ids.Contains(id)) RemoveRow(id, row);
        // A fresh snapshot may change metadata while retaining the same immutable release identity.
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (_rows.TryGetValue(item.Id, out var stale) && stale.Release != item) RemoveRow(item.Id, stale);
            if (!_rows.TryGetValue(item.Id, out var row))
            { row = new StoreRow(item, r => Picked?.Invoke(r)); _rows.Add(item.Id, row); _list.AddChild(row); }
            _order.Add(row);
            if (row.GetIndex() != i) _list.MoveChild(row, i);
        }
        if (_card.Visible && _card.ReleaseId is { } active && _rows.TryGetValue(active, out var hostRow))
            _list.MoveChild(_card, hostRow.GetIndex());
        _scroll.Visible = list.Count != 0; // Empty must hide the sampler too, not freeze a populated frame.
    }
    private void RemoveRow(Guid id, StoreRow row) { _rows.Remove(id); _list.RemoveChild(row); row.QueueFree(); }
    public void ClearSelection()
    {
        _folding = null; _keyboardFold = false; _card.ClearNow();
        foreach (var row in _rows.Values) { row.Visible = true; row.Select(false); }
        _caption.Text = _status.Text = ""; _primary.Disabled = true; _primary.Visible = _commandMark.Visible = false;
    }
    public void Expand(StoreRelease release, bool interactive)
    {
        if (!_rows.TryGetValue(release.Id, out var row)) return;
        var moveFocus = interactive && row.HasFocus(); _folding = null;
        if (_card.ReleaseId is { } old && _rows.TryGetValue(old, out var previous) && previous != row)
        { previous.Visible = true; previous.Select(false); }
        row.Select(true); row.Visible = false;
        _list.MoveChild(_card, row.GetIndex()); _card.Open(release, moveFocus);
        if (interactive)
            Callable.From(() => { if (GodotObject.IsInstanceValid(_card) && _card.IsInsideTree() && _card.Visible && _card.ReleaseId == release.Id) _scroll.EnsureControlVisible(_card); }).CallDeferred();
    }
    public void Collapse(Guid id)
    {
        if (_card.ReleaseId != id) { ClearSelection(); return; }
        var focus = _scroll.GetViewport().GuiGetFocusOwner();
        _keyboardFold = focus is not null && (_card.IsAncestorOf(focus) || focus == _card);
        _folding = id; _card.Collapse(); _primary.Disabled = true; _primary.Hide(); _caption.Text = _status.Text = "";
    }
    private void FinishFold()
    {
        if (_folding is { } id && _rows.TryGetValue(id, out var row)) { row.Visible = true; row.Select(false); if (_keyboardFold) row.GrabFocus(); }
        _folding = null; _keyboardFold = false;
    }
    public void SetCover(byte[] bytes, string mime) => _card.SetCover(bytes, mime);
    public void ShowListing(string status, string empty, bool escape, bool more, bool loading)
    {
        _count.Text = status; _count.TooltipText = status;
        _empty.Text = empty; _empty.Visible = empty.Length > 0;
        _all.Visible = escape && _empty.Visible; _all.Disabled = !_all.Visible;
        _more.Visible = more; _more.Disabled = loading || !more;
    }
    public void ShowCommand(StoreRelease? r, bool installed, InstallProgress progress, CatalogIdentity? source, bool current)
    {
        _primary.Visible = _commandMark.Visible = current && r is not null;
        if (!_primary.Visible) { _primary.Disabled = true; _caption.Text = _status.Text = ""; return; }
        var release = r!; var supported = release.Display?.ValidationProfile == StoreContract.Profile;
        var busy = progress.Status is "downloading" or "installing"; var ours = progress.Source == source && progress.ReleaseId == release.Id;
        _caption.Text = release.Title; _caption.TooltipText = release.Title;
        _status.Text = installed ? "已安装" : !supported ? "兼容性尚未确认" : ours && progress.Status == "failed" ? StoreScreen.Message(progress.Error ?? "installation_failed") : "";
        _status.TooltipText = _status.Text; _primary.Disabled = !installed && (busy || !supported);
        _primaryTint(installed ? NetworkDeskChrome.Teal : NetworkDeskChrome.Purple);
        _primary.Text = installed ? "前往本地选曲" : ours && busy ? (progress.Status == "downloading" ? "正在下载…" : "正在安装…") : "下载谱包";
        _primary.TooltipText = $"授权说明：{release.Rights}\n发布：{release.Version}";
        _primary.Modulate = _primary.Disabled ? new Color(1, 1, 1, .48f) : Colors.White;
    }
    public IReadOnlyList<StoreRow> OnScreen()
    {
        if (!_scroll.Visible || _order.Count == 0) return [];
        const float pitch = RowHeight + 22;
        // All normal rows are 96 high with 22 separation. At most one expanded row
        // adds 296px, so four rows of leading guard cover every animated geometry.
        int first = Math.Max(0, (int)Math.Floor(_scroll.ScrollVertical / pitch) - 4);
        int last = Math.Min(_order.Count - 1, (int)Math.Ceiling((_scroll.ScrollVertical + _scroll.Size.Y) / pitch) + 1);
        var rect = _scroll.GetGlobalRect(); var rows = new List<StoreRow>(14);
        for (int i = first; i <= last; i++)
            if (_order[i].Visible && _order[i].GetGlobalRect().Intersects(rect)) rows.Add(_order[i]);
        return rows;
    }
}
