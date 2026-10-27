namespace DynamiteUniverse.Ui;

public sealed record SongBrowseChart(string Id, string Charter);
public sealed record SongBrowseEntry(string Id, string Title, string Artist, IReadOnlyList<SongBrowseChart> Charts);

/// <summary>
/// Page-local browsing state. Filtering only computes visible rows; the host collapses an old
/// card before selecting a replacement. Browsing never commits the gameplay selection or loads audio.
/// </summary>
public sealed class SongBrowseState(IReadOnlyList<SongBrowseEntry> catalog)
{
    private readonly List<int> _visible = new();
    private readonly bool[] _matches = new bool[catalog.Count];
    public int PackIndex { get; private set; } = -1;
    public int DifficultyIndex { get; private set; }
    public IReadOnlyList<int> VisibleIndices => _visible;

    public void Filter(string query)
    {
        query = query.Trim();
        _visible.Clear();
        for (var i = 0; i < catalog.Count; i++)
        {
            var pack = catalog[i];
            var match = query.Length == 0 || pack.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                pack.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                pack.Charts.Any(chart => chart.Charter.Contains(query, StringComparison.OrdinalIgnoreCase));
            _matches[i] = match;
            if (match)
                _visible.Add(i);
        }
    }

    public bool IsVisible(int index) => _matches[index];

    public void SelectPack(int index)
    {
        PackIndex = index;
        DifficultyIndex = 0;
    }

    public void ClearSelection() => PackIndex = -1;

    public void Restore(string packId, string chartId)
    {
        for (var i = 0; i < catalog.Count; i++)
        {
            if (!StringComparer.Ordinal.Equals(catalog[i].Id, packId)) continue;
            PackIndex = i;
            var charts = catalog[i].Charts;
            for (var j = 0; j < charts.Count; j++)
                if (StringComparer.Ordinal.Equals(charts[j].Id, chartId))
                {
                    DifficultyIndex = j;
                    break;
                }
            return;
        }
    }

    public void NextDifficulty(int chartCount)
    {
        if (chartCount > 0) DifficultyIndex = (DifficultyIndex + 1) % chartCount;
    }

    public void ClampDifficulty(int chartCount)
    {
        if (chartCount > 0) DifficultyIndex = Math.Clamp(DifficultyIndex, 0, chartCount - 1);
    }
}
