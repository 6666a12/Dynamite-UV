using DynamiteUniverse.Server.Accounts;
using DynamiteUniverse.Shared.Chart.V2;
using Npgsql;

namespace DynamiteUniverse.Server.Catalog;

internal static class CatalogEndpoints
{
    internal static void MapCatalog(this RouteGroupBuilder api)
    {
        var catalog = api.MapGroup("/catalog").AddEndpointFilter<AccountRequestFilter>().AddEndpointFilter<SessionFilter>();
        catalog.MapGet("/packs", async (string? q, int? offset, CatalogService service, HttpContext http) =>
        {
            try { return Results.Ok(await service.ListAsync(q, offset ?? 0, http.RequestAborted)); }
            catch (ArgumentException) { return AccountRequestFilter.Error(400, "invalid_query"); }
        });
        catalog.MapGet("/releases/{id:guid}", async (Guid id, CatalogService service, HttpContext http) =>
            await service.GetAsync(id, http.RequestAborted) is { } details ? Results.Ok(details) : Results.NotFound());
        catalog.MapGet("/releases/{id:guid}/download", async (Guid id, CatalogService service, HttpContext http) =>
        {
            var details = await service.GetAsync(id, http.RequestAborted);
            if (details is null) return Results.NotFound();
            var stream = await service.DownloadAsync(details.Release, http.RequestAborted);
            return stream is null ? AccountRequestFilter.Error(503, "content_unavailable") :
                Results.File(stream, "application/zip", id + ".zip", enableRangeProcessing: true);
        });
        catalog.MapGet("/releases/{id:guid}/cover", async (Guid id, CatalogService service, HttpContext http) =>
        {
            var details = await service.GetAsync(id, http.RequestAborted);
            if (details?.Release.Display?.Cover is not { } cover) return Results.NotFound();
            var stream = await service.CoverAsync(details.Release, http.RequestAborted);
            return stream is null ? AccountRequestFilter.Error(503, "content_unavailable") : Results.File(stream, cover.MediaType);
        });
        var admin = api.MapGroup("/admin/catalog").AddEndpointFilter<AccountRequestFilter>().AddEndpointFilter<AdminFilter>();
        admin.MapGet("/releases", async (int? offset, CatalogService service, HttpContext http) =>
        {
            if (offset is < 0 or > 10000) return AccountRequestFilter.Error(400, "invalid_offset");
            return Results.Ok(await service.AdminListAsync(offset ?? 0, http.RequestAborted));
        });
        admin.MapPost("/releases", async (string version, CatalogService service, HttpContext http) =>
        {
            if (http.Request.ContentType != "application/zip") return AccountRequestFilter.Error(415, "zip_required");
            var rights = http.Request.Headers["X-Content-Rights"].ToString();
            try
            {
                var release = await service.PublishAsync(SessionFilter.Actor(http), version, rights, http.Request.Body, http.RequestAborted);
                return Results.Json(release, statusCode: 201);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505") { return AccountRequestFilter.Error(409, "release_exists"); }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or V2DiagnosticException or OverflowException)
            { return AccountRequestFilter.Error(400, "invalid_package"); }
            catch (InvalidOperationException ex) when (ex.Message == "publication_busy")
            { return AccountRequestFilter.Error(429, "publication_busy"); }
        }).WithMetadata(new CatalogUpload());
        admin.MapPost("/releases/{id:guid}/withdraw", async (Guid id, Withdrawal request, CatalogService service, HttpContext http) =>
        {
            try { return await service.WithdrawAsync(SessionFilter.Actor(http), id, request.Reason ?? "", http.RequestAborted)
                    ? Results.NoContent() : Results.NotFound(); }
            catch (ArgumentException) { return AccountRequestFilter.Error(400, "reason_required"); }
        });
    }
}
internal sealed class CatalogUpload;
