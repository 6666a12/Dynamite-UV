namespace DynamiteUniverse.Shared.IO;

/// <summary>Small bounded path-keyed cache used by runtime resource loaders.</summary>
public sealed class PathResourceCache<T> where T : class
{
    private readonly Dictionary<string, T> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public bool TryGet(string path, out T? value) => _entries.TryGetValue(path, out value);

    public void Set(string path, T value) => _entries[path] = value;

    public bool Remove(string path) => _entries.Remove(path);

    public void Clear() => _entries.Clear();
}
