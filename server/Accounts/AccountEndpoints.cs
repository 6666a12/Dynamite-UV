namespace DynamiteUniverse.Server.Accounts;

internal static class AccountEndpoints
{
    internal static void MapAccounts(this RouteGroupBuilder api)
    {
        var accounts = api.MapGroup("/accounts").AddEndpointFilter<AccountRequestFilter>();
        accounts.MapPost("/register", async (Credentials credentials, AccountService service, HttpContext http) =>
        {
            if (!AccountValidation.Valid(credentials)) return AccountRequestFilter.Error(400, "invalid_credentials_format");
            var profile = await service.RegisterAsync(credentials, http.RequestAborted);
            return profile is null ? AccountRequestFilter.Error(409, "username_unavailable") : Results.Json(profile, statusCode: 201);
        }).RequireRateLimiting("credentials").AddEndpointFilter<CredentialWorkFilter>();
        accounts.MapPost("/login", async (Credentials credentials, AccountService service, HttpContext http) =>
        {
            var result = await service.LoginAsync(credentials, http.RequestAborted);
            return result is null ? AccountRequestFilter.Error(401, "invalid_credentials") : Results.Ok(result);
        }).RequireRateLimiting("credentials").AddEndpointFilter<CredentialWorkFilter>();
        accounts.MapGet("/me", async (AccountService service, HttpContext http) =>
        {
            var profile = await service.AuthenticateAsync(AccountRequestFilter.Bearer(http), http.RequestAborted);
            return profile is null ? AccountRequestFilter.Error(401, "unauthorized") : Results.Ok(profile);
        });
        accounts.MapPost("/logout", async (AccountService service, HttpContext http) =>
        {
            var token = AccountRequestFilter.Bearer(http);
            if (await service.AuthenticateAsync(token, http.RequestAborted) is null)
                return AccountRequestFilter.Error(401, "unauthorized");
            await service.LogoutAsync(token!, http.RequestAborted);
            return Results.NoContent();
        });
    }
}
