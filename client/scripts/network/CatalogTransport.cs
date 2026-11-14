using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace DynamiteUniverse.Network;

/// <summary>Fixed same-origin routes, bounded JSON/images and streaming archives; no automatic redirects.</summary>
public sealed class CatalogTransport : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 24 };
    public CatalogTransport() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false,
        UseDefaultCredentials = false, AutomaticDecompression = DecompressionMethods.None }) { }
    public CatalogTransport(HttpMessageHandler handler) => _http = new(handler) { Timeout = TimeSpan.FromSeconds(15) };
    public async Task<StoreRelease[]> ListAsync(CatalogAccess access, string query, int offset, CancellationToken ct)
    {
        if (query.Length > 80 || offset is < 0 or > 10000) throw new OnlineException("invalid_input");
        var rows = await JsonAsync<StoreRelease[]>(access, $"api/v1/catalog/packs?q={Uri.EscapeDataString(query)}&offset={offset}", ct);
        if (rows.Length > 50) throw new OnlineException("invalid_response");
        foreach (var row in rows) StoreContract.Validate(row);
        return rows;
    }
    public async Task<StoreDetails> DetailsAsync(CatalogAccess access, Guid id, CancellationToken ct)
    {
        var details = await JsonAsync<StoreDetails>(access, $"api/v1/catalog/releases/{id:D}", ct);
        StoreContract.Validate(details.Release);
        if (details.Release.Id != id || details.Charts is not { Length: > 0 and <= 128 } ||
            details.Charts.Any(c => c is null || string.IsNullOrEmpty(c.ChartId) || !StoreContract.Hash(c.Digest) ||
                c.RulesetId != "dynamite-uv-ruleset-2.0")) throw new OnlineException("invalid_response");
        return details;
    }
    private async Task<T> JsonAsync<T>(CatalogAccess access, string route, CancellationToken ct)
    {
        using var output = new MemoryStream();
        await Transfer(access, route, "application/json", output, 1024 * 1024, null, null, null, ct);
        try { return JsonSerializer.Deserialize<T>(output.ToArray(), Json) ?? throw new OnlineException("invalid_response"); }
        catch (JsonException) { throw new OnlineException("invalid_response"); }
    }
    public async Task<byte[]> CoverAsync(CatalogAccess access, StoreRelease release, CancellationToken ct)
    {
        var cover = release.Display?.Cover ?? throw new OnlineException("not_found");
        using var output = new MemoryStream();
        await Transfer(access, $"api/v1/catalog/releases/{release.Id:D}/cover", cover.MediaType, output,
            8 * 1024 * 1024, cover.Size, cover.Sha256, null, ct);
        return output.ToArray();
    }
    public async Task DownloadAsync(CatalogAccess access, StoreRelease release, string destination,
        Action<long>? progress, CancellationToken ct)
    {
        StoreContract.Validate(release);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await Transfer(access, $"api/v1/catalog/releases/{release.Id:D}/download", "application/zip", output,
            128L * 1024 * 1024, release.Size, release.Sha256, progress, ct);
    }
    private async Task Transfer(CatalogAccess access, string route, string mediaType, Stream output, long limit,
        long? expected, string? hash, Action<long>? progress, CancellationToken ct)
    {
        try
        {
            if (access.ExpiresAt <= DateTimeOffset.UtcNow) throw new OnlineException("unauthorized");
            var server = ServerAddress.Parse(access.Identity.Address, true);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server.BaseUri, route));
            request.Headers.Authorization = new("Bearer", access.Token);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400) throw new OnlineException("redirect_refused");
            if (response.StatusCode != HttpStatusCode.OK) throw new OnlineException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "unauthorized", HttpStatusCode.Forbidden => "forbidden",
                HttpStatusCode.NotFound => "not_found", HttpStatusCode.TooManyRequests => "rate_limited",
                _ => "service_unavailable",
            });
            if (response.Content.Headers.ContentType?.MediaType != mediaType || response.Content.Headers.ContentEncoding.Count > 0 ||
                response.Content.Headers.ContentLength > limit ||
                (expected is not null && response.Content.Headers.ContentLength is { } length && length != expected))
                throw new OnlineException("invalid_response");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536]; long total = 0; int count;
            while (true)
            {
                try { count = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false); }
                catch (IOException) { throw new OnlineException("connection_failed"); }
                if (count == 0) break;
                if ((total += count) > limit || total > (expected ?? limit)) throw new OnlineException("invalid_response");
                digest.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
                progress?.Invoke(total);
            }
            if ((expected is not null && total != expected) || (hash is not null &&
                !Convert.ToHexString(digest.GetHashAndReset()).Equals(hash, StringComparison.OrdinalIgnoreCase)))
                throw new OnlineException("integrity_failed");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new OnlineException("timeout"); }
        catch (HttpRequestException) { throw new OnlineException("connection_failed"); }
    }
    public void Dispose() => _http.Dispose();
}
