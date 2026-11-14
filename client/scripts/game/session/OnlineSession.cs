using System.Text.Json;
using Godot;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Game;

internal sealed record SavedServer(Guid Id, string Address, Guid InstanceId);

/// <summary>Application-owned in-memory session; only non-secret server entries are persisted.</summary>
internal static class OnlineSession
{
    private static readonly AccountTransport Transport = new();
    internal static readonly CatalogTransport Catalog = new();
    private static CatalogInstaller? _installer;
    internal static CatalogInstaller Installer => _installer ??= new(ProjectSettings.GlobalizePath("user://online"), Catalog);
    internal static CatalogAccess CaptureCatalog()
    {
        Load();
        var state = Connection.State;
        if (state.Server is null || state.Descriptor is null || state.NeedsConfirmation)
            throw new OnlineException("server_not_saved");
        var entry = Entries.FirstOrDefault(e => e.Address == state.Server.BaseUri.AbsoluteUri && e.InstanceId == state.Descriptor.InstanceId);
        if (entry is null)
        {
            if (!SaveConfirmed()) throw new OnlineException("server_not_saved");
            entry = Entries.Single(e => e.Address == state.Server.BaseUri.AbsoluteUri);
        }
        return Connection.CaptureCatalog(entry.Id);
    }
    internal static readonly AccountConnection Connection = new(Transport);
    private static readonly List<SavedServer> Entries = new();
    private static bool _loaded;
    internal static IReadOnlyList<SavedServer> Servers
    {
        get { Load(); return Entries; }
    }
    internal static bool AllowLocalHttp => OS.HasFeature("editor") || OS.HasFeature("internal_testdata");
    private static string StorePath => ProjectSettings.GlobalizePath("user://servers.json");
    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(StorePath)) return;
            if (new FileInfo(StorePath).Length > 64 * 1024) return;
            var entries = JsonSerializer.Deserialize<SavedServer[]>(File.ReadAllText(StorePath));
            foreach (var entry in (entries ?? []).Take(20))
            {
                try
                {
                    if (entry is null || string.IsNullOrWhiteSpace(entry.Address)) continue;
                    var address = ServerAddress.Parse(entry.Address, AllowLocalHttp);
                    if (entry.Id != Guid.Empty && entry.InstanceId != Guid.Empty && !Entries.Any(e => e.Address == address.BaseUri.AbsoluteUri))
                        Entries.Add(entry with { Address = address.BaseUri.AbsoluteUri });
                }
                catch (OnlineException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { GD.PushWarning("Online: saved server list is unavailable; credentials are never stored here."); }
    }
    internal static void Select(string address)
    {
        Load();
        var server = ServerAddress.Parse(address, AllowLocalHttp);
        var saved = Entries.FirstOrDefault(e => e.Address == server.BaseUri.AbsoluteUri);
        Connection.Select(server, saved?.InstanceId);
    }
    internal static bool SaveConfirmed()
    {
        var state = Connection.State;
        if (state.Server is null || state.Descriptor is null || state.NeedsConfirmation) return false;
        Load();
        var address = state.Server.BaseUri.AbsoluteUri;
        var index = Entries.FindIndex(e => e.Address == address);
        if (index < 0 && Entries.Count >= 20) return false;
        var entry = new SavedServer(index >= 0 ? Entries[index].Id : Guid.NewGuid(), address, state.Descriptor.InstanceId);
        var next = Entries.ToList();
        if (index < 0) next.Add(entry); else next[index] = entry;
        try
        {
            DynamiteUniverse.Game.SettingsFilePersistence.Write(StorePath, JsonSerializer.Serialize(next));
            Entries.Clear(); Entries.AddRange(next); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { GD.PushWarning("Online: could not save server address list."); return false; }
    }
}
