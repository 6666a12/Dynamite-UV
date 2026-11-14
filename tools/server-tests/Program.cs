using System.Net;
using System.Net.Http.Json;
using DynamiteUniverse.Server.Hosting;
using DynamiteUniverse.Server.Protocol;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

if (args.Contains("--local-mock-download")) return await LocalMockDownload.Run();
if (args.Contains("--postgres")) return await PostgresTests.Run();
Console.WriteLine("PostgreSQL integration: not requested; run --postgres separately with an isolated database role.");
var failures = 0;
var unsafeConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ConnectionStrings:Postgres"] = "Host=remote.invalid;Database=fixture;Username=fixture;Password=secret-marker;SSL Mode=Disable" }).Build();
try
{
    await using var rejected = new DynamiteUniverse.Server.Persistence.DatabaseConnection(unsafeConfiguration);
    Check(false, "Remote database rejects unverified TLS");
}
catch (InvalidOperationException ex)
{
    Check(!ex.ToString().Contains("secret-marker"), "Remote database rejects unverified TLS without leaking secrets");
}
void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
    if (!condition) failures++;
}
await ClientNetworkTests.Run(Check);
await StoreClientTests.Run(Check);
await UploadStateTests.Run(Check);
var instanceId = Guid.NewGuid();
await using var app = ServerApplication.Build([], builder =>
{
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Instance:Id"] = instanceId.ToString(), ["Instance:Name"] = "Isolated HTTP fixture",
        ["ConnectionStrings:Postgres"] = "",
    });
});
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
try
{
    await app.StartAsync(deadline.Token);
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
    var descriptor = await http.GetFromJsonAsync<ServerDescriptor>("/.well-known/dynamite-universe", deadline.Token);
    Check(descriptor is { ProtocolVersion: 1, ImplementationStage: "phase1-preview" } && descriptor.InstanceId == instanceId,
        "HTTP discovery preserves configured server identity");
    Check(descriptor?.Capabilities == new ServerCapabilities(false, false, false), "Discovery does not advertise unfinished business features");
    using var live = await http.GetAsync("/health/live", deadline.Token);
    Check(live.StatusCode == HttpStatusCode.OK, "Liveness is available");
    using var databaseHealth = await http.GetAsync("/health/database", deadline.Token);
    Check(databaseHealth.StatusCode == HttpStatusCode.ServiceUnavailable &&
        (await databaseHealth.Content.ReadAsStringAsync(deadline.Token)).Contains("not_configured"),
        "Missing database configuration is explicitly unavailable");
    using var ready = await http.GetAsync("/health/ready", deadline.Token);
    Check(ready.StatusCode == HttpStatusCode.ServiceUnavailable, "Readiness fails closed without persistence/authentication");
    foreach (var (method, path) in new[] { (HttpMethod.Post, "/api/v1/accounts/register"),
        (HttpMethod.Post, "/api/v1/accounts/login"), (HttpMethod.Get, "/api/v1/catalog/packs"),
        (HttpMethod.Get, "/api/v1/me/plays/"), (HttpMethod.Post, "/api/v1/me/plays/") })
    {
        using var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post && path.StartsWith("/api/v1/accounts/"))
            request.Content = JsonContent.Create(new { username = "fixture", password = "fixture-password-123" });
        if (method == HttpMethod.Post && path.StartsWith("/api/v1/me/"))
            request.Content = JsonContent.Create(new { });
        using var response = await http.SendAsync(request, deadline.Token);
        Check(response.StatusCode == HttpStatusCode.ServiceUnavailable &&
            response.Content.Headers.ContentType?.MediaType == "application/problem+json", $"Explicit unavailable contract: {path}");
        Check(response.Headers.CacheControl?.NoStore == true, "Responses are not cached");
    }
    using var missing = await http.GetAsync("/api/v2/catalog/packs", deadline.Token);
    Check(missing.StatusCode == HttpStatusCode.NotFound, "Unknown protocol route is not silently redirected");
    using var oversized = await http.PostAsync("/api/v1/accounts/register", new ByteArrayContent(new byte[65537]), deadline.Token);
    Check(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge, "Oversized declared body is rejected");
    HttpStatusCode status = HttpStatusCode.OK;
    for (var i = 0; i < 125 && status != HttpStatusCode.TooManyRequests; i++)
    {
        using var response = await http.GetAsync("/.well-known/dynamite-universe", deadline.Token);
        status = response.StatusCode;
    }
    Check(status == HttpStatusCode.TooManyRequests, "Scaffold request limiter is active");
    using var liveAfterLimit = await http.GetAsync("/health/live", deadline.Token);
    Check(liveAfterLimit.StatusCode == HttpStatusCode.OK, "Liveness remains available after request limit");
}
finally { await app.StopAsync(CancellationToken.None); }
await using var invalid = ServerApplication.Build([], builder =>
{
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Instance:Id"] = Guid.Empty.ToString() });
});
try { await invalid.StartAsync(deadline.Token); Check(false, "Empty identity fails startup"); }
catch (OptionsValidationException) { Check(true, "Empty identity fails startup"); }
finally { await invalid.StopAsync(CancellationToken.None); }
return failures == 0 ? 0 : 1;
