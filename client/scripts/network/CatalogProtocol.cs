namespace DynamiteUniverse.Network;

public sealed record CatalogIdentity(Guid ServerEntryId, string Address, Guid InstanceId);
public sealed class CatalogAccess(CatalogIdentity identity, Guid accountId, string token, DateTimeOffset expiresAt)
{
    public CatalogIdentity Identity { get; } = identity;
    public Guid AccountId { get; } = accountId;
    internal string Token { get; } = token;
    internal DateTimeOffset ExpiresAt { get; } = expiresAt;
    public override string ToString() => "CatalogAccess [credentials redacted]";
}
public sealed record StoreRelease(Guid Id, string PackId, string Version, string Title, string Artist,
    long Revision, long Size, string Sha256, string Rights, DateTimeOffset PublishedAt, StoreDisplay? Display);
public sealed record StoreDisplay(string ValidationProfile, StoreChart[] Charts, StoreCover? Cover);
public sealed record StoreChart(string ChartId, string Difficulty, string? DifficultyKey, int? Level, bool Unrated,
    string[] Charters, double BpmMin, double BpmMax);
public sealed record StoreCover(string MediaType, long Size, string Sha256);
public sealed record StoreContentChart(string ChartId, string Digest, string RulesetId);
public sealed record StoreDetails(StoreRelease Release, StoreContentChart[] Charts);

public static class StoreContract
{
    public const string Profile = "desktop-mobile-media-1";
    public static void Validate(StoreRelease r)
    {
        static bool Text(string? s, int max) => s is not null && s.Length <= max && !s.Any(char.IsControl);
        if (r is null || r.Id == Guid.Empty || !Text(r.PackId, 128) || string.IsNullOrWhiteSpace(r.PackId) ||
            !Text(r.Title, 512) || !Text(r.Artist, 512) || !Text(r.Version, 32) || !Text(r.Rights, 2000) ||
            r.Size is < 1 or > 134217728 || !Hash(r.Sha256)) throw new OnlineException("invalid_response");
        if (r.Display is not { } d) return; // Old publications remain visible but cannot be newly installed.
        if (d.Charts is not { Length: > 0 and <= 128 }) throw new OnlineException("invalid_response");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in d.Charts)
            if (c is null || !Text(c.ChartId, 128) || !ids.Add(c.ChartId) || !Text(c.Difficulty, 32) ||
                (c.DifficultyKey is not null && !Text(c.DifficultyKey, 128)) || c.Charters is not { Length: <= 64 } ||
                c.Charters.Any(s => !Text(s, 256)) || !double.IsFinite(c.BpmMin) || !double.IsFinite(c.BpmMax) ||
                c.BpmMin <= 0 || c.BpmMax < c.BpmMin) throw new OnlineException("invalid_response");
        if (d.Cover is { } cover && (cover.MediaType is not ("image/png" or "image/jpeg" or "image/webp") ||
            cover.Size is < 1 or > 8388608 || !Hash(cover.Sha256))) throw new OnlineException("invalid_response");
    }
    public static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
