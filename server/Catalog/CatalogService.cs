using System.Security.Cryptography;
using System.Text.Json;
using NpgsqlTypes;
using DynamiteUniverse.Server.Persistence;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DynamiteUniverse.Server.Catalog;

public sealed class CatalogService(DatabaseConnection database, IOptions<StorageOptions> storage)
{
    private readonly string _root = Path.GetFullPath(storage.Value.CatalogRoot);
    private readonly SemaphoreSlim _publish = new(1, 1);
    private NpgsqlDataSource Source => database.Source ?? throw new InvalidOperationException("Database unavailable");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Columns = "id,pack_id,version,title,artist,revision,byte_size,sha256,rights,published_at,display_metadata";
    private static CatalogRelease Read(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2),
        r.GetString(3), r.GetString(4), r.GetInt64(5), r.GetInt64(6), r.GetString(7), r.GetString(8), r.GetFieldValue<DateTimeOffset>(9),
        r.IsDBNull(10) ? null : JsonSerializer.Deserialize<CatalogDisplay>(r.GetString(10), Json));

    public async Task<IReadOnlyList<CatalogRelease>> ListAsync(string? query, int offset, CancellationToken ct)
    {
        if (query?.Length > 80 || offset is < 0 or > 10000) throw new ArgumentException();
        await using var command = Source.CreateCommand($"""
            SELECT {Columns} FROM (SELECT DISTINCT ON(pack_id) * FROM du.catalog_releases
            WHERE published ORDER BY pack_id,published_at DESC,id DESC) r
            WHERE strpos(lower(title || ' ' || artist || ' ' || pack_id || ' ' || coalesce(display_metadata->'charts','[]'::jsonb)::text),lower($1))>0
            ORDER BY published_at DESC,id DESC LIMIT 50 OFFSET $2
            """);
        command.Parameters.AddWithValue(query ?? ""); command.Parameters.AddWithValue(offset);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<CatalogRelease>();
        while (await reader.ReadAsync(ct)) result.Add(Read(reader));
        return result;
    }
    public async Task<IReadOnlyList<AdminRelease>> AdminListAsync(int offset, CancellationToken ct)
    {
        if (offset is < 0 or > 10000) throw new ArgumentException();
        await using var command = Source.CreateCommand($"SELECT {Columns},published FROM du.catalog_releases ORDER BY published_at DESC,id DESC LIMIT 50 OFFSET $1");
        command.Parameters.AddWithValue(offset);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<AdminRelease>();
        while (await reader.ReadAsync(ct)) result.Add(new(Read(reader), reader.GetBoolean(11)));
        return result;
    }
    public async Task<ReleaseDetails?> GetAsync(Guid id, CancellationToken ct)
    {
        CatalogRelease release;
        await using (var command = Source.CreateCommand($"SELECT {Columns} FROM du.catalog_releases WHERE id=$1 AND published"))
        {
            command.Parameters.AddWithValue(id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            release = Read(reader);
        }
        await using var charts = Source.CreateCommand("SELECT chart_id,digest,ruleset_id FROM du.release_charts WHERE release_id=$1 ORDER BY chart_id");
        charts.Parameters.AddWithValue(id);
        await using var rows = await charts.ExecuteReaderAsync(ct);
        var entries = new List<ReleaseChart>();
        while (await rows.ReadAsync(ct)) entries.Add(new(rows.GetString(0), rows.GetString(1), rows.GetString(2)));
        return new(release, entries);
    }
    public async Task<CatalogRelease> PublishAsync(Guid actor, string version, string rights, Stream body, CancellationToken ct)
    {
        if (version.Length is < 1 or > 32 || version.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')) ||
            string.IsNullOrWhiteSpace(rights) || rights.Length > 2000) throw new ArgumentException();
        if (!await _publish.WaitAsync(0, ct)) throw new InvalidOperationException("publication_busy");
        var stage = Path.Combine(_root, ".staging", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stage);
            var archive = Path.Combine(stage, "package.zip");
            long size = 0;
            await using (var output = new FileStream(archive, FileMode.CreateNew))
            {
                var buffer = new byte[65536]; int read;
                while ((read = await body.ReadAsync(buffer, ct)) != 0)
                {
                    if ((size += read) > PackageInspector.MaxArchive) throw new InvalidDataException("Archive too large");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            var validated = PackageInspector.Inspect(archive, stage);
            var id = Guid.NewGuid();
            string hash;
            await using (var file = File.OpenRead(archive)) hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
            await using var connection = await Source.OpenConnectionAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(ct);
            await using var insert = new NpgsqlCommand($"""
                INSERT INTO du.catalog_releases(id,pack_id,version,title,artist,revision,storage_name,sha256,byte_size,rights,display_metadata)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11) RETURNING {Columns}
                """, connection);
            var pack = validated.Pack;
            foreach (var value in new object[] { id, pack.Id, version, pack.Title, pack.Artist, pack.Revision, id+".zip", hash, size, rights })
                insert.Parameters.AddWithValue(value);
            insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(validated.Display, Json));
            CatalogRelease release;
            await using (var row = await insert.ExecuteReaderAsync(ct)) { await row.ReadAsync(ct); release = Read(row); }
            foreach (var chart in validated.Charts)
            {
                await using var add = new NpgsqlCommand("INSERT INTO du.release_charts VALUES ($1,$2,$3,$4)", connection);
                foreach (var value in new object[] { id, chart.ChartId, chart.Digest, chart.RulesetId }) add.Parameters.AddWithValue(value);
                await add.ExecuteNonQueryAsync(ct);
            }
            await Audit(connection, actor, "publish", id, rights, ct);
            if (validated.CoverPath is not null) File.Copy(validated.CoverPath, Path.Combine(_root, id + ".cover"));
            File.Move(archive, Path.Combine(_root, id + ".zip"));
            // A commit with a lost response may have succeeded: never delete the completed file on uncertainty.
            await tx.CommitAsync(ct);
            return release;
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            catch (IOException) { /* Inaccessible staging is not published; operator may clean it later. */ }
            catch (UnauthorizedAccessException) { }
            finally { _publish.Release(); }
        }
    }
    public async Task<bool> WithdrawAsync(Guid actor, Guid id, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new ArgumentException();
        await using var connection = await Source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("UPDATE du.catalog_releases SET published=false WHERE id=$1 AND published", connection);
        command.Parameters.AddWithValue(id);
        if (await command.ExecuteNonQueryAsync(ct) == 0) return false;
        await Audit(connection, actor, "withdraw", id, reason, ct);
        await tx.CommitAsync(ct); return true;
    }
    public async Task<Stream?> CoverAsync(CatalogRelease release, CancellationToken ct)
    {
        if (release.Display?.Cover is not { } cover) return null;
        return await OpenVerified(Path.Combine(_root, release.Id + ".cover"), cover.Size, cover.Sha256, ct);
    }
    public Task<Stream?> DownloadAsync(CatalogRelease release, CancellationToken ct) =>
        OpenVerified(Path.Combine(_root, release.Id + ".zip"), release.Size, release.Sha256, ct);
    private static async Task<Stream?> OpenVerified(string path, long size, string hash, CancellationToken ct)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length != size || Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct)) != hash)
            { await stream.DisposeAsync(); return null; }
            stream.Position = 0; return stream;
        }
        catch { await stream.DisposeAsync(); throw; }
    }
    private static async Task Audit(NpgsqlConnection c, Guid actor, string action, Guid id, string reason, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("INSERT INTO du.admin_audit(actor_id,action,target,reason) VALUES ($1,$2,$3,$4)", c);
        foreach (var value in new object[] { actor, action, id.ToString(), reason }) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
