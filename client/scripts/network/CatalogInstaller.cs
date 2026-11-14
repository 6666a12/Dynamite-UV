using System.Text.Json;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.IO;

namespace DynamiteUniverse.Network;

public sealed record InstalledRelease(Guid InstallationId, CatalogIdentity Source, string ServerName, StoreRelease Release);
public sealed record InstallProgress(Guid? ReleaseId, string Status, long Received, long Total, string? Error, CatalogIdentity? Source = null);

/// <summary>Application-owned first-install transaction; only complete receipts are scanned. No update/delete.</summary>
public sealed class CatalogInstaller(string root, CatalogTransport transport)
{
    private readonly object _gate = new();
    private InstallProgress _state = new(null, "idle", 0, 0, null);
    private long _revision;
    private bool _running;
    private CancellationTokenSource? _cancellation;
    private StoreRelease? _activeRelease;
    public StoreRelease? ActiveRelease { get { lock (_gate) return _activeRelease; } }
    public void Cancel(CatalogIdentity source, Guid releaseId)
    {
        lock (_gate)
            if (_running && _source == source && _activeRelease?.Id == releaseId) _cancellation?.Cancel();
    }
    public long Revision => Interlocked.Read(ref _revision);
    public InstallProgress State { get { lock (_gate) return _state; } }
    private CatalogIdentity? _source;
    private void Set(InstallProgress state) { lock (_gate) _state = state with { Source = _source }; }
    public string PackagePath(InstalledRelease entry) => Path.Combine(root, "installed", entry.InstallationId.ToString("N"), "pack");
    public IReadOnlyList<InstalledRelease> Installed()
    {
        var folder = Path.Combine(root, "installed");
        if (!Directory.Exists(folder)) return [];
        var entries = new List<InstalledRelease>();
        foreach (var directory in Directory.EnumerateDirectories(folder))
        {
            try
            {
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id) ||
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                var receipt = Path.Combine(directory, "receipt.json");
                if (!File.Exists(receipt) || new FileInfo(receipt).Length > 1024 * 1024 ||
                    (File.GetAttributes(receipt) & FileAttributes.ReparsePoint) != 0) continue;
                var entry = JsonSerializer.Deserialize<InstalledRelease>(File.ReadAllText(receipt));
                if (entry is null || entry.Source is null || entry.Release is null || entry.InstallationId != id || entry.Source.ServerEntryId == Guid.Empty ||
                    entry.Source.InstanceId == Guid.Empty || !Directory.Exists(PackagePath(entry))) continue;
                StoreContract.Validate(entry.Release);
                _ = ServerAddress.Parse(entry.Source.Address, true);
                V2PackageWriter.ValidateDirectoryTree(PackagePath(entry));
                entries.Add(entry);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or OnlineException or V2DiagnosticException or ArgumentException) { }
        }
        return entries;
    }
    public bool Contains(CatalogIdentity source, Guid releaseId) => Installed().Any(e => e.Source == source && e.Release.Id == releaseId);
    public async Task InstallAsync(CatalogAccess access, string serverName, StoreRelease release, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_running) return; // Ignore a duplicate click until the previous transaction finishes cleanup.
            _running = true; _activeRelease = release;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ct = _cancellation.Token;
            _source = access.Identity;
            _state = new(release.Id, "downloading", 0, release.Size, null, _source);
        }
        var stage = Path.Combine(root, "staging", Guid.NewGuid().ToString("N"));
        try
        {
            StoreContract.Validate(release);
            if (release.Display?.ValidationProfile != StoreContract.Profile) throw new OnlineException("unsupported_profile");
            if (Contains(access.Identity, release.Id)) { Set(new(release.Id, "installed", release.Size, release.Size, null)); return; }
            Directory.CreateDirectory(stage);
            var archive = Path.Combine(stage, "download.zip");
            await transport.DownloadAsync(access, release, archive, n => Set(new(release.Id, "downloading", n, release.Size, null)), ct).ConfigureAwait(false);
            Set(new(release.Id, "installing", release.Size, release.Size, null));
            await Task.Run(() =>
            {
                var packRoot = Path.Combine(stage, "pack");
                SafePackageArchive.Extract(archive, packRoot, ct);
                V2PackageWriter.ValidateDirectoryTree(packRoot);
                var pack = V2JsonDecoder.DecodePackFile(Path.Combine(packRoot, "meta.json"));
                // Lightweight identity/reference check, not a second media/semantic publishing audit.
                if (pack.Id != release.PackId || pack.Revision != release.Revision ||
                    !pack.Charts.Select(c => c.Id).Order().SequenceEqual(release.Display.Charts.Select(c => c.ChartId).Order()))
                    throw new OnlineException("integrity_failed");
                foreach (var chart in pack.Charts)
                {
                    _ = V2PackageWriter.ResolveRegularFile(packRoot, chart.File);
                    _ = V2PackageWriter.ResolveRegularFile(packRoot, pack.ResolveAudio(chart));
                }
                if (pack.Cover is not null) _ = V2PackageWriter.ResolveRegularFile(packRoot, pack.Cover);
                ct.ThrowIfCancellationRequested();
                var receipt = new InstalledRelease(Guid.NewGuid(), access.Identity, serverName, release);
                File.WriteAllText(Path.Combine(stage, "receipt.json"), JsonSerializer.Serialize(receipt));
                File.Delete(archive);
                var installed = Path.Combine(root, "installed"); Directory.CreateDirectory(installed);
                lock (_gate)
                {
                    ct.ThrowIfCancellationRequested();
                    Directory.Move(stage, Path.Combine(installed, receipt.InstallationId.ToString("N")));
                    Interlocked.Increment(ref _revision);
                    _state = new(release.Id, "installed", release.Size, release.Size, null, _source);
                }
            }, ct).ConfigureAwait(false);
            Set(new(release.Id, "installed", release.Size, release.Size, null));
        }
        catch (Exception ex) when (ex is OnlineException or InvalidDataException or IOException or UnauthorizedAccessException or OperationCanceledException or V2DiagnosticException or ArgumentException)
        {
            var code = ex is OnlineException online ? online.Code : ex is OperationCanceledException ? "cancelled" :
                ex is InvalidDataException or V2DiagnosticException ? "invalid_package" :
                ex is UnauthorizedAccessException ? "storage_denied" :
                ex is IOException io && (io.HResult & 0xffff) is 112 or 39 or 28 ? "storage_full" : "installation_failed";
            Set(new(release.Id, "failed", 0, release.Size, code));
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            lock (_gate) { _running = false; _cancellation?.Dispose(); _cancellation = null; }
        }
    }
}
