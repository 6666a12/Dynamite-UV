using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DynamiteUniverse.Server.Accounts;
using DynamiteUniverse.Server.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

internal static class AccountTests
{
    internal static async Task Run(string connectionString, Guid instance, CancellationToken cancellation)
    {
        await using var app = ServerApplication.Build([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Instance:Id"] = instance.ToString(), ["ConnectionStrings:Postgres"] = connectionString,
            });
        });
        await app.StartAsync(cancellation);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
            const string password = "test-password-' OR 1=1; --";
            async Task<HttpResponseMessage> Post(string endpoint, string user, string pass) =>
                await http.PostAsJsonAsync("/api/v1/accounts/" + endpoint, new Credentials(user, pass), cancellation);
            using (var registration = await Post("register", "Alice_123", password))
                Require(registration.StatusCode == HttpStatusCode.Created, "Registration persists account");
            using (var duplicate = await Post("register", "alice_123", password))
                Require(duplicate.StatusCode == HttpStatusCode.Conflict, "Normalized username uniqueness enforced");
            using (var injection = await Post("register", "' OR 1=1 --", password))
                Require(injection.StatusCode == HttpStatusCode.BadRequest, "Invalid username rejected");
            using (var wrong = await Post("login", "alice_123", "incorrect-password"))
            using (var unknown = await Post("login", "unknown_123", "incorrect-password"))
                Require(wrong.StatusCode == HttpStatusCode.Unauthorized && unknown.StatusCode == wrong.StatusCode,
                    "Unknown account and wrong password share unauthorized response");
            using var loggedIn = await Post("login", "ALICE_123", password);
            var login = await loggedIn.Content.ReadFromJsonAsync<LoginResponse>(cancellation);
            Require(loggedIn.StatusCode == HttpStatusCode.OK && login is not null, "Password containing SQL syntax authenticates literally");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
            var me = await http.GetFromJsonAsync<AccountProfile>("/api/v1/accounts/me", cancellation);
            Require(me?.Id == login.Account.Id && me.Username == "alice_123", "Bearer resolves only its own account");
            await using var source = NpgsqlDataSource.Create(connectionString);
            await using (var stored = source.CreateCommand("SELECT password_hash FROM du.accounts WHERE id=$1"))
            {
                stored.Parameters.AddWithValue(me!.Id);
                var hash = (string)(await stored.ExecuteScalarAsync(cancellation))!;
                Require(hash != password && !hash.Contains(password), "Database stores password hash, not plaintext");
            }
            await using (var session = source.CreateCommand("SELECT token_hash FROM du.sessions WHERE account_id=$1"))
            {
                session.Parameters.AddWithValue(login.Account.Id);
                var storedToken = (byte[])(await session.ExecuteScalarAsync(cancellation))!;
                Require(storedToken.SequenceEqual(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.ASCII.GetBytes(login.AccessToken))), "Session stores token digest only");
            }
            using (var bobRegistration = await Post("register", "bob_123", password))
                Require(bobRegistration.StatusCode == HttpStatusCode.Created, "Second account registers independently");
            using var bobResponse = await Post("login", "bob_123", password);
            var bob = (await bobResponse.Content.ReadFromJsonAsync<LoginResponse>(cancellation))!;
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bob.AccessToken);
            var bobMe = await http.GetFromJsonAsync<AccountProfile>("/api/v1/accounts/me", cancellation);
            Require(bobMe?.Id == bob.Account.Id && bobMe.Id != login.Account.Id, "Two accounts cannot read each other's profile via session");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            using (var logout = await http.PostAsync("/api/v1/accounts/logout", null, cancellation))
                Require(logout.StatusCode == HttpStatusCode.NoContent, "Logout revokes current session");
            using (var revoked = await http.GetAsync("/api/v1/accounts/me", cancellation))
                Require(revoked.StatusCode == HttpStatusCode.Unauthorized, "Revoked token cannot authenticate");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bob.AccessToken);
            using (var bobStillValid = await http.GetAsync("/api/v1/accounts/me", cancellation))
                Require(bobStillValid.StatusCode == HttpStatusCode.OK, "Logout leaves another account's session intact");
            using var secondLogin = await Post("login", "alice_123", password);
            var second = (await secondLogin.Content.ReadFromJsonAsync<LoginResponse>(cancellation))!;
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
            await using (var disable = source.CreateCommand("UPDATE du.accounts SET enabled=false WHERE id=$1"))
            {
                disable.Parameters.AddWithValue(second.Account.Id);
                await disable.ExecuteNonQueryAsync(cancellation);
            }
            using (var disabled = await http.GetAsync("/api/v1/accounts/me", cancellation))
                Require(disabled.StatusCode == HttpStatusCode.Unauthorized, "Disabled account loses session access immediately");
            using (var disabledLogin = await Post("login", "alice_123", password))
                Require(disabledLogin.StatusCode == HttpStatusCode.Unauthorized, "Disabled account cannot create sessions");
            await using (var enable = source.CreateCommand("UPDATE du.accounts SET enabled=true WHERE id=$1"))
            {
                enable.Parameters.AddWithValue(second.Account.Id);
                await enable.ExecuteNonQueryAsync(cancellation);
            }
            await using (var expire = source.CreateCommand("UPDATE du.sessions SET created_at=now()-interval '2 days',expires_at=now()-interval '1 day'"))
                await expire.ExecuteNonQueryAsync(cancellation);
            using (var expired = await http.GetAsync("/api/v1/accounts/me", cancellation))
                Require(expired.StatusCode == HttpStatusCode.Unauthorized, "Expired token cannot authenticate");
            using var transport = new DynamiteUniverse.Network.AccountTransport();
            var client = new DynamiteUniverse.Network.AccountConnection(transport);
            client.Select(DynamiteUniverse.Network.ServerAddress.Parse(address, allowLocalHttp: true), null);
            await client.DiscoverAsync(); client.ConfirmInstance();
            await client.RegisterAsync("client_fixture", "client-fixture-password");
            Require(client.State.Status == "registered_login_required" && client.State.Account is null, "Game client registration does not silently sign in");
            await client.LoginAsync("client_fixture", "client-fixture-password");
            Require(client.State.Account?.Username == "client_fixture", "Game client transport logs in against real PostgreSQL API");
            await client.RefreshAsync();
            Require(client.State.Status == "signed_in", "Game client refresh validates its bearer identity");
            await client.LogoutAsync();
            Require(client.State.Account is null && client.State.Status == "signed_out", "Game client logout revokes remote session");
            HttpStatusCode limited = HttpStatusCode.OK;
            for (var i = 0; i < 21 && limited != HttpStatusCode.TooManyRequests; i++)
            {
                using var attempt = await Post("login", "alice_123", "incorrect-password");
                limited = attempt.StatusCode;
            }
            Require(limited == HttpStatusCode.TooManyRequests, "Credential endpoint limit enforced");
        }
        finally { await app.StopAsync(CancellationToken.None); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
