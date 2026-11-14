using System.Net;
using DynamiteUniverse.Server.Hosting;
using DynamiteUniverse.Server.Persistence;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DynamiteUniverse.Server.Accounts;

internal sealed class AccountOperationDeadline;

internal sealed class AccountRequestFilter(DatabaseMigrator database, IOptions<InstanceOptions> instance) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        // Development plaintext is only allowed for an actual loopback peer, never a forwarded header.
        if (!http.Request.IsHttps && (http.Connection.RemoteIpAddress is not { } peer || !IPAddress.IsLoopback(peer)))
            return Error(400, "https_required");
        var aborted = http.RequestAborted;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            if (await database.StatusAsync(instance.Value.Id, timeout.Token) != "ready")
                return Error(503, "database_unavailable");
            // Only account operations share this overall deadline. Publishing and transfers
            // have their own budgets and must not inherit an account's ten-second limit.
            if (http.GetEndpoint()?.Metadata.GetMetadata<AccountOperationDeadline>() is not null)
                http.RequestAborted = timeout.Token;
            return await next(context);
        }
        catch (NpgsqlException) { return Error(503, "database_unavailable"); }
        catch (TimeoutException) { return Error(503, "database_unavailable"); }
        catch (OperationCanceledException) when (!aborted.IsCancellationRequested)
        { return Error(503, "database_unavailable"); }
        finally { http.RequestAborted = aborted; }
    }

    internal static IResult Error(int status, string code) => Results.Problem(statusCode: status,
        title: code, extensions: new Dictionary<string, object?> { ["code"] = code });
    internal static string? Bearer(HttpContext http)
    {
        var header = http.Request.Headers.Authorization;
        if (header.Count != 1) return null;
        var value = header.ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? value[7..] : null;
    }
}
