using Npgsql;

namespace DynamiteUniverse.Server.Persistence;

/// <summary>Owns one pooled data source. Configuration never appears in HTTP or diagnostic messages.</summary>
public sealed class DatabaseConnection : IAsyncDisposable
{
    public NpgsqlDataSource? Source { get; }

    public DatabaseConnection(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(connectionString)
            {
                IncludeErrorDetail = false, LogParameters = false,
                Timeout = 5, CommandTimeout = 15, MaxPoolSize = 20,
                ApplicationName = "DynamiteUniverse.Server",
            };
            if (string.IsNullOrWhiteSpace(settings.Database) || string.IsNullOrWhiteSpace(settings.Username))
                throw new ArgumentException("Database and username required");
            if (settings.Host is not ("127.0.0.1" or "localhost" or "::1") && settings.SslMode != SslMode.VerifyFull)
                throw new ArgumentException("Remote PostgreSQL requires VerifyFull TLS");
            Source = NpgsqlDataSource.Create(settings.ConnectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Invalid PostgreSQL configuration; check ConnectionStrings__Postgres.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Source is not null) await Source.DisposeAsync();
    }
}
