using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

internal enum StoreFilter { All, MissingLoaded, Installed }

/// <summary>One captured catalog source; visible releases, page cursor and explicit user selection.
/// No player, credentials, installation commands or rendering side effects.</summary>
internal sealed class StoreBrowseState
{
    private readonly List<StoreRelease> _loaded = [];
    private readonly HashSet<Guid> _loadedIds = [];
    private Dictionary<Guid, InstalledRelease> _receipts = [];
    private Dictionary<Guid, StoreRelease> _visible = [];
    private Guid? _preferred;
    private bool _wantExpanded;
    public StoreFilter Filter { get; private set; }
    public string Query { get; private set; } = "";
    public IReadOnlyList<StoreRelease> Visible { get; private set; } = [];
    public int NextOffset { get; private set; }
    public bool HasMore { get; private set; }
    public bool PageLimitReached { get; private set; }
    public StoreRelease? Expanded => _wantExpanded && _preferred is { } id ? Find(id) : null;
    public StoreRelease? Find(Guid id) => _visible.GetValueOrDefault(id);
    public InstalledRelease? Receipt(Guid id) => _receipts.GetValueOrDefault(id);

    public void Begin(string query, StoreFilter filter, bool resetPage)
    {
        Query = query.Trim(); Filter = filter;
        if (resetPage) { _loaded.Clear(); _loadedIds.Clear(); NextOffset = 0; HasMore = PageLimitReached = false; }
        Recalculate();
    }
    public void AcceptPage(int requestedOffset, IReadOnlyList<StoreRelease> page)
    {
        if (requestedOffset != NextOffset) return;
        foreach (var r in page) if (_loadedIds.Add(r.Id)) _loaded.Add(r);
        NextOffset += page.Count; // offset counts server rows, not filtered or deduplicated rows.
        HasMore = page.Count == 50 && NextOffset <= 10000;
        PageLimitReached = page.Count == 50 && NextOffset > 10000;
        Recalculate();
    }
    public void SetReceipts(CatalogIdentity source, IEnumerable<InstalledRelease> receipts)
    {
        _receipts = receipts.Where(r => r.Source == source).GroupBy(r => r.Release.Id).ToDictionary(g => g.Key, g => g.First());
        Recalculate();
    }
    public bool Choose(Guid id, bool toggle = true)
    {
        if (!_visible.ContainsKey(id)) return false;
        _wantExpanded = !toggle || _preferred != id || !_wantExpanded;
        _preferred = id;
        return true;
    }
    public void Clear()
    {
        _preferred = null; _wantExpanded = false; _receipts.Clear(); Begin("", StoreFilter.All, true);
    }
    private void Recalculate()
    {
        IEnumerable<StoreRelease> pool = Filter switch
        {
            StoreFilter.MissingLoaded => _loaded.Where(r => !_receipts.ContainsKey(r.Id)),
            StoreFilter.Installed => _receipts.Values.Select(i => i.Release).Where(MatchesLocal).OrderByDescending(r => r.PublishedAt).ThenBy(r => r.Id),
            _ => _loaded,
        };
        Visible = pool.ToArray(); _visible = Visible.ToDictionary(r => r.Id);
    }
    private bool MatchesLocal(StoreRelease r) => string.Join(" ", new[] { r.Title, r.Artist }
        .Concat(r.Display?.Charts.SelectMany(c => c.Charters) ?? [])).Contains(Query, StringComparison.OrdinalIgnoreCase);
}
