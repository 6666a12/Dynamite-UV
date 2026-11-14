using DynamiteUniverse.Server.Hosting;
using DynamiteUniverse.Server.Persistence;
using Microsoft.Extensions.Options;

if (args.Length == 2 && args[0] == "--inspect-package") return PackagePreflight.Run(args[1]);
var promote = args.Length == 2 && args[0] == "--grant-admin";
var migrate = args.Contains("--migrate", StringComparer.Ordinal);
await using var app = ServerApplication.Build(promote ? [] : args.Where(a => a != "--migrate").ToArray());
if (promote)
{
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var id = app.Services.GetRequiredService<IOptions<InstanceOptions>>().Value.Id;
        if (await app.Services.GetRequiredService<DatabaseMigrator>().StatusAsync(id, timeout.Token) != "ready") return 1;
        await DynamiteUniverse.Server.Accounts.AdminProvisioning.PromoteAsync(
            app.Services.GetRequiredService<DatabaseConnection>(), args[1], timeout.Token);
        Console.WriteLine("Administrator role granted to existing account and audited.");
        return 0;
    }
    catch (Exception) { Console.Error.WriteLine("Administrator provisioning failed."); return 1; }
}
if (migrate)
{
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var identity = app.Services.GetRequiredService<IOptions<InstanceOptions>>().Value.Id;
        await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(identity, timeout.Token);
        Console.WriteLine("PostgreSQL migrations applied and instance identity verified.");
        return 0;
    }
    catch (Exception)
    {
        // Driver errors may contain connection details; never print credentials or raw SQL values.
        Console.Error.WriteLine("PostgreSQL migration failed. Check configuration, connectivity, permissions and migration/instance identity. Migrations are transactional; retry safely to verify the committed state.");
        return 1;
    }
}
await app.RunAsync();
return 0;
