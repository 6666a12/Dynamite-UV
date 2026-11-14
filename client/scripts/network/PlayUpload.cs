using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Network;

public sealed record PlayOrigin(CatalogIdentity Server, Guid AccountId, string? Username = null);
public sealed record UploadStatus(string Code, DateTimeOffset? RetryAfter = null);
public interface IPlayTransport
{
    Task<UploadStatus> SendAsync(CatalogAccess access, PlayReport report, CancellationToken ct);
}
public sealed class PlayTransport : IPlayTransport
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }) { Timeout = TimeSpan.FromSeconds(15) };
    public async Task<UploadStatus> SendAsync(CatalogAccess access, PlayReport report, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(access.Identity.Address + "api/v1/me/plays/v2"));
            request.Headers.Authorization = new("Bearer", access.Token); request.Content = JsonContent.Create(report);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
                await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var output = new MemoryStream(); var buffer = new byte[1024]; int count;
                while ((count = await body.ReadAsync(buffer, deadline.Token)) > 0)
                { if (output.Length + count > 4096) return new("retryable"); output.Write(buffer, 0, count); }
                using var json = JsonDocument.Parse(output.ToArray());
                return json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("playId", out var id) && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var guid) && guid == report.PlayId
                    ? new("uploaded") : new("retryable");
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new("retryable", response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60)));
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(10));
                    await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
                    var buffer = new byte[4097]; var count = await body.ReadAtLeastAsync(buffer, buffer.Length, false, deadline.Token);
                    if (count <= 4096)
                    {
                        using var error = JsonDocument.Parse(buffer.AsMemory(0, count));
                        if (error.RootElement.ValueKind == JsonValueKind.Object && error.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String &&
                            code.GetString() is "release_unavailable" or "content_mismatch") return new(code.GetString()!);
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or OperationCanceledException) { }
                return new("not_accepted");
            }
            return new(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "reauthenticate", HttpStatusCode.Forbidden => "forbidden",
                HttpStatusCode.Conflict => "conflict", HttpStatusCode.UnprocessableEntity => "not_accepted",
                HttpStatusCode.BadRequest => "ineligible", _ => (int)response.StatusCode >= 500 ? "retryable" : "rejected",
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException)
        { return new("retryable"); }
    }
}

/// <summary>One result context. Never rebuilds the payload or changes its account/server on retry.</summary>
public sealed class PlayUpload(PlayOrigin? origin, PlayReport? report, string initial, IPlayTransport transport) : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private UploadStatus _state = new(initial);
    private bool _attempted, _disposed;
    private string? _rejectedToken;
    public PlayOrigin? Origin => origin;
    public UploadStatus State { get { lock (_gate) return _state; } }
    public async Task AttemptAsync(CatalogAccess? access, bool automatic = false)
    {
        lock (_gate)
        {
            if (_disposed || report is null || origin is null || _state.Code is "uploading" or "uploaded" or "forbidden" or "conflict" or "not_accepted" or "release_unavailable" or "content_mismatch" or "ineligible" or "rejected" ||
                (_state.RetryAfter is { } after && after > DateTimeOffset.UtcNow) || (automatic && _attempted)) return;
            _attempted = true;
            if (access is null || access.Identity != origin.Server || access.AccountId != origin.AccountId ||
                access.Token == _rejectedToken || access.ExpiresAt <= DateTimeOffset.UtcNow)
            { _state = new("reauthenticate"); return; }
            _state = new("uploading");
        }
        var result = await transport.SendAsync(access, report, _lifetime.Token).ConfigureAwait(false);
        lock (_gate)
        {
            if (!_disposed)
            {
                if (result.Code == "reauthenticate") _rejectedToken = access.Token;
                _state = result;
            }
        }
    }
    public void Dispose() { lock (_gate) { _disposed = true; _lifetime.Cancel(); } }
}
