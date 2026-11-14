using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DynamiteUniverse.Network;

/// <summary>Root-relative requests only, no redirects/cookies/default credentials, bounded responses.</summary>
public sealed class AccountTransport : IAccountTransport, IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    public AccountTransport() : this(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
        AutomaticDecompression = DecompressionMethods.None,
    }) { }
    public AccountTransport(HttpMessageHandler handler) => _http = new(handler) { Timeout = TimeSpan.FromSeconds(15) };
    public Task<OnlineDescriptor> DiscoverAsync(ServerAddress server, CancellationToken ct) =>
        Send<OnlineDescriptor>(server, HttpMethod.Get, ".well-known/dynamite-universe", null, null, ct);
    public Task<OnlineAccount> RegisterAsync(ServerAddress server, string username, string password, CancellationToken ct) =>
        Send<OnlineAccount>(server, HttpMethod.Post, "api/v1/accounts/register", new { username, password }, null, ct);
    public Task<OnlineLogin> LoginAsync(ServerAddress server, string username, string password, CancellationToken ct) =>
        Send<OnlineLogin>(server, HttpMethod.Post, "api/v1/accounts/login", new { username, password }, null, ct);
    public Task<OnlineAccount> MeAsync(ServerAddress server, string token, CancellationToken ct) =>
        Send<OnlineAccount>(server, HttpMethod.Get, "api/v1/accounts/me", null, token, ct);
    public async Task LogoutAsync(ServerAddress server, string token, CancellationToken ct) =>
        _ = await Send<object>(server, HttpMethod.Post, "api/v1/accounts/logout", null, token, ct);

    private async Task<T> Send<T>(ServerAddress server, HttpMethod method, string route, object? body, string? token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(server.BaseUri, route));
            if (body is not null) request.Content = JsonContent.Create(body);
            if (token is not null) request.Headers.Authorization = new("Bearer", token);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400) throw new OnlineException("redirect_refused");
            if (!response.IsSuccessStatusCode)
                throw new OnlineException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "unauthorized",
                    HttpStatusCode.Forbidden => "forbidden",
                    HttpStatusCode.Conflict => "username_unavailable",
                    HttpStatusCode.TooManyRequests => "rate_limited",
                    HttpStatusCode.BadRequest => "invalid_input",
                    HttpStatusCode.ServiceUnavailable => "service_unavailable",
                    _ => "request_failed",
                });
            if (response.StatusCode == HttpStatusCode.NoContent && typeof(T) == typeof(object)) return default!;
            if (response.Content.Headers.ContentLength > 64 * 1024) throw new OnlineException("invalid_response");
            if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new OnlineException("invalid_response");
            // HttpClient.Timeout stops at headers with ResponseHeadersRead; body needs its own deadline.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[4096]; int read;
            while ((read = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
            {
                if (bytes.Length + read > 64 * 1024) throw new OnlineException("invalid_response");
                bytes.Write(buffer, 0, read);
            }
            return JsonSerializer.Deserialize<T>(bytes.ToArray(), Json) ?? throw new OnlineException("invalid_response");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new OnlineException("timeout"); }
        catch (HttpRequestException) { throw new OnlineException("connection_failed"); }
        catch (JsonException) { throw new OnlineException("invalid_response"); }
        catch (IOException) { throw new OnlineException("connection_failed"); }
    }
    public void Dispose() => _http.Dispose();
}
