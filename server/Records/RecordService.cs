using System.Text.Json;
using DynamiteUniverse.Server.Persistence;
using NpgsqlTypes;

namespace DynamiteUniverse.Server.Records;

public sealed record PlaySubmission(Guid PlayId, Guid ReleaseId, string ChartId, string Digest, string RulesetId,
    string Mode, bool Mirror, bool Auto, bool Completed, int Score, int Perfect, int Great, int Good, int Miss, int MaxCombo);
public sealed record SubmittedPlay(PlaySubmission Play, DateTimeOffset SubmittedAt, string Verification = "client_reported");
public sealed record SubmittedPlayV2(JsonElement Play, DateTimeOffset SubmittedAt, string Verification = "client_reported");
public sealed record SubmissionResult(int Status, string? Code = null)
{
    public static implicit operator SubmissionResult(int status) => new(status, status switch
    { 400 => "play_not_eligible", 409 => "play_id_conflict", _ => null });
}
public sealed class RecordService(DatabaseConnection database)
{
    public async Task<SubmissionResult> SubmitAsync(Guid account, PlaySubmission p, CancellationToken ct) =>
        await SaveAsync(account, p, JsonSerializer.Serialize(p), ct);
    public async Task<SubmissionResult> SubmitV2Async(Guid account, DynamiteUniverse.Shared.Score.PlayReport p, CancellationToken ct)
    {
        if (p.ContractVersion != 2 || p.VerificationRun || p.UsedSeek || p.DebugRun || p.FinishReason != "completed" ||
            !double.IsFinite(p.RawScore) || !double.IsFinite(p.TheoreticalMax) || !double.IsFinite(p.Clear) ||
            p.TheoreticalMax <= 0 || p.TheoreticalMax > int.MaxValue || p.RawScore < 0 || p.RawScore > p.TheoreticalMax ||
            p.RawScore != Math.Truncate(p.RawScore) || p.TheoreticalMax != Math.Truncate(p.TheoreticalMax) ||
            p.Score != ((long)p.RawScore * 1000000 + (long)p.TheoreticalMax / 2) / (long)p.TheoreticalMax ||
            Math.Abs(p.Clear - p.RawScore / p.TheoreticalMax * 100) > 0.000001 ||
            p.StartedAt < DateTimeOffset.UnixEpoch || p.StartedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
            (p.Mode == "hardcore" && !p.Bleed)) return 400;
        var legacy = new PlaySubmission(p.PlayId, p.ReleaseId, p.ChartId, p.Digest, p.RulesetId, p.Mode, p.Mirror,
            p.Auto, p.Completed, p.Score, p.Perfect, p.Great, p.Good, p.Miss, p.MaxCombo);
        return await SaveAsync(account, legacy, JsonSerializer.Serialize(p), ct);
    }
    private async Task<SubmissionResult> SaveAsync(Guid account, PlaySubmission p, string json, CancellationToken ct)
    {
        if (p.PlayId == Guid.Empty || p.ReleaseId == Guid.Empty || p.ChartId is not { Length: > 0 and <= 128 } ||
            p.Digest is not { Length: 64 } || p.RulesetId is not { Length: > 0 and <= 128 } || p.Mode is not ("standard" or "hardcore") ||
            p.Auto || !p.Completed || p.Score is < 0 or > 1000000 || p.Perfect < 0 || p.Great < 0 || p.Good < 0 || p.Miss < 0 ||
            (long)p.Perfect + p.Great + p.Good + p.Miss is <= 0 or > 10000000 || p.MaxCombo < 0 ||
            p.MaxCombo > (long)p.Perfect + p.Great + p.Good) return 400;
        await using var connection = await database.Source!.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // Exact retries remain successful after withdrawal; they do not create new records.
        await using (var existing = new Npgsql.NpgsqlCommand("SELECT payload=$3::jsonb FROM du.plays WHERE account_id=$1 AND play_id=$2", connection))
        {
            existing.Parameters.AddWithValue(account); existing.Parameters.AddWithValue(p.PlayId); existing.Parameters.AddWithValue(json);
            if (await existing.ExecuteScalarAsync(ct) is bool same) return same ? 200 : 409;
        }
        await using (var identity = new Npgsql.NpgsqlCommand("""
            SELECT c.digest,c.ruleset_id FROM du.release_charts c JOIN du.catalog_releases r ON r.id=c.release_id
            WHERE c.release_id=$1 AND c.chart_id=$2 AND r.published FOR SHARE OF r
            """, connection))
        {
            identity.Parameters.AddWithValue(p.ReleaseId); identity.Parameters.AddWithValue(p.ChartId);
            await using var row = await identity.ExecuteReaderAsync(ct);
            if (!await row.ReadAsync(ct)) return new(422, "release_unavailable");
            if (row.GetString(0) != p.Digest || row.GetString(1) != p.RulesetId) return new(422, "content_mismatch");
        }
        await using var insert = new Npgsql.NpgsqlCommand("""
            INSERT INTO du.plays(account_id,play_id,release_id,chart_id,payload) VALUES ($1,$2,$3,$4,$5)
            ON CONFLICT(account_id,play_id) DO NOTHING
            """, connection);
        insert.Parameters.AddWithValue(account); insert.Parameters.AddWithValue(p.PlayId); insert.Parameters.AddWithValue(p.ReleaseId);
        insert.Parameters.AddWithValue(p.ChartId); insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, json);
        var added = await insert.ExecuteNonQueryAsync(ct);
        await using var compare = new Npgsql.NpgsqlCommand("SELECT payload=$3::jsonb FROM du.plays WHERE account_id=$1 AND play_id=$2", connection);
        compare.Parameters.AddWithValue(account); compare.Parameters.AddWithValue(p.PlayId); compare.Parameters.AddWithValue(json);
        var matches = (bool)(await compare.ExecuteScalarAsync(ct))!;
        await tx.CommitAsync(ct);
        return !matches ? 409 : added == 1 ? 201 : 200;
    }
    public async Task<IReadOnlyList<SubmittedPlayV2>> ListV2Async(Guid account, int offset, CancellationToken ct)
    {
        await using var command = database.Source!.CreateCommand("SELECT payload::text,submitted_at FROM du.plays WHERE account_id=$1 ORDER BY submitted_at DESC,play_id LIMIT 50 OFFSET $2");
        command.Parameters.AddWithValue(account); command.Parameters.AddWithValue(offset);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<SubmittedPlayV2>();
        while (await reader.ReadAsync(ct)) result.Add(new(JsonSerializer.Deserialize<JsonElement>(reader.GetString(0)), reader.GetFieldValue<DateTimeOffset>(1)));
        return result;
    }
    public async Task<IReadOnlyList<SubmittedPlay>> ListAsync(Guid account, int offset, CancellationToken ct)
    {
        await using var command = database.Source!.CreateCommand("SELECT payload::text,submitted_at FROM du.plays WHERE account_id=$1 ORDER BY submitted_at DESC,play_id LIMIT 50 OFFSET $2");
        command.Parameters.AddWithValue(account); command.Parameters.AddWithValue(offset);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<SubmittedPlay>();
        while (await reader.ReadAsync(ct)) result.Add(new(JsonSerializer.Deserialize<PlaySubmission>(reader.GetString(0))!, reader.GetFieldValue<DateTimeOffset>(1)));
        return result;
    }
}
