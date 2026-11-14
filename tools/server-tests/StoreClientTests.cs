using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using DynamiteUniverse.Network;
using DynamiteUniverse.Shared.IO;

internal static class StoreClientTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "du-store-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            foreach (var bad in new[] { "../escape.json", "/absolute.json", "a\\b.json", "CON.json", "a/../b.json", "a:", "a/", "script.cs", "chart?.json", "chart*.json", "chart|.json", "chart<.json", "chart>.json", "chart\".json" })
            {
                var archive = Path.Combine(root, Guid.NewGuid() + ".zip");
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                { using var output = zip.CreateEntry(bad).Open(); output.Write("{}"u8); }
                var rejected = false;
                try { SafePackageArchive.Extract(archive, Path.Combine(root, Guid.NewGuid().ToString())); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected, "Client extraction refuses unsafe entry " + bad);
            }
            var source = new CatalogIdentity(Guid.NewGuid(), "https://store.example/", Guid.NewGuid());
            var access = new CatalogAccess(source, Guid.NewGuid(), new string('a', 64), DateTimeOffset.UtcNow.AddHours(1));
            var release = new StoreRelease(Guid.NewGuid(), "pack", "1", "Title", "Artist", 1, 3,
                Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })), "fixture", DateTimeOffset.UtcNow,
                new(StoreContract.Profile, [new("chart", "hard", null, 1, false, ["Author"], 120, 120)], null));
            using var redirect = new CatalogTransport(new Reply(HttpStatusCode.Redirect, []));
            try { await redirect.ListAsync(access, "", 0, default); check(false, "Redirect rejected"); }
            catch (OnlineException ex) { check(ex.Code == "redirect_refused", "Catalog refuses credential redirects"); }
            using var corrupt = new CatalogTransport(new Reply(HttpStatusCode.OK, [1, 2, 4]));
            var installer = new CatalogInstaller(Path.Combine(root, "installs"), corrupt);
            check(installer.Installed(_ => throw new UnauthorizedAccessException()).Count == 0,
                "Unlistable installation root does not break library loading");
            var receipt = new InstalledRelease(Guid.NewGuid(), source, "Fixture", release);
            var receiptDir = Path.Combine(root, "installs", "installed", receipt.InstallationId.ToString("N"));
            Directory.CreateDirectory(Path.Combine(receiptDir, "pack"));
            File.WriteAllText(Path.Combine(receiptDir, "receipt.json"), System.Text.Json.JsonSerializer.Serialize(receipt));
            IEnumerable<string> InterruptedScan(string _)
            {
                yield return receiptDir;
                throw new DirectoryNotFoundException();
            }
            check(installer.Installed(InterruptedScan).Count == 1, "Mid-scan I/O failure preserves safely collected receipts");
            Directory.Delete(receiptDir, true);
            await installer.InstallAsync(access, "Server", release);
            check(installer.State.Error == "integrity_failed" && installer.Installed().Count == 0,
                "Wrong archive hash never produces installed receipt");
            using var malformed = new CatalogTransport(new Reply(HttpStatusCode.OK, [1, 2, 3]));
            var invalidInstaller = new CatalogInstaller(Path.Combine(root, "malformed"), malformed);
            await invalidInstaller.InstallAsync(access, "Server", release);
            check(invalidInstaller.State.Status == "failed" && invalidInstaller.Installed().Count == 0,
                "Malformed ZIP with correct hash never becomes selectable");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await invalidInstaller.InstallAsync(access, "Server", release, cancelled.Token);
            check(invalidInstaller.Installed().Count == 0, "Cancelled installation has no published receipt");
            using var waiting = new WaitingReply(); using var slow = new CatalogTransport(waiting);
            var cancellable = new CatalogInstaller(Path.Combine(root, "cancel-active"), slow);
            var work = cancellable.InstallAsync(access, "Server", release);
            await waiting.Started.Task;
            cancellable.Cancel(source with { InstanceId = Guid.NewGuid() }, release.Id);
            check(!work.IsCompleted, "Other server cannot cancel the active download");
            cancellable.Cancel(source, release.Id); await work;
            check(cancellable.State.Error == "cancelled" && cancellable.Installed().Count == 0,
                "Active cancellation removes staging without publishing an installation");
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class WaitingReply : HttpMessageHandler
    {
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Cancelled wait should not return");
        }
    }
    private sealed class Reply(HttpStatusCode status, byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new("application/zip");
            return Task.FromResult(response);
        }
    }
}
