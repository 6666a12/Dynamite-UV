namespace DynamiteUniverse.Network;

/// <summary>A one-use hint for navigation, never a credential or an authority to open the store.
/// A fresh install may choose its first server; once pinned, another address/instance invalidates it.</summary>
internal sealed class StoreLoginIntent(AccountConnectionState initial)
{
    private string? _address = initial.Server?.BaseUri.AbsoluteUri;
    private Guid? _instance = initial.Descriptor?.InstanceId;
    private long _attempt = -1;
    private string? _username;
    public bool Active { get; private set; } = initial.Account is null;
    public void Cancel() { Active = false; _attempt = -1; _username = null; }

    public bool Pin(string address, Guid? instance)
    {
        if (_address is not null && _address != address || _instance.HasValue && instance.HasValue && _instance != instance)
            Cancel();
        if (!Active) return false;
        _address ??= address;
        _instance ??= instance;
        return true;
    }
    public void LoginStarted(AccountConnectionState before, string username)
    {
        _attempt = -1;
        if (before.Server is null || before.Descriptor is null || before.NeedsConfirmation || before.Busy || before.Account is not null ||
            !Pin(before.Server.BaseUri.AbsoluteUri, before.Descriptor.InstanceId)) return;
        _username = username.Trim().ToLowerInvariant();
        _attempt = before.Generation + 1; // LoginAsync starts exactly one AccountConnection request.
    }
    public bool Take(AccountConnectionState state)
    {
        if (!Active || _attempt < 0) return false;
        var same = state.Generation == _attempt && state.Server?.BaseUri.AbsoluteUri == _address && state.Descriptor?.InstanceId == _instance;
        _attempt = -1;
        if (!same || state.Status is "session_expired" or "identity_mismatch" or "cancelled") { Cancel(); return false; }
        if (state.Busy || state.NeedsConfirmation || state.Status != "signed_in" || state.Account is not { } account) return false;
        var match = account.Username == _username && account.Id != Guid.Empty;
        Cancel();
        return match;
    }
}
