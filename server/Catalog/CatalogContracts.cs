namespace DynamiteUniverse.Server.Catalog;

public sealed class StorageOptions
{
    public string CatalogRoot { get; init; } = Path.Combine(AppContext.BaseDirectory, "content", "catalog");
}
public sealed record CatalogRelease(Guid Id, string PackId, string Version, string Title, string Artist,
    long Revision, long Size, string Sha256, string Rights, DateTimeOffset PublishedAt, CatalogDisplay? Display = null);
public sealed record CatalogDisplay(string ValidationProfile, IReadOnlyList<ChartDisplay> Charts, CoverDisplay? Cover);
public sealed record ChartDisplay(string ChartId, string Difficulty, string? DifficultyKey, int? Level, bool Unrated,
    IReadOnlyList<string> Charters, double BpmMin, double BpmMax);
public sealed record CoverDisplay(string MediaType, long Size, string Sha256);
public sealed record ReleaseDetails(CatalogRelease Release, IReadOnlyList<ReleaseChart> Charts);
public sealed record AdminRelease(CatalogRelease Release, bool Published);
public sealed record Withdrawal(string? Reason);
