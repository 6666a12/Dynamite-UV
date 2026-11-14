using DynamiteUniverse.Network;
using DynamiteUniverse.Shared.Score;

internal static class UploadStateTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var source = new CatalogIdentity(Guid.NewGuid(), "https://a.example/", Guid.NewGuid());
        var account = Guid.NewGuid();
        var token = new CatalogAccess(source, account, "token-a", DateTimeOffset.UtcNow.AddHours(1));
        var report = new PlayReport(Guid.NewGuid(), Guid.NewGuid(), "chart", new string('a', 64), "rules", "standard",
            false, false, true, 900000, 1, 0, 0, 0, 1, 2, false, 90, 100, 90, "completed", DateTimeOffset.UtcNow, false, false, false);
        var fake = new Transport();
        using var upload = new PlayUpload(new(source, account), report, "ready", fake);
        await upload.AttemptAsync(token, true); await upload.AttemptAsync(token, true);
        check(fake.Calls == 1, "One automatic upload per result");
        await upload.AttemptAsync(new(source with { Address = "https://b.example/" }, account, "b", token.ExpiresAt));
        await upload.AttemptAsync(new(source, Guid.NewGuid(), "b", token.ExpiresAt));
        check(fake.Calls == 1 && upload.State.Code == "reauthenticate", "Retry cannot move to another origin or account");
        fake.Next = "reauthenticate"; await upload.AttemptAsync(token);
        await upload.AttemptAsync(token);
        check(fake.Calls == 2, "Rejected token requires a new login token");
        fake.Next = "uploaded";
        await upload.AttemptAsync(new(source, account, "new-token", token.ExpiresAt));
        await upload.AttemptAsync(token);
        check(fake.Calls == 3 && upload.State.Code == "uploaded" && fake.Reports.All(p => ReferenceEquals(p, report)),
            "Original identity reauth retries the identical immutable payload and never duplicates success");
        using var offline = new PlayUpload(null, null, "not_logged_in", fake);
        await offline.AttemptAsync(token);
        check(fake.Calls == 3, "Logged-out run cannot be claimed later");
        var old = new ScoreStoreCore(); var id = new ScoreIdentity("pack", "chart", "rules", "digest");
        old.TryUpdate(id, new ScoreRecord { Score = 800000, Acc = 80 });
        var modes = new ModeScoreStore(); modes.LoadJson(old.SaveJson());
        check(modes.History.Get(id)?.Score == 800000 && modes.For("standard").Get(id) is null && modes.For("hardcore").Get(id) is null,
            "Old scores remain unclassified and do not claim a mode");
        modes.For("standard").TryUpdate(id, new ScoreRecord { Score = 900000, Acc = 90, Bleed = false, Mirror = true });
        modes.For("hardcore").TryUpdate(id, new ScoreRecord { Score = 700000, Acc = 70, Bleed = true, Mirror = false });
        var roundtrip = new ModeScoreStore(); roundtrip.LoadJson(modes.SaveJson());
        check(roundtrip.For("standard").Get(id)?.Score == 900000 && roundtrip.For("hardcore").Get(id)?.Score == 700000 &&
            roundtrip.For("standard").Get(id)?.Mirror == true, "Mode BEST and actual modifiers survive persistence independently");
        var root = Path.Combine(Path.GetTempPath(), "du-score-migration-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "scores.json"); var original = old.SaveJson(); File.WriteAllText(path, original);
            var store = new ModeScoreStore(); var file = new ModeScoreFile(path, store); file.Load(); file.Save();
            var backup = Directory.GetFiles(root, "*.before-modes-*").Single();
            check(File.ReadAllText(backup) == original, "Migration preserves exact old score file before writing");
            var brokenPath = Path.Combine(root, "broken.json"); File.WriteAllText(brokenPath, "{broken");
            var broken = new ModeScoreFile(brokenPath, new ModeScoreStore());
            try { broken.Load(); } catch (System.Text.Json.JsonException) { }
            broken.Save();
            check(File.ReadAllText(Directory.GetFiles(root, "broken.json.recovery-*").Single()) == "{broken",
                "Failed score migration preserves original bytes before later save");
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class Transport : IPlayTransport
    {
        public int Calls; public string Next = "retryable"; public List<PlayReport> Reports = new();
        public Task<UploadStatus> SendAsync(CatalogAccess access, PlayReport report, CancellationToken ct)
        { Calls++; Reports.Add(report); return Task.FromResult(new UploadStatus(Next)); }
    }
}
