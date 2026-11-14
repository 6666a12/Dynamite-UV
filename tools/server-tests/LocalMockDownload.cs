using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DynamiteUniverse.Network;
using DynamiteUniverse.Server.Accounts;
using DynamiteUniverse.Server.Hosting;
using DynamiteUniverse.Shared.Chart.V2;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

/// <summary>Explicit opt-in: publishes one mock release to the local dev DB; never runs in default tests.</summary>
internal static class LocalMockDownload
{
    internal static async Task<int> Run()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? throw new InvalidOperationException("Use server/dev.py mock-download");
        var builder = new NpgsqlConnectionStringBuilder(connection);
        if (builder.Host != "127.0.0.1" || builder.Port != 55432 || builder.Database != "dynamite")
            throw new InvalidOperationException("Only the local development database is allowed");
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
        var temporary = Path.Combine(Path.GetTempPath(), "du-local-download-" + Guid.NewGuid().ToString("N"));
        Guid? actor = null;
        await using var database = NpgsqlDataSource.Create(connection);
        await using var app = ServerApplication.Build([], b => b.WebHost.UseUrls("http://127.0.0.1:0"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = deadline.Token;
        await app.StartAsync(ct);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var accounts = new AccountTransport();
            var server = ServerAddress.Parse(address, true);
            var descriptor = await accounts.DiscoverAsync(server, ct);
            var name = "mock_" + Guid.NewGuid().ToString("N")[..20];
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var profile = await accounts.RegisterAsync(server, name, password, ct); actor = profile.Id;
            var login = await accounts.LoginAsync(server, name, password, ct);
            // A fresh fixture actor only; preserve every existing user and role.
            await using (var promote = database.CreateCommand("""
                WITH promoted AS (UPDATE du.accounts SET role='admin' WHERE id=$1 RETURNING id)
                INSERT INTO du.admin_audit(actor_id,action,target,reason)
                SELECT id,'grant_admin',id::text,'explicit local mock download fixture' FROM promoted
                """))
            { promote.Parameters.AddWithValue(actor.Value); await promote.ExecuteNonQueryAsync(ct); }
            using var catalog = new CatalogTransport();
            var access = new CatalogAccess(new(Guid.NewGuid(), server.BaseUri.AbsoluteUri, descriptor.InstanceId),
                actor.Value, login.AccessToken, login.ExpiresAt);
            var existing = await catalog.ListAsync(access, LocalMockPackage.PackId, 0, ct);
            var release = existing.FirstOrDefault(r => r.PackId == LocalMockPackage.PackId);
            if (release is null)
            {
                using var http = new HttpClient { BaseAddress = server.BaseUri };
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/admin/catalog/releases?version=mock-v1");
                request.Headers.Authorization = new("Bearer", login.AccessToken);
                request.Headers.Add("X-Content-Rights", "Local test only: repository synthetic schema charts and generated sine pulses; no third-party artwork or game assets.");
                request.Content = new ByteArrayContent(LocalMockPackage.Create(repo!.FullName));
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                using var response = await http.SendAsync(request, ct);
                if (response.StatusCode != HttpStatusCode.Created) throw new InvalidOperationException("Mock publication failed: " + (int)response.StatusCode);
                release = await response.Content.ReadFromJsonAsync<StoreRelease>(ct) ?? throw new InvalidOperationException("Missing release");
                Console.WriteLine("PASS mock published through authenticated HTTP admin endpoint");
            }
            else Console.WriteLine("PASS existing mock release reused; no duplicate publication");
            var detail = await catalog.DetailsAsync(access, release.Id, ct);
            var installer = new CatalogInstaller(temporary, catalog);
            await installer.InstallAsync(access, descriptor.Name, release, ct);
            if (installer.State.Status != "installed") throw new InvalidOperationException("Mock installation failed: " + installer.State.Error);
            var receipt = installer.Installed().Single();
            Console.WriteLine("PASS HTTP download, archive integrity and atomic installation");
            foreach (var chart in detail.Charts)
            {
                var loaded = V2Integration.LoadStrict(installer.PackagePath(receipt), chart.ChartId);
                if (loaded.GameplayDigest != chart.Digest || loaded.RulesetId != chart.RulesetId || loaded.NoteCount < 1)
                    throw new InvalidOperationException("Installed chart identity mismatch");
                Console.WriteLine($"PASS installed chart {chart.ChartId}: {loaded.NoteCount} notes, published Digest matches");
            }
            await installer.InstallAsync(access, descriptor.Name, release, ct);
            if (installer.Installed().Count != 1) throw new InvalidOperationException("Duplicate installation");
            Console.WriteLine("PASS repeat download reuses completed installation");
            Console.WriteLine($"Mock available in local catalog: {release.Title} / {release.Id}");
            return 0;
        }
        finally
        {
            if (actor is { } id) await RevokeFixture(database, id);
            await app.StopAsync(CancellationToken.None);
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }
    private static async Task RevokeFixture(NpgsqlDataSource database, Guid id)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var sql in new[] {
            "UPDATE du.accounts SET enabled=false,role='user' WHERE id=$1",
            "DELETE FROM du.sessions WHERE account_id=$1",
            "INSERT INTO du.admin_audit(actor_id,action,target,reason) VALUES ($1,'disable_fixture',$1::text,'local mock test complete; temporary credentials revoked')" })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue(id);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        await using var verify = database.CreateCommand("""
            SELECT count(*) FROM du.accounts a WHERE id=$1
            AND (enabled OR role='admin' OR EXISTS (SELECT 1 FROM du.sessions s WHERE s.account_id=a.id))
            """);
        verify.Parameters.AddWithValue(id);
        if ((long)(await verify.ExecuteScalarAsync())! != 0) throw new InvalidOperationException("Fixture revocation incomplete");
        Console.WriteLine("PASS temporary fixture account disabled, role removed and sessions revoked");
    }
}
