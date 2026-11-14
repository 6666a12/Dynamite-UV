using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace DynamiteUniverse.Server.Persistence;

/// <summary>Explicit, forward-only transactional migrations; HTTP startup never changes the schema.</summary>
public sealed class DatabaseMigrator(DatabaseConnection database)
{
    private static readonly (int Version, string Sql, string Hash)[] Migrations = LoadMigrations();

    private static (int, string, string)[] LoadMigrations() => typeof(DatabaseMigrator).Assembly
        .GetManifestResourceNames().Where(n => n.Contains(".Persistence.Migrations.") && n.EndsWith(".sql"))
        .Order(StringComparer.Ordinal).Select(name =>
        {
            using var stream = typeof(DatabaseMigrator).Assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var sql = reader.ReadToEnd().Replace("\r\n", "\n");
            var version = int.Parse(name.Split('.')[^2].Split('_')[0]);
            return (version, sql, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
        }).ToArray();

    public async Task MigrateAsync(Guid instanceId, CancellationToken cancellation)
    {
        if (instanceId == Guid.Empty) throw new InvalidOperationException("An instance identity is required.");
        var source = database.Source ?? throw new InvalidOperationException("PostgreSQL is not configured.");
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        // Serialize concurrent migrators before creating/checking the journal.
        await Execute("SET LOCAL lock_timeout = '10s'; SELECT pg_advisory_xact_lock(7325190361123);", connection, cancellation);
        await Execute("""
            CREATE SCHEMA IF NOT EXISTS du;
            CREATE TABLE IF NOT EXISTS du.schema_migrations (
                version integer PRIMARY KEY, sha256 text NOT NULL,
                applied_at timestamptz NOT NULL DEFAULT now()
            );
            """, connection, cancellation);
        var applied = await ReadApplied(connection, cancellation);
        ValidateApplied(applied);
        foreach (var migration in Migrations.Where(m => !applied.ContainsKey(m.Version)))
        {
            await Execute(migration.Sql, connection, cancellation);
            await using var record = new NpgsqlCommand("INSERT INTO du.schema_migrations(version,sha256) VALUES ($1,$2)", connection);
            record.Parameters.AddWithValue(migration.Version);
            record.Parameters.AddWithValue(migration.Hash);
            await record.ExecuteNonQueryAsync(cancellation);
        }
        await using var bind = new NpgsqlCommand(
            "INSERT INTO du.instance_identity(singleton,instance_id) VALUES (true,$1) ON CONFLICT DO NOTHING", connection);
        bind.Parameters.AddWithValue(instanceId);
        await bind.ExecuteNonQueryAsync(cancellation);
        await using var identity = new NpgsqlCommand("SELECT instance_id FROM du.instance_identity WHERE singleton", connection);
        if ((Guid)(await identity.ExecuteScalarAsync(cancellation))! != instanceId)
            throw new InvalidOperationException("Database belongs to another instance; migration refused.");
        await transaction.CommitAsync(cancellation);
    }

    public async Task<string> StatusAsync(Guid instanceId, CancellationToken cancellation)
    {
        if (database.Source is null) return "not_configured";
        try
        {
            await using var connection = await database.Source.OpenConnectionAsync(cancellation);
            var applied = await ReadApplied(connection, cancellation);
            ValidateApplied(applied);
            if (applied.Count != Migrations.Length) return "migration_required";
            await using var identity = new NpgsqlCommand("SELECT instance_id FROM du.instance_identity WHERE singleton", connection);
            return await identity.ExecuteScalarAsync(cancellation) is Guid id && id == instanceId ? "ready" : "identity_mismatch";
        }
        catch (PostgresException ex) when (ex.SqlState is "42P01" or "3F000") { return "migration_required"; }
        catch (InvalidOperationException) { return "schema_mismatch"; }
        catch (NpgsqlException) { return "unavailable"; }
        catch (TimeoutException) { return "unavailable"; }
    }

    private static void ValidateApplied(Dictionary<int, string> applied)
    {
        foreach (var (version, hash) in applied)
            if (!Migrations.Any(m => m.Version == version && m.Hash == hash))
                throw new InvalidOperationException("Database migration history differs from this server version.");
        var count = applied.Count;
        if (Migrations.Take(count).Any(m => !applied.ContainsKey(m.Version)))
            throw new InvalidOperationException("Database migration history has gaps.");
    }

    private static async Task<Dictionary<int, string>> ReadApplied(NpgsqlConnection connection, CancellationToken cancellation)
    {
        await using var command = new NpgsqlCommand("SELECT version,sha256 FROM du.schema_migrations ORDER BY version", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var result = new Dictionary<int, string>();
        while (await reader.ReadAsync(cancellation)) result.Add(reader.GetInt32(0), reader.GetString(1));
        return result;
    }

    private static async Task Execute(string sql, NpgsqlConnection connection, CancellationToken cancellation)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellation);
    }
}
