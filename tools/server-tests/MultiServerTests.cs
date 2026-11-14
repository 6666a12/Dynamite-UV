using DynamiteUniverse.Network;
using DynamiteUniverse.Server.Hosting;
using DynamiteUniverse.Server.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

/// <summary>Two independent PostgreSQL databases and HTTP instances, not aliases of one server.</summary>
internal static class MultiServerTests
{
    internal static async Task Run(string adminConnection, CancellationToken ct)
    {
        var names = new[] { "du_test_" + Guid.NewGuid().ToString("N"), "du_test_" + Guid.NewGuid().ToString("N") };
        await using var admin = NpgsqlDataSource.Create(adminConnection);
        var created = new List<string>();
        var apps = new List<Microsoft.AspNetCore.Builder.WebApplication>();
        var storage = Path.Combine(Path.GetTempPath(), "du-two-servers-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var name in names)
            {
                await using var create = admin.CreateCommand($"CREATE DATABASE {name}");
                await create.ExecuteNonQueryAsync(ct); created.Add(name);
                var cs = new NpgsqlConnectionStringBuilder(adminConnection) { Database = name }.ConnectionString;
                var id = Guid.NewGuid();
                var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = cs }).Build();
                await using var database = new DatabaseConnection(config);
                await new DatabaseMigrator(database).MigrateAsync(id, ct);
                var app = ServerApplication.Build([], b =>
                {
                    b.WebHost.UseUrls("http://127.0.0.1:0");
                    b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Instance:Id"] = id.ToString(), ["ConnectionStrings:Postgres"] = cs, ["Storage:CatalogRoot"] = Path.Combine(storage, name) });
                });
                apps.Add(app); await app.StartAsync(ct);
            }
            ServerAddress Address(int index) => ServerAddress.Parse(apps[index].Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single(), true);
            using var transport = new AccountTransport();
            var a = Address(0); var b = Address(1);
            var accountA = await transport.RegisterAsync(a, "same_player", "test-server-a-password", ct);
            var accountB = await transport.RegisterAsync(b, "same_player", "test-server-b-password", ct);
            if (accountA.Id == accountB.Id) throw new InvalidOperationException("Independent account identities collided");
            var loginA = await transport.LoginAsync(a, "same_player", "test-server-a-password", ct);
            try { await transport.MeAsync(b, loginA.AccessToken, ct); throw new InvalidOperationException("Cross-server token accepted"); }
            catch (OnlineException ex) when (ex.Code == "unauthorized") { }
            var connection = new AccountConnection(transport);
            connection.Select(a, null); await connection.DiscoverAsync(); connection.ConfirmInstance();
            await connection.LoginAsync("same_player", "test-server-a-password");
            connection.Select(b, null); await connection.DiscoverAsync();
            if (connection.State.Account is not null || !connection.State.NeedsConfirmation) throw new InvalidOperationException("Server switch retained authentication");
            connection.ConfirmInstance(); await connection.LoginAsync("same_player", "test-server-b-password");
            if (connection.State.Account?.Id != accountB.Id) throw new InvalidOperationException("Wrong current account");
            Console.WriteLine("PASS independent HTTP/database servers isolate same username, credentials and current session");
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
            var bytes = LocalMockPackage.Create(repo!.FullName);
            using var catalog = new CatalogTransport();
            var installer = new CatalogInstaller(Path.Combine(storage, "client"), catalog);
            for (var i = 0; i < 2; i++)
            {
                var id = i == 0 ? accountA.Id : accountB.Id;
                await using var role = apps[i].Services.GetRequiredService<DatabaseConnection>().Source!.CreateCommand("UPDATE du.accounts SET role='admin' WHERE id=$1");
                role.Parameters.AddWithValue(id); await role.ExecuteNonQueryAsync(ct);
                var endpoint = Address(i);
                var login = await transport.LoginAsync(endpoint, "same_player", i == 0 ? "test-server-a-password" : "test-server-b-password", ct);
                using var http = new HttpClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint.BaseUri, "api/v1/admin/catalog/releases?version=fixture"));
                request.Headers.Authorization = new("Bearer", login.AccessToken);
                request.Headers.Add("X-Content-Rights", "Synthetic isolated fixture");
                request.Content = new ByteArrayContent(bytes); request.Content.Headers.ContentType = new("application/zip");
                using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode();
                var descriptor = await transport.DiscoverAsync(endpoint, ct);
                var access = new CatalogAccess(new(Guid.NewGuid(), endpoint.BaseUri.AbsoluteUri, descriptor.InstanceId), id, login.AccessToken, login.ExpiresAt);
                var releases = await catalog.ListAsync(access, "", 0, ct);
                await installer.InstallAsync(access, "Server " + i, releases.Single(), ct);
                if (installer.State.Status != "installed") throw new InvalidOperationException("Independent server install failed");
            }
            if (installer.Installed().Count != 2 || installer.Installed().Select(e => e.Release.PackId).Distinct().Count() != 1)
                throw new InvalidOperationException("Same pack was not isolated");
            Console.WriteLine("PASS independent servers publish and install the same pack ID without merging sources");
        }
        finally
        {
            foreach (var app in apps) { await app.StopAsync(CancellationToken.None); await app.DisposeAsync(); }
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
            foreach (var name in created)
            {
                await using var drop = admin.CreateCommand($"DROP DATABASE {name} WITH (FORCE)");
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }
}
