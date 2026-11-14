namespace DynamiteUniverse.Network;

public sealed record AccountConnectionState(ServerAddress? Server, OnlineDescriptor? Descriptor,
    OnlineAccount? Account, bool Busy, string Status, bool NeedsConfirmation);

/// <summary>Single active session, immutable request identity, and generation-gated completion. No engine or disk.</summary>
public sealed class AccountConnection(IAccountTransport transport)
{
    private readonly object _gate = new();
    private CancellationTokenSource _requests = new();
    private long _generation;
    private ServerAddress? _server;
    private OnlineDescriptor? _descriptor;
    private OnlineLogin? _login;
    private Guid? _expectedInstance;
    private bool _confirmed;
    private bool _busy;
    private string _status = "select_server";
    public AccountConnectionState State
    {
        get
        {
            lock (_gate)
            {
                if (_login is not null && _login.ExpiresAt <= DateTimeOffset.UtcNow) { _login = null; _status = "session_expired"; }
                return new(_server, _descriptor, _login?.Account, _busy, _status, _descriptor is not null && !_confirmed);
            }
        }
    }
    public CatalogAccess CaptureCatalog(Guid serverEntryId)
    {
        lock (_gate)
        {
            if (serverEntryId == Guid.Empty || _busy || !_confirmed || _server is null || _login is null ||
                _login.ExpiresAt <= DateTimeOffset.UtcNow) throw new OnlineException("unauthorized");
            if (_descriptor?.Capabilities.Catalog != true || _descriptor.Contracts is not
                { Catalog: 1, PackageFormat: "dynamite-uv-pack/2", DigestAlgorithm: "gameplay-v1" })
                throw new OnlineException("incompatible_server");
            return new(new(serverEntryId, _server.BaseUri.AbsoluteUri, _descriptor.InstanceId),
                _login.Account.Id, _login.AccessToken, _login.ExpiresAt);
        }
    }
    public bool IsCurrent(CatalogAccess access)
    {
        lock (_gate) return _confirmed && _server?.BaseUri.AbsoluteUri == access.Identity.Address &&
            _descriptor?.InstanceId == access.Identity.InstanceId && _login?.Account.Id == access.AccountId &&
            _login.AccessToken == access.Token && _login.ExpiresAt > DateTimeOffset.UtcNow;
    }
    public void RejectCatalogSession(CatalogAccess access)
    {
        lock (_gate) { if (IsCurrent(access)) { _login = null; _status = "unauthorized"; } }
    }
    public void Select(ServerAddress address, Guid? expectedInstance)
    {
        lock (_gate)
        {
            Invalidate();
            _server = address; _expectedInstance = expectedInstance;
            _descriptor = null; _confirmed = false; _login = null; _status = "discover_required";
        }
    }
    public void CancelPending()
    {
        lock (_gate) { if (_busy) { Invalidate(); _status = "cancelled"; } }
    }
    private void Invalidate()
    {
        _generation++; _requests.Cancel(); _requests.Dispose(); _requests = new(); _busy = false;
    }
    public void ConfirmInstance()
    {
        lock (_gate)
        {
            if (_busy || _descriptor is null) return;
            _expectedInstance = _descriptor.InstanceId; _confirmed = true; _status = "ready_to_login";
        }
    }
    public Task DiscoverAsync()
    {
        lock (_gate) { if (!_busy) { _confirmed = false; _descriptor = null; _login = null; } }
        return Execute(async (server, _, ct) =>
    {
        var descriptor = await transport.DiscoverAsync(server, ct).ConfigureAwait(false);
        if (descriptor.Protocol != "dynamite-universe" || descriptor.ProtocolVersion != 1 ||
            descriptor.Contracts?.Accounts != 1 || descriptor.InstanceId == Guid.Empty ||
            descriptor.Name is not { Length: >= 1 and <= 80 } || descriptor.Name.Any(char.IsControl) || descriptor.Capabilities is null)
            throw new OnlineException("incompatible_server");
        return () =>
        {
            if (_descriptor is not null && _descriptor.InstanceId != descriptor.InstanceId) _login = null;
            _descriptor = descriptor;
            _confirmed = _expectedInstance == descriptor.InstanceId;
            if (!_confirmed) _login = null;
            _status = !_confirmed ? "confirm_server_identity" : descriptor.Capabilities.Accounts ? "ready_to_login" : "service_unavailable";
        };
    }, requireDiscovery: false);
    }
    public Task RegisterAsync(string username, string password) => Execute(async (server, _, ct) =>
    {
        ValidateCredentials(username, password);
        var profile = await transport.RegisterAsync(server, username, password, ct).ConfigureAwait(false);
        ValidateAccount(profile);
        return () => { _status = "registered_login_required"; };
    });
    public Task LoginAsync(string username, string password) => Execute(async (server, _, ct) =>
    {
        ValidateCredentials(username, password);
        var login = await transport.LoginAsync(server, username, password, ct).ConfigureAwait(false);
        if (login is null) throw new OnlineException("invalid_response");
        ValidateAccount(login.Account);
        if (login.Account.Username != username.Trim().ToLowerInvariant()) throw new OnlineException("identity_mismatch");
        if (login.AccessToken is not { Length: 64 } || login.AccessToken.Any(c => !char.IsAsciiHexDigit(c)) ||
            login.ExpiresAt <= DateTimeOffset.UtcNow || login.ExpiresAt > DateTimeOffset.UtcNow.AddDays(2))
            throw new OnlineException("invalid_response");
        return () => { _login = login; _status = "signed_in"; };
    });
    public Task RefreshAsync() => Execute(async (server, login, ct) =>
    {
        if (login is null) throw new OnlineException("unauthorized");
        var profile = await transport.MeAsync(server, login.AccessToken, ct).ConfigureAwait(false);
        ValidateAccount(profile);
        if (profile.Id != login.Account.Id) throw new OnlineException("identity_mismatch");
        return () => { _login = login with { Account = profile }; _status = "signed_in"; };
    });
    public async Task LogoutAsync()
    {
        ServerAddress? server; OnlineLogin? login; long generation;
        lock (_gate)
        {
            server = _server; login = _login; Invalidate(); generation = _generation;
            _login = null; _status = "signed_out";
        }
        if (server is null || login is null) return;
        try { await transport.LogoutAsync(server, login.AccessToken, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) when (ex is OnlineException or OperationCanceledException)
        { lock (_gate) { if (_generation == generation) _status = "signed_out_remote_revoke_failed"; } }
    }
    private async Task Execute(Func<ServerAddress, OnlineLogin?, CancellationToken, Task<Action>> operation, bool requireDiscovery = true)
    {
        ServerAddress server; OnlineLogin? login; CancellationToken ct; long generation;
        lock (_gate)
        {
            if (_busy) return;
            if (_server is null) { _status = "select_server"; return; }
            if (requireDiscovery && (!_confirmed || _descriptor?.Capabilities.Accounts != true))
            { _status = "discover_and_confirm_first"; return; }
            server = _server; login = _login; ct = _requests.Token; generation = ++_generation; _busy = true; _status = "working";
        }
        try
        {
            var apply = await operation(server, login, ct).ConfigureAwait(false);
            lock (_gate) { if (generation == _generation) apply(); }
        }
        catch (OperationCanceledException) { lock (_gate) { if (generation == _generation) _status = "cancelled"; } }
        catch (OnlineException ex)
        {
            lock (_gate)
            {
                if (generation != _generation) return;
                if (ex.Code is "unauthorized" or "identity_mismatch") _login = null;
                _status = ex.Code;
            }
        }
        finally { lock (_gate) { if (generation == _generation) _busy = false; } }
    }
    private static void ValidateCredentials(string username, string password)
    {
        if (username.Length is < 3 or > 32 || password.Length is < 12 or > 128 ||
            username.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) throw new OnlineException("invalid_input");
    }
    private static void ValidateAccount(OnlineAccount? profile)
    {
        if (profile is null || profile.Id == Guid.Empty || profile.Username is not { Length: >= 3 and <= 32 } ||
            profile.Username.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) throw new OnlineException("invalid_response");
    }
}
