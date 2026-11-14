using System.Net;
using System.Net.Http.Json;
using DynamiteUniverse.Network;

internal static class ClientNetworkTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var address in new[] { "http://example.com", "https://example.com/path", "https://user:pass@example.com", "https://example.com/?secret=x" })
        {
            try { ServerAddress.Parse(address); check(false, "Unsafe endpoint rejected"); }
            catch (OnlineException) { check(true, "Unsafe endpoint rejected"); }
        }
        var a = ServerAddress.Parse("https://a.invalid"); var b = ServerAddress.Parse("https://b.invalid");
        var fake = new Fake(); var state = new AccountConnection(fake);
        state.Select(a, null); await state.DiscoverAsync();
        check(state.State.NeedsConfirmation, "First discovery requires explicit identity confirmation");
        await state.LoginAsync("fixture", "fixture-password");
        check(fake.Logins == 0, "No credentials sent before confirmation");
        state.ConfirmInstance();
        fake.Delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldLogin = state.LoginAsync("fixture", "fixture-password");
        state.Select(b, null); await state.DiscoverAsync(); state.ConfirmInstance();
        fake.Delayed.SetResult(new(new string('a',64),DateTimeOffset.UtcNow.AddHours(1),new(Guid.NewGuid(),"fixture")));
        await oldLogin;
        check(state.State.Account is null && state.State.Server == b, "Late login from A cannot populate B even with same instance UUID");
        fake.Delayed = null; await state.LoginAsync("fixture", "fixture-password");
        check(state.State.Account is not null, "Confirmed current server can log in");
        fake.Offline = true; await state.RefreshAsync();
        check(state.State.Account is not null, "Transport failure does not sign out current identity");
        fake.Offline = false;
        var access = state.CaptureCatalog(Guid.NewGuid());
        fake.DelayedMe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = state.RefreshAsync();
        state.RejectCatalogSession(access);
        check(state.State.Account is null && !state.State.Busy, "Rejected catalog session cancels pending account operation");
        fake.DelayedMe.SetResult(fake.Account); await refresh;
        check(state.State.Account is null && state.State.Status == "unauthorized", "Late refresh cannot restore rejected credentials");
        fake.DelayedMe = null;
        fake.Instance = Guid.NewGuid(); await state.DiscoverAsync();
        check(state.State.Account is null && state.State.NeedsConfirmation, "Changed instance clears credentials and requires confirmation");
        using var redirect = new AccountTransport(new Replies(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
            { Headers = { Location = new Uri("https://other.invalid/") } }));
        try { await redirect.DiscoverAsync(a, default); check(false,"Redirect refused"); }
        catch (OnlineException ex) { check(ex.Code == "redirect_refused", "Redirect refused"); }
        using var oversized = new AccountTransport(new Replies(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(new string('x', 65537), System.Text.Encoding.UTF8, "application/json") }));
        try { await oversized.DiscoverAsync(a,default); check(false,"Oversized response refused"); }
        catch (OnlineException ex) { check(ex.Code == "invalid_response", "Oversized response refused"); }
        using var malformed = new AccountTransport(new Replies(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{broken",System.Text.Encoding.UTF8,"application/json") }));
        try { await malformed.DiscoverAsync(a,default); check(false,"Malformed response refused"); }
        catch (OnlineException ex) { check(ex.Code == "invalid_response", "Malformed response refused"); }
    }
    private sealed class Replies(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) => Task.FromResult(reply(request));
    }
    private sealed class Fake : IAccountTransport
    {
        internal Guid Instance = Guid.NewGuid(); internal bool Offline; internal int Logins;
        internal TaskCompletionSource<OnlineLogin>? Delayed;
        internal readonly OnlineAccount Account = new(Guid.NewGuid(),"fixture");
        internal TaskCompletionSource<OnlineAccount>? DelayedMe;
        public Task<OnlineDescriptor> DiscoverAsync(ServerAddress s,CancellationToken ct) => Task.FromResult(new OnlineDescriptor(
            "dynamite-universe",1,Instance,"Fixture","test",new(true,true,true),new(1,1,2,"dynamite-uv-pack/2","gameplay-v1")));
        public Task<OnlineLogin> LoginAsync(ServerAddress s,string u,string p,CancellationToken ct)
        { Logins++; return Delayed?.Task ?? Task.FromResult(new OnlineLogin(new string('b',64),DateTimeOffset.UtcNow.AddHours(1),Account)); }
        public Task<OnlineAccount> RegisterAsync(ServerAddress s,string u,string p,CancellationToken ct) => Task.FromResult(Account);
        public Task<OnlineAccount> MeAsync(ServerAddress s,string t,CancellationToken ct) => Offline ? Task.FromException<OnlineAccount>(new OnlineException("connection_failed")) : DelayedMe?.Task ?? Task.FromResult(Account);
        public Task LogoutAsync(ServerAddress s,string t,CancellationToken ct) => Task.CompletedTask;
    }
}
