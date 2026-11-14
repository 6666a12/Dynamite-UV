using System.Net;

namespace DynamiteUniverse.Network;

public sealed record ServerAddress
{
    public Uri BaseUri { get; }
    private ServerAddress(Uri uri) => BaseUri = uri;
    public static ServerAddress Parse(string text, bool allowLocalHttp = false)
    {
        if (text.Length > 2048 || text.Contains('\\') || text.Any(char.IsControl) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new OnlineException("invalid_address");
        var host = uri.IdnHost.Trim('[', ']');
        var local = host == "localhost" || IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
        if (uri.Scheme != "https" && !(allowLocalHttp && uri.Scheme == "http" && local))
            throw new OnlineException("https_required");
        BaseUriCheck(uri);
        return new ServerAddress(new UriBuilder(uri.Scheme, uri.IdnHost, uri.IsDefaultPort ? -1 : uri.Port, "/").Uri);
    }
    private static void BaseUriCheck(Uri uri)
    {
        if (uri.Host.Length == 0) throw new OnlineException("invalid_address");
    }
}

public sealed record OnlineCapabilities(bool Accounts, bool Catalog, bool PersonalRecords);
public sealed record OnlineContracts(int Accounts, int Catalog, int PersonalRecords,
    string? PackageFormat = null, string? DigestAlgorithm = null);
public sealed record OnlineDescriptor(string Protocol, int ProtocolVersion, Guid InstanceId, string Name,
    string ImplementationStage, OnlineCapabilities Capabilities, OnlineContracts? Contracts);
public sealed record OnlineAccount(Guid Id, string Username);
public sealed record OnlineLogin(string AccessToken, DateTimeOffset ExpiresAt, OnlineAccount Account)
{
    public override string ToString() => "OnlineLogin [credentials redacted]";
}
public sealed class OnlineException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
public interface IAccountTransport
{
    Task<OnlineDescriptor> DiscoverAsync(ServerAddress server, CancellationToken ct);
    Task<OnlineAccount> RegisterAsync(ServerAddress server, string username, string password, CancellationToken ct);
    Task<OnlineLogin> LoginAsync(ServerAddress server, string username, string password, CancellationToken ct);
    Task<OnlineAccount> MeAsync(ServerAddress server, string token, CancellationToken ct);
    Task LogoutAsync(ServerAddress server, string token, CancellationToken ct);
}
