using DynamiteUniverse.Server.Persistence;
using System.Threading.RateLimiting;
using DynamiteUniverse.Server.Accounts;
using DynamiteUniverse.Server.Catalog;
using DynamiteUniverse.Server.Protocol;
using DynamiteUniverse.Server.Records;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DynamiteUniverse.Server.Hosting;

public static class ServerApplication
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args, ContentRootPath = AppContext.BaseDirectory,
        });
        // Development-safe default; exposing this service requires explicit operator configuration.
        if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
            builder.WebHost.UseUrls("http://127.0.0.1:5080");
        configure?.Invoke(builder);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 64 * 1024;
            options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
        });
        builder.Services.AddOptions<InstanceOptions>()
            .Bind(builder.Configuration.GetSection(InstanceOptions.Section))
            .ValidateDataAnnotations()
            .Validate(o => o.Id != Guid.Empty, "Instance:Id must be an operator-assigned non-empty UUID.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Name), "Instance:Name must not be blank.")
            .ValidateOnStart();
        builder.Services.AddSingleton<DatabaseConnection>();
        builder.Services.AddSingleton<DatabaseMigrator>();
        builder.Services.AddSingleton<RecordService>();
        builder.Services.AddSingleton<CatalogService>();
        builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<CredentialWorkLimiter>();
        builder.Services.AddProblemDetails();
        builder.Services.AddRateLimiter(options =>
        {
            // A bounded global limiter for the scaffold, not production per-account abuse control.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter("instance", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                    AutoReplenishment = true,
                }));
            options.AddPolicy("credentials", _ => RateLimitPartition.GetFixedWindowLimiter("credentials", _ =>
                new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0, AutoReplenishment = true }));
            options.OnRejected = async (context, cancellation) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await Results.Problem(statusCode: 429, title: "Request limit exceeded",
                    extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" })
                    .ExecuteAsync(context.HttpContext);
            };
        });
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages(async context =>
            await Results.Problem(statusCode: context.HttpContext.Response.StatusCode)
                .ExecuteAsync(context.HttpContext));
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            var limit = context.GetEndpoint()?.Metadata.GetMetadata<CatalogUpload>() is null ? 64 * 1024 : PackageInspector.MaxArchive;
            var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = limit;
            if (context.Request.ContentLength > limit)
            {
                await Results.Problem(statusCode: 413, title: "Request body too large").ExecuteAsync(context);
                return;
            }
            await next(context);
        });
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).DisableRateLimiting();
        app.MapGet("/health/database", async (DatabaseMigrator database, IOptions<InstanceOptions> options, HttpContext context) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            string status;
            try { status = await database.StatusAsync(options.Value.Id, timeout.Token); }
            catch (OperationCanceledException) { status = "unavailable"; }
            return Results.Json(new { status }, statusCode: status == "ready" ? 200 : 503);
        });
        app.MapGet("/health/ready", async (DatabaseMigrator database, IOptions<InstanceOptions> options, HttpContext http) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            string status;
            try { status = await database.StatusAsync(options.Value.Id, timeout.Token); }
            catch (OperationCanceledException) { status = "unavailable"; }
            return Results.Json(new { status, stage = "phase1-preview" }, statusCode: status == "ready" ? 200 : 503);
        });
        app.UseRateLimiter();
        app.MapGet("/.well-known/dynamite-universe", async (IOptions<InstanceOptions> options, DatabaseMigrator database, HttpContext http) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            var available = false;
            try { available = await database.StatusAsync(options.Value.Id, timeout.Token) == "ready"; }
            catch (OperationCanceledException) { }
            return new ServerDescriptor("dynamite-universe", 1, options.Value.Id, options.Value.Name,
                "phase1-preview", new ServerCapabilities(available, available, available),
                new ServerContracts(1, available ? 1 : 0, available ? 2 : 0, "dynamite-uv-pack/2", "gameplay-v1"));
        });
        var api = app.MapGroup("/api/v1");
        api.MapAccounts();
        api.MapCatalog();
        api.MapPersonalRecords();
        return app;
    }
}
