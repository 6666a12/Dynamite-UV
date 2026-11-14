using DynamiteUniverse.Server.Accounts;

namespace DynamiteUniverse.Server.Records;

internal static class RecordEndpoints
{
    internal static void MapPersonalRecords(this RouteGroupBuilder api)
    {
        var records = api.MapGroup("/me/plays").AddEndpointFilter<AccountRequestFilter>().AddEndpointFilter<SessionFilter>();
        records.MapGet("/", async (int? offset, RecordService service, HttpContext http) =>
        {
            if (offset is < 0 or > 10000) return AccountRequestFilter.Error(400, "invalid_offset");
            return Results.Ok(await service.ListAsync(SessionFilter.Actor(http), offset ?? 0, http.RequestAborted));
        });
        records.MapGet("/v2", async (int? offset, RecordService service, HttpContext http) =>
        {
            if (offset is < 0 or > 10000) return AccountRequestFilter.Error(400, "invalid_offset");
            return Results.Ok(await service.ListV2Async(SessionFilter.Actor(http), offset ?? 0, http.RequestAborted));
        });
        records.MapPost("/v2", async (DynamiteUniverse.Shared.Score.PlayReport play, RecordService service, HttpContext http) =>
        {
            var status = await service.SubmitV2Async(SessionFilter.Actor(http), play, http.RequestAborted);
            return status.Status is 200 or 201 ? Results.Json(new { playId = play.PlayId, verification = "client_reported" }, statusCode: status.Status) :
                AccountRequestFilter.Error(status.Status, status.Code ?? "play_not_eligible");
        });
        records.MapPost("/", async (PlaySubmission play, RecordService service, HttpContext http) =>
        {
            var status = await service.SubmitAsync(SessionFilter.Actor(http), play, http.RequestAborted);
            return status.Status is 200 or 201 ? Results.Json(new { playId = play.PlayId, verification = "client_reported" }, statusCode: status.Status) :
                AccountRequestFilter.Error(status.Status, status.Code ?? "play_not_eligible");
        });
    }
}
