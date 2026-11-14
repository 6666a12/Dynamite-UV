namespace DynamiteUniverse.Server.Accounts;

/// <summary>Bound password-hashing work as well as request frequency; do not queue credential bodies.</summary>
internal sealed class CredentialWorkLimiter : IDisposable
{
    private readonly SemaphoreSlim _slots = new(2, 2);
    internal bool TryEnter() => _slots.Wait(0);
    internal void Exit() => _slots.Release();
    public void Dispose() => _slots.Dispose();
}

internal sealed class CredentialWorkFilter(CredentialWorkLimiter limiter) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!limiter.TryEnter())
        {
            context.HttpContext.Response.Headers.RetryAfter = "1";
            return AccountRequestFilter.Error(429, "credential_service_busy");
        }
        try { return await next(context); }
        finally { limiter.Exit(); }
    }
}
