using DynamiteUniverse.Server.Persistence;

namespace DynamiteUniverse.Server.Accounts;

internal sealed class SessionFilter(AccountService accounts, DatabaseConnection database, bool admin = false) : IEndpointFilter
{
    internal static Guid Actor(HttpContext http) => ((AccountProfile)http.Items[typeof(AccountProfile)]!).Id;
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var profile = await accounts.AuthenticateAsync(AccountRequestFilter.Bearer(http), http.RequestAborted);
        if (profile is null) return AccountRequestFilter.Error(401, "unauthorized");
        if (admin)
        {
            await using var command = database.Source!.CreateCommand("SELECT role FROM du.accounts WHERE id=$1 AND enabled");
            command.Parameters.AddWithValue(profile.Id);
            if (await command.ExecuteScalarAsync(http.RequestAborted) as string != "admin")
                return AccountRequestFilter.Error(403, "admin_required");
        }
        http.Items[typeof(AccountProfile)] = profile;
        return await next(context);
    }
}
internal sealed class AdminFilter(AccountService accounts, DatabaseConnection database) : IEndpointFilter
{
    private readonly SessionFilter _filter = new(accounts, database, true);
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c, EndpointFilterDelegate next) => _filter.InvokeAsync(c, next);
}
