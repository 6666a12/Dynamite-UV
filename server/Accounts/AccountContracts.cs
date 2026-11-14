using System.Text.RegularExpressions;

namespace DynamiteUniverse.Server.Accounts;

public sealed record Credentials(string? Username, string? Password);
public sealed record AccountProfile(Guid Id, string Username);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, AccountProfile Account);

internal static partial class AccountValidation
{
    [GeneratedRegex("^[a-z0-9_]{3,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
    internal static string Normalize(string? username) => (username ?? "").Trim().ToLowerInvariant();
    internal static bool Valid(Credentials credentials) =>
        credentials.Username is { Length: <= 64 } && UsernamePattern().IsMatch(Normalize(credentials.Username)) &&
        credentials.Password is { Length: >= 12 and <= 128 };
}
