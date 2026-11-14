using DynamiteUniverse.Server.Persistence;
using Microsoft.Extensions.Configuration;
using Npgsql;

internal static class PostgresTests
{
    public static async Task<int> Run()
    {
        var connectionString = Environment.GetEnvironmentVariable("DYNA_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("PostgreSQL test requires DYNA_TEST_POSTGRES (a local role with CREATEDB). No test ran.");
            return 1;
        }
        var name = "du_test_" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var target = new NpgsqlConnectionStringBuilder(connectionString);
            if (target.Host is not ("127.0.0.1" or "localhost" or "::1"))
                throw new InvalidOperationException("Integration tests require a loopback PostgreSQL instance.");
            await using var admin = NpgsqlDataSource.Create(connectionString);
            await using (var create = admin.CreateCommand($"CREATE DATABASE {name}"))
                await create.ExecuteNonQueryAsync(timeout.Token);
            try
            {
                var settings = new NpgsqlConnectionStringBuilder(connectionString) { Database = name };
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    { ["ConnectionStrings:Postgres"] = settings.ConnectionString }).Build();
                await using var database = new DatabaseConnection(configuration);
                var migrator = new DatabaseMigrator(database);
                var id = Guid.NewGuid();
                Require(await migrator.StatusAsync(id, timeout.Token) == "migration_required", "Empty database needs migration");
                await Task.WhenAll(migrator.MigrateAsync(id, timeout.Token), migrator.MigrateAsync(id, timeout.Token));
                Require(await migrator.StatusAsync(id, timeout.Token) == "ready", "Concurrent migrations complete once");
                await migrator.MigrateAsync(id, timeout.Token);
                Require(await migrator.StatusAsync(id, timeout.Token) == "ready", "Repeated migration is idempotent");
                try { await migrator.MigrateAsync(Guid.NewGuid(), timeout.Token); throw new Exception("Identity mismatch accepted"); }
                catch (InvalidOperationException) { Console.WriteLine("PASS Different instance cannot adopt the database"); }
                Require(await migrator.StatusAsync(id, timeout.Token) == "ready", "Identity rejection preserves original data");
                await using (var connection = await database.Source!.OpenConnectionAsync(timeout.Token))
                await using (var transaction = await connection.BeginTransactionAsync(timeout.Token))
                {
                    const string payload = "'; DROP TABLE du.instance_identity; --";
                    await using var parameter = new NpgsqlCommand("UPDATE du.schema_migrations SET sha256=$1 RETURNING sha256", connection);
                    parameter.Parameters.AddWithValue(payload);
                    Require((string)(await parameter.ExecuteScalarAsync(timeout.Token))! == payload,
                        "SQL-looking parameter remains literal text");
                    await using var intact = new NpgsqlCommand("SELECT count(*) FROM du.instance_identity", connection);
                    Require((long)(await intact.ExecuteScalarAsync(timeout.Token))! == 1,
                        "Parameterized input cannot execute injected statements");
                    await transaction.RollbackAsync(timeout.Token);
                }
                await MultiServerTests.Run(connectionString, timeout.Token);
                await AccountTests.Run(settings.ConnectionString, id, timeout.Token);
                await CatalogTests.Run(settings.ConnectionString, id, timeout.Token);
                await using (var change = database.Source!.CreateCommand("UPDATE du.schema_migrations SET sha256='tampered'"))
                    await change.ExecuteNonQueryAsync(timeout.Token);
                Require(await migrator.StatusAsync(id, timeout.Token) == "schema_mismatch", "Changed migration checksum is rejected");
                try { await migrator.MigrateAsync(id, timeout.Token); throw new Exception("Checksum mismatch accepted"); }
                catch (InvalidOperationException) { Console.WriteLine("PASS Migrator refuses altered journal"); }
            }
            finally
            {
                // Only the uniquely named database created by this invocation is removed.
                await using var drop = admin.CreateCommand($"DROP DATABASE {name} WITH (FORCE)");
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine($"PostgreSQL integration test failed; credentials/details suppressed. If cleanup failed, inspect isolated database {name}.");
            return 1;
        }
    }

    private static void Require(bool value, string name)
    {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
}
