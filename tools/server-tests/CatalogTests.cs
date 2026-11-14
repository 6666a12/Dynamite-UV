using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DynamiteUniverse.Server.Accounts;
using DynamiteUniverse.Server.Catalog;
using DynamiteUniverse.Server.Records;
using DynamiteUniverse.Server.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

internal static class CatalogTests
{
    internal static async Task Run(string connection, Guid instance, CancellationToken ct)
    {
        var root = Path.Combine(Path.GetTempPath(), "du-catalog-test-" + Guid.NewGuid().ToString("N"));
        await using var app = ServerApplication.Build([], b =>
        {
            b.WebHost.UseUrls("http://127.0.0.1:0");
            b.Configuration.AddInMemoryCollection(new Dictionary<string,string?> {
                ["Instance:Id"] = instance.ToString(), ["ConnectionStrings:Postgres"] = connection, ["Storage:CatalogRoot"] = root });
        });
        await app.StartAsync(ct);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            using var unauthenticated = await http.GetAsync("/api/v1/catalog/packs", ct);
            Check(unauthenticated.StatusCode == HttpStatusCode.Unauthorized, "Catalog requires login");
            async Task<LoginResponse> Login(string name)
            {
                using var registration = await http.PostAsJsonAsync("/api/v1/accounts/register", new Credentials(name,"catalog-fixture-password"), ct);
                using var response = await http.PostAsJsonAsync("/api/v1/accounts/login", new Credentials(name,"catalog-fixture-password"), ct);
                return (await response.Content.ReadFromJsonAsync<LoginResponse>(ct))!;
            }
            var admin = await Login("catalog_admin"); var user = await Login("catalog_reader");
            var bytes = Fixture(cover: true);
            async Task<HttpResponseMessage> Publish(byte[] content, string version)
            {
                var request = new HttpRequestMessage(HttpMethod.Post,"/api/v1/admin/catalog/releases?version="+version);
                request.Headers.Add("X-Content-Rights", "Synthetic clean-room fixture; generated silence and repository golden charts");
                request.Content = new ByteArrayContent(content); request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                using (request) return await http.SendAsync(request,ct);
            }
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",user.AccessToken);
            using (var deniedList = await http.GetAsync("/api/v1/admin/catalog/releases", ct))
                Check(deniedList.StatusCode == HttpStatusCode.Forbidden, "Normal account cannot list administrative releases");
            using (var denied = await Publish(bytes,"v1")) Check(denied.StatusCode == HttpStatusCode.Forbidden,"Normal account cannot publish");
            await using var database = NpgsqlDataSource.Create(connection);
            await using (var role = database.CreateCommand("UPDATE du.accounts SET role='admin' WHERE id=$1"))
            { role.Parameters.AddWithValue(admin.Account.Id); await role.ExecuteNonQueryAsync(ct); }
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",admin.AccessToken);
            using var inspectionCancellation = new CancellationTokenSource();
            using (var body = new CancelAtEndStream(bytes, inspectionCancellation))
            {
                try
                {
                    await app.Services.GetRequiredService<CatalogService>().PublishAsync(admin.Account.Id, "cancelled", "Fixture", body, inspectionCancellation.Token);
                    throw new Exception("Cancelled publication unexpectedly completed");
                }
                catch (OperationCanceledException)
                {
                    Check(!Directory.EnumerateDirectories(Path.Combine(root, ".staging")).Any(),
                        "Cancellation before inspection removes publication staging");
                }
            }
            // Success immediately after cancellation also checks release of the singleton publish slot.
            using var publish = await Publish(bytes,"v1");
            Check(publish.StatusCode == HttpStatusCode.Created,"Validated package publishes");
            var release = (await publish.Content.ReadFromJsonAsync<CatalogRelease>(ct))!;
            using (var duplicate = await Publish(bytes,"v1")) Check(duplicate.StatusCode == HttpStatusCode.Conflict,"Immutable release version rejects duplicate");
            using (var invalid = await Publish(Fixture(true),"bad")) Check(invalid.StatusCode == HttpStatusCode.BadRequest,"Archive traversal rejected");
            using (var invalidCover = await Publish(Fixture(badCover: true), "bad-cover"))
                Check(invalidCover.StatusCode == HttpStatusCode.BadRequest, "Server rejects undecodable cover before publication");
            using (var invalidName = await Publish(Fixture(invalidFilename: true), "bad-filename"))
                Check(invalidName.StatusCode == HttpStatusCode.BadRequest, "Publication rejects referenced Windows-invalid filename");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",user.AccessToken);
            using (var deniedWithdrawal = await http.PostAsJsonAsync($"/api/v1/admin/catalog/releases/{release.Id}/withdraw", new Withdrawal("not allowed"), ct))
                Check(deniedWithdrawal.StatusCode == HttpStatusCode.Forbidden,"Normal account cannot withdraw");
            using (var ready = await http.GetAsync("/health/ready", ct))
                Check(ready.StatusCode == HttpStatusCode.OK,"Migrated instance reports backend readiness");
            var list = await http.GetFromJsonAsync<CatalogRelease[]>("/api/v1/catalog/packs",ct);
            Check(list?.Length == 1 && list[0].Id == release.Id,"Published list uses database metadata");
            var detail = (await http.GetFromJsonAsync<ReleaseDetails>($"/api/v1/catalog/releases/{release.Id}",ct))!;
            var download = await http.GetByteArrayAsync($"/api/v1/catalog/releases/{release.Id}/download",ct);
            Check(download.SequenceEqual(bytes),"Download returns exact validated archive");
            using (var client = new DynamiteUniverse.Network.CatalogTransport())
            {
                var source = new DynamiteUniverse.Network.CatalogIdentity(Guid.NewGuid(), http.BaseAddress!.AbsoluteUri, instance);
                var access = new DynamiteUniverse.Network.CatalogAccess(source, user.Account.Id, user.AccessToken, user.ExpiresAt);
                var releases = await client.ListAsync(access, "", 0, ct);
                Check(releases[0].Display?.ValidationProfile == "desktop-mobile-media-1" && releases[0].Display!.Charts.Length == 2,
                    "Store receives audited chart display metadata");
                var coverBytes = await client.CoverAsync(access, releases[0], ct);
                Check(coverBytes.SequenceEqual(CoverFixture()), "Version-bound cover returns exact audited bytes");
                var installer = new DynamiteUniverse.Network.CatalogInstaller(Path.Combine(root, "client"), client);
                await installer.InstallAsync(access, "Fixture server", releases[0], ct);
                Check(installer.State.Status == "installed" && installer.Installed().Count == 1, "Real client downloads and atomically installs");
                await installer.InstallAsync(access, "Fixture server", releases[0], ct);
                Check(installer.Installed().Count == 1, "Repeated first install reuses receipt");
                var reopened = new DynamiteUniverse.Network.CatalogInstaller(Path.Combine(root, "client"), client);
                Check(reopened.Installed().Count == 1, "Completed installation survives installer restart");
                var otherSource = source with { ServerEntryId = Guid.NewGuid(), Address = http.BaseAddress.AbsoluteUri.Replace("127.0.0.1", "localhost") };
                var otherAccess = new DynamiteUniverse.Network.CatalogAccess(otherSource, user.Account.Id, user.AccessToken, user.ExpiresAt);
                await installer.InstallAsync(otherAccess, "Other origin", releases[0], ct);
                Check(installer.Installed().Count == 2 && installer.Installed().Select(e => e.InstallationId).Distinct().Count() == 2,
                    "Same instance release and pack at distinct origins install separately");
            }
            var storedPath = Path.Combine(root, release.Id + ".zip");
            var corrupt = bytes.ToArray(); corrupt[^1] ^= 1;
            await File.WriteAllBytesAsync(storedPath, corrupt, ct);
            using (var damaged = await http.GetAsync($"/api/v1/catalog/releases/{release.Id}/download", ct))
                Check(damaged.StatusCode == HttpStatusCode.ServiceUnavailable,"Same-length file tampering blocks download");
            await File.WriteAllBytesAsync(storedPath, bytes, ct);
            var chart = detail.Charts[0];
            var report = new DynamiteUniverse.Shared.Score.PlayReport(Guid.NewGuid(), release.Id, chart.ChartId, chart.Digest,
                chart.RulesetId, "standard", true, false, true, 900000, 1, 0, 0, 0, 1, 2, false, 90, 100, 90,
                "completed", DateTimeOffset.UtcNow, false, false, false);
            var uploadAccess = new DynamiteUniverse.Network.CatalogAccess(
                new(Guid.NewGuid(), http.BaseAddress!.AbsoluteUri, instance), user.Account.Id, user.AccessToken, user.ExpiresAt);
            var uploadTransport = new DynamiteUniverse.Network.PlayTransport();
            Check((await uploadTransport.SendAsync(uploadAccess, report, ct)).Code == "uploaded", "Client v2 upload persists raw result");
            Check((await uploadTransport.SendAsync(uploadAccess, report, ct)).Code == "uploaded", "V2 exact retry is idempotent");
            var v2History = await http.GetFromJsonAsync<SubmittedPlayV2[]>("/api/v1/me/plays/v2", ct);
            Check(v2History is { Length: 1 } && v2History[0].Play.GetProperty("RawScore").GetDouble() == 90 &&
                v2History[0].Play.GetProperty("Mirror").GetBoolean(), "V2 history retains raw score and modifiers");
            Check((await uploadTransport.SendAsync(uploadAccess, report with { Mirror = false }, ct)).Code == "conflict", "V2 changed modifiers conflict");
            Check((await uploadTransport.SendAsync(uploadAccess, report with { PlayId = Guid.NewGuid(), UsedSeek = true }, ct)).Code == "ineligible", "V2 debug seek run rejected");
            var play = new PlaySubmission(Guid.NewGuid(),release.Id,chart.ChartId,chart.Digest,chart.RulesetId,"standard",false,false,true,900000,1,0,0,0,1);
            async Task<HttpStatusCode> Submit(PlaySubmission p)
            { using var response = await http.PostAsJsonAsync("/api/v1/me/plays/",p,ct); return response.StatusCode; }
            Check(await Submit(play) == HttpStatusCode.Created,"Own play persists");
            Check(await Submit(play) == HttpStatusCode.OK,"Retry is idempotent");
            var concurrent = play with { PlayId = Guid.NewGuid() };
            var parallel = await Task.WhenAll(Submit(concurrent), Submit(concurrent));
            Check(parallel.Count(s => s == HttpStatusCode.Created) == 1 && parallel.Count(s => s == HttpStatusCode.OK) == 1,
                "Concurrent retries produce one record");
            Check(await Submit(play with { Score=800000 }) == HttpStatusCode.Conflict,"Changed retry cannot overwrite play");
            Check(await Submit(play with { PlayId=Guid.NewGuid(), Auto=true }) == HttpStatusCode.BadRequest,"Auto play is ineligible");
            Check(await Submit(play with { PlayId=Guid.NewGuid(), Digest=new string('0',64) }) == HttpStatusCode.UnprocessableEntity,"Mismatched chart digest rejected");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",admin.AccessToken);
            var others = await http.GetFromJsonAsync<SubmittedPlay[]>("/api/v1/me/plays/",ct);
            Check(others?.Length==0,"Records cannot cross account identity");
            using var withdrawal = await http.PostAsJsonAsync($"/api/v1/admin/catalog/releases/{release.Id}/withdraw",new Withdrawal("test withdrawal"),ct);
            Check(withdrawal.StatusCode == HttpStatusCode.NoContent,"Admin withdrawal succeeds");
            var adminList = await http.GetFromJsonAsync<AdminRelease[]>("/api/v1/admin/catalog/releases", ct);
            Check(adminList is { Length: 1 } && !adminList[0].Published, "Admin listing includes withdrawn releases");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",user.AccessToken);
            using var withdrawnDownload = await http.GetAsync($"/api/v1/catalog/releases/{release.Id}/download",ct);
            Check(withdrawnDownload.StatusCode == HttpStatusCode.NotFound,"Withdrawn release cannot download");
            Check((await uploadTransport.SendAsync(uploadAccess, report, ct)).Code == "uploaded", "Saved v2 report retries after withdrawal");
            Check((await uploadTransport.SendAsync(uploadAccess, report with { PlayId = Guid.NewGuid() }, ct)).Code == "release_unavailable", "New v2 report rejected after withdrawal");
            Check(await Submit(play) == HttpStatusCode.OK,"Exact retry survives release withdrawal");
            Check(await Submit(play with { PlayId=Guid.NewGuid() }) == HttpStatusCode.UnprocessableEntity,"New play on withdrawn release rejected");
        }
        finally { await app.StopAsync(CancellationToken.None); if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
    private static byte[] Fixture(bool unsafePath=false, bool badCover=false, bool cover=false, bool invalidFilename=false)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while(repo is not null && !File.Exists(Path.Combine(repo.FullName,"AGENTS.md"))) repo=repo.Parent;
        var golden = Path.Combine(repo!.FullName,"schemas/chart-format-v2/examples/golden-pack");
        using var memory = new MemoryStream();
        using(var zip = new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            if (badCover || cover)
            {
                using var image = zip.CreateEntry("cover.png").Open();
                image.Write(badCover ? "not a real image"u8.ToArray() : CoverFixture());
            }
            foreach(var name in new[]{"meta.json","chart_hard.json","chart_custom.json"})
            {
                using var output = zip.CreateEntry(invalidFilename && name == "chart_hard.json" ? "chart?.json" : name).Open();
                var text = File.ReadAllText(Path.Combine(golden, name));
                if (invalidFilename && name == "meta.json") text = text.Replace("chart_hard.json", "chart?.json");
                if ((badCover || cover) && name == "meta.json") text = text.Replace("\"audio\": \"audio.wav\",", "\"audio\": \"audio.wav\",\n  \"cover\": \"cover.png\",");
                output.Write(System.Text.Encoding.UTF8.GetBytes(text));
            }
            using(var audio = zip.CreateEntry("audio.wav").Open())
            using(var writer = new BinaryWriter(audio))
            {
                const int length=8000*2*60;
                writer.Write("RIFF"u8); writer.Write(36+length); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((short)1);writer.Write((short)1);writer.Write(8000);writer.Write(16000);writer.Write((short)2);writer.Write((short)16);
                writer.Write("data"u8);writer.Write(length);writer.Write(new byte[length]);
            }
            if(unsafePath) { using var bad=zip.CreateEntry("../escape.json").Open(); bad.Write("{}"u8); }
        }
        return memory.ToArray();
    }
    private static byte[] CoverFixture()
    {
        using var png = new MemoryStream(); png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string name, byte[] data)
        {
            Span<byte> number = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length); png.Write(number);
            var type = System.Text.Encoding.ASCII.GetBytes(name); png.Write(type); png.Write(data);
            uint crc = 0xffffffff;
            foreach (var b in type.Concat(data))
            {
                crc ^= b;
                for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); png.Write(number);
        }
        Chunk("IHDR", [0,0,0,1,0,0,0,1,8,2,0,0,0]);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, true)) zlib.Write(new byte[] { 0, 40, 70, 90 });
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []); return png.ToArray();
    }
    private sealed class CancelAtEndStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var count = await base.ReadAsync(buffer, ct);
            if (count == 0) cancellation.Cancel();
            return count;
        }
    }
    private static void Check(bool condition,string message)
    { if(!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS "+message); }
}
