using DynamiteUniverse.Network;
using DynamiteUniverse.Ui;

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}
static StoreRelease Release(int number) => new(Guid.NewGuid(), "pack" + number, "1.0", "Title " + number,
    "Artist", number, 512, new string('a', 64), "Test fixture only", DateTimeOffset.UnixEpoch.AddSeconds(number),
    new(StoreContract.Profile, [new("hard", "hard", null, 5, false, ["Mapper " + number], 120, 144)], null));
var source = new CatalogIdentity(Guid.NewGuid(), "https://a.example/", Guid.NewGuid());
var foreign = source with { InstanceId = Guid.NewGuid() };
var records = Enumerable.Range(0, 51).Select(Release).ToArray();
var model = new StoreBrowseState();
model.Begin("", StoreFilter.All, true); model.AcceptPage(0, records[..50]);
Require(model.Visible.Count == 50 && model.HasMore && model.NextOffset == 50 && model.Expanded is null, "first page doesn't open a release automatically");
Require(model.Choose(records[2].Id) && model.Expanded == records[2], "explicit selection opens only its real release");
model.SetReceipts(source, [new(Guid.NewGuid(), foreign, "foreign", records[2])]);
model.Begin("", StoreFilter.Installed, false);
Require(model.Visible.Count == 0 && model.Expanded is null && model.Receipt(records[2].Id) is null && !model.Choose(records[2].Id), "empty source-scoped installed state cannot reopen a stale or foreign card");
model.Begin("", StoreFilter.All, false);
Require(model.Expanded == records[2], "returning to all restores the previously explicitly expanded visible release");
model.Choose(records[2].Id); model.Begin("", StoreFilter.Installed, false); model.Begin("", StoreFilter.All, false);
Require(model.Expanded is null, "explicitly folded item stays folded after filtering");
var receipts = records[..50].Select(r => new InstalledRelease(Guid.NewGuid(), source, "catalog", r)).ToArray();
model.SetReceipts(source, receipts); model.Begin("", StoreFilter.MissingLoaded, false);
Require(!model.Visible.Any() && model.HasMore && model.NextOffset == 50, "a full installed first page doesn't imply remote catalog exhaustion");
model.AcceptPage(0, [records[0]]); Require(model.HasMore && model.NextOffset == 50, "wrong-offset responses do not replace or advance the page");
model.AcceptPage(50, [records[49], records[50]]);
Require(model.Visible.SequenceEqual([records[50]]) && !model.HasMore && model.NextOffset == 52, "pagination counts server rows, deduplicates IDs, and finds a later missing release");
model.Begin("Mapper 3", StoreFilter.Installed, true);
Require(model.Find(records[3].Id) is not null && model.Find(records[11].Id) is null, "installed search uses receipt metadata even for unloaded remote pages");
model.Clear(); Require(!model.HasMore && !model.Visible.Any() && model.Expanded is null && model.Receipt(records[0].Id) is null, "expired source clears both catalog and receipts");

// Deliberately completing cancelled tasks models a transport that has already read its last bytes.
var account = Guid.NewGuid(); var a = new CatalogAccess(source, account, "fixture-A", DateTimeOffset.UtcNow.AddHours(1));
var b = new CatalogAccess(foreign, account, "fixture-B", DateTimeOffset.UtcNow.AddHours(1));
using var slot = new StoreRequest<int>();
var old = new TaskCompletionSource<int>(); var latest = new TaskCompletionSource<int>(); CancellationToken oldToken = default;
slot.Start(a, ct => { oldToken = ct; return old.Task; }); slot.Start(a, _ => latest.Task); old.SetResult(11);
Require(oldToken.IsCancellationRequested && !slot.TryTake(a, out _, out _), "a cancelled late response cannot paint the previous search or previous cover");
latest.SetResult(22); Require(slot.TryTake(a, out var value, out var error) && value == 22 && error is null, "current request applied once");
slot.Start(a, _ => Task.FromResult(33)); Require(!slot.TryTake(b, out _, out _), "new access cannot consume a result from a different source");
slot.Start(a, _ => Task.FromException<int>(new OnlineException("rate_limited")));
Require(slot.TryTake(a, out _, out var code) && code == "rate_limited", "typed transport failure is preserved for the page");
slot.Start(a, _ => Task.FromResult(44)); slot.Clear(); Require(!slot.TryTake(a, out _, out _), "no cover or selection result is published after leaving the catalog");
Console.WriteLine("STORE BROWSE TESTS PASS");
