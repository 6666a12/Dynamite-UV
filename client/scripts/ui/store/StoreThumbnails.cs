using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Page-owned, two-request thumbnail queue with a 64-entry resized texture cache.</summary>
internal sealed class StoreThumbnails : IDisposable
{
    private readonly Dictionary<Guid, Texture2D?> _cache = new();
    private readonly Dictionary<Guid, (Task<byte[]> Task, StoreRelease Release)> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    public Texture2D? Get(Guid id) => _cache.GetValueOrDefault(id);
    public bool Step(IEnumerable<StoreRelease> visible, CatalogAccess access)
    {
        var changed = false;
        foreach (var (id, item) in _pending.ToArray())
        {
            if (!item.Task.IsCompleted) continue;
            _pending.Remove(id); Texture2D? texture = null;
            if (item.Task.IsCompletedSuccessfully && item.Release.Display?.Cover is { } meta)
            {
                var ext = meta.MediaType switch { "image/png" => "png", "image/jpeg" => "jpg", _ => "webp" };
                using var original = Res.LoadTextureBytes(ext, item.Task.Result);
                if (original is not null)
                {
                    using var image = original.GetImage(); image.Resize(96, 96);
                    texture = ImageTexture.CreateFromImage(image);
                }
            }
            else if (item.Task.IsFaulted) { _ = item.Task.Exception; }
            if (_cache.Count >= 64) { var first = _cache.First(); first.Value?.Dispose(); _cache.Remove(first.Key); }
            _cache[id] = texture; changed = true;
        }
        foreach (var release in visible)
        {
            if (_pending.Count >= 2) break;
            if (release.Display?.Cover is null || _cache.ContainsKey(release.Id) || _pending.ContainsKey(release.Id)) continue;
            _pending[release.Id] = (OnlineSession.Catalog.CoverAsync(access, release, _lifetime.Token), release);
        }
        return changed;
    }
    public void Dispose()
    {
        _lifetime.Cancel(); _lifetime.Dispose();
        foreach (var item in _pending.Values) _ = item.Task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        foreach (var texture in _cache.Values) texture?.Dispose();
    }
}
