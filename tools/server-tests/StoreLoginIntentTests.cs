using DynamiteUniverse.Network;

internal static class StoreLoginIntentTests
{
    internal static void Run(Action<bool, string> check)
    {
        var a = ServerAddress.Parse("https://a.invalid");
        var b = ServerAddress.Parse("https://b.invalid");
        var descriptor = new OnlineDescriptor("dynamite-universe", 1, Guid.NewGuid(), "Fixture", "test", new(true, true, false), new(1, 1, 0));
        var empty = new AccountConnectionState(null, null, null, false, "select_server", false);
        var ready = new AccountConnectionState(a, descriptor, null, false, "ready_to_login", false) { Generation = 11 };
        var success = ready with { Account = new(Guid.NewGuid(), "alice"), Status = "signed_in", Generation = 12 };
        StoreLoginIntent Begun()
        {
            var intent = new StoreLoginIntent(ready); intent.LoginStarted(ready, " ALICE "); return intent;
        }

        var first = new StoreLoginIntent(empty);
        check(first.Pin(a.BaseUri.AbsoluteUri, null) && first.Pin(a.BaseUri.AbsoluteUri, descriptor.InstanceId), "Store request can explicitly accept its first real server");
        first.LoginStarted(ready, "Alice");
        check(first.Take(success) && !first.Take(success), "Only the same successful login can continue once");
        var cancelled = Begun(); cancelled.Cancel();
        check(!cancelled.Take(success), "Back, local play, picker cancellation or exit discards pending destination");
        check(!new StoreLoginIntent(ready).Take(success), "Without a requested login a signed-in snapshot never redirects");
        var unconfirmed = new StoreLoginIntent(empty); unconfirmed.LoginStarted(ready with { NeedsConfirmation = true }, "alice");
        check(!unconfirmed.Take(success), "First server may not sign in before confirmation");
        var switched = Begun(); switched.Pin(b.BaseUri.AbsoluteUri, descriptor.InstanceId);
        check(!switched.Take(success), "Switching address cancels even if instance UUID is reused");
        var replaced = Begun(); replaced.Pin(a.BaseUri.AbsoluteUri, Guid.NewGuid());
        check(!replaced.Take(success), "Replacing instance on the same address cancels continuation");
        check(!Begun().Take(success with { Generation = success.Generation + 2 }), "Later login to the same server/account cannot satisfy a stale attempt");
        check(!Begun().Take(success with { Account = new(Guid.NewGuid(), "another") }), "An unrelated account cannot satisfy requested identity");
        check(!Begun().Take(success with { Status = "session_expired", Account = null }), "Expired login stays on account page");
        check(!Begun().Take(success with { Status = "registered_login_required", Account = null }), "Registration is not authentication");
        var retry = Begun();
        check(!retry.Take(success with { Status = "unauthorized", Account = null }), "Failed login does not enter store");
        retry.LoginStarted(ready with { Generation = 12 }, "alice");
        check(retry.Take(success with { Generation = 13 }), "Explicit retry on the original server may enter after verified login");
    }
}
