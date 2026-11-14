using DynamiteUniverse.Server.Persistence;

namespace DynamiteUniverse.Server.Accounts;

internal static class AdminProvisioning
{
    internal static async Task PromoteAsync(DatabaseConnection database, string username, CancellationToken ct)
    {
        await using var connection = await database.Source!.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var update = new Npgsql.NpgsqlCommand("UPDATE du.accounts SET role='admin' WHERE username=$1 AND enabled RETURNING id", connection);
        update.Parameters.AddWithValue(AccountValidation.Normalize(username));
        if (await update.ExecuteScalarAsync(ct) is not Guid id) throw new InvalidOperationException("No enabled account");
        await using var audit = new Npgsql.NpgsqlCommand("INSERT INTO du.admin_audit(actor_id,action,target,reason) VALUES ($1,'grant_admin',$2,'local operator command')", connection);
        audit.Parameters.AddWithValue(id); audit.Parameters.AddWithValue(id.ToString());
        await audit.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
}
