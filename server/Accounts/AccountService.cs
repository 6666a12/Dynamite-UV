using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using DynamiteUniverse.Server.Persistence;
using Npgsql;

namespace DynamiteUniverse.Server.Accounts;

/// <summary>Parameterized persistence and opaque sessions. Never stores raw passwords or bearer tokens.</summary>
public sealed class AccountService(DatabaseConnection database)
{
    private readonly PasswordHasher<string> _hasher = new(Options.Create(new PasswordHasherOptions
        { IterationCount = 210_000, CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3 }));
    private readonly Lazy<string> _dummyHash = new(() => new PasswordHasher<string>(Options.Create(new PasswordHasherOptions
        { IterationCount = 210_000 })).HashPassword("dummy", Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));
    private NpgsqlDataSource Source => database.Source ?? throw new InvalidOperationException("Database unavailable");

    public async Task<AccountProfile?> RegisterAsync(Credentials credentials, CancellationToken cancellation)
    {
        if (!AccountValidation.Valid(credentials)) throw new ArgumentException("Invalid credentials format");
        var username = AccountValidation.Normalize(credentials.Username);
        var id = Guid.NewGuid();
        var hash = _hasher.HashPassword(username, credentials.Password!);
        await using var command = Source.CreateCommand("""
            INSERT INTO du.accounts(id,username,password_hash) VALUES ($1,$2,$3)
            ON CONFLICT (username) DO NOTHING RETURNING id
            """);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(username);
        command.Parameters.AddWithValue(hash);
        return await command.ExecuteScalarAsync(cancellation) is Guid ? new(id, username) : null;
    }

    public async Task<LoginResponse?> LoginAsync(Credentials credentials, CancellationToken cancellation)
    {
        if (!AccountValidation.Valid(credentials)) return null;
        var username = AccountValidation.Normalize(credentials.Username);
        Guid id = default;
        string? hash = null;
        bool enabled = false;
        await using (var find = Source.CreateCommand("SELECT id,password_hash,enabled FROM du.accounts WHERE username=$1"))
        {
            find.Parameters.AddWithValue(username);
            await using var reader = await find.ExecuteReaderAsync(cancellation);
            if (await reader.ReadAsync(cancellation))
                (id, hash, enabled) = (reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2));
        }
        // Unknown users still pay a password verification cost; never return a distinct unknown-user error.
        var result = _hasher.VerifyHashedPassword(username, hash ?? _dummyHash.Value, credentials.Password!);
        if (hash is null || !enabled || result == PasswordVerificationResult.Failed) return null;
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await using var connection = await Source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        // Serialize logins per account, cap outstanding sessions and recheck enabled after password verification.
        await using (var locked = new NpgsqlCommand("SELECT enabled FROM du.accounts WHERE id=$1 FOR UPDATE", connection))
        {
            locked.Parameters.AddWithValue(id);
            if (await locked.ExecuteScalarAsync(cancellation) is not true) return null;
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            await using var upgrade = new NpgsqlCommand("UPDATE du.accounts SET password_hash=$1 WHERE id=$2", connection);
            upgrade.Parameters.AddWithValue(_hasher.HashPassword(username, credentials.Password!));
            upgrade.Parameters.AddWithValue(id);
            await upgrade.ExecuteNonQueryAsync(cancellation);
        }
        await using (var prune = new NpgsqlCommand("""
            DELETE FROM du.sessions WHERE account_id=$1 AND (expires_at<=now() OR token_hash NOT IN
                (SELECT token_hash FROM du.sessions WHERE account_id=$1 AND expires_at>now()
                 ORDER BY created_at DESC,token_hash LIMIT 9))
            """, connection))
        {
            prune.Parameters.AddWithValue(id);
            await prune.ExecuteNonQueryAsync(cancellation);
        }
        await using var insert = new NpgsqlCommand("""
            INSERT INTO du.sessions(token_hash,account_id,expires_at) VALUES ($1,$2,now()+interval '24 hours')
            RETURNING expires_at
            """, connection);
        insert.Parameters.AddWithValue(HashToken(token));
        insert.Parameters.AddWithValue(id);
        var expires = (DateTime)(await insert.ExecuteScalarAsync(cancellation))!;
        await transaction.CommitAsync(cancellation);
        return new(token, new DateTimeOffset(expires), new(id, username));
    }

    public async Task<AccountProfile?> AuthenticateAsync(string? token, CancellationToken cancellation)
    {
        if (!ValidToken(token)) return null;
        await using var command = Source.CreateCommand("""
            SELECT a.id,a.username FROM du.sessions s JOIN du.accounts a ON a.id=s.account_id
            WHERE s.token_hash=$1 AND s.expires_at>now() AND a.enabled
            """);
        command.Parameters.AddWithValue(HashToken(token!));
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? new(reader.GetGuid(0), reader.GetString(1)) : null;
    }

    public async Task LogoutAsync(string token, CancellationToken cancellation)
    {
        if (!ValidToken(token)) return;
        await using var command = Source.CreateCommand("DELETE FROM du.sessions WHERE token_hash=$1");
        command.Parameters.AddWithValue(HashToken(token));
        await command.ExecuteNonQueryAsync(cancellation);
    }

    private static bool ValidToken(string? token) => token is { Length: 64 } &&
        token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));
}
