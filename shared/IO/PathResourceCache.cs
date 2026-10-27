namespace DynamiteUniverse.Shared.IO;

/// <summary>Small bounded path-keyed cache used by runtime resource loaders.</summary>
public sealed class PathResourceCache<T> where T : class
{
    private sealed class Entry
    {
        public required string Path;
        public required T Value;
    }

    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _lru = new();

    public PathResourceCache(int capacity = 16)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
                "Cache capacity must be positive.");
        _capacity = capacity;
    }

    public int Count => _entries.Count;
    public int Capacity => _capacity;

    public bool TryGet(string path, out T? value)
    {
        if (!_entries.TryGetValue(path, out var node))
        {
            value = null;
            return false;
        }

        _lru.Remove(node);
        _lru.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    public void Set(string path, T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_entries.TryGetValue(path, out var existing))
        {
            existing.Value.Value = value;
            _lru.Remove(existing);
            _lru.AddFirst(existing);
            return;
        }

        var node = _lru.AddFirst(new Entry { Path = path, Value = value });
        _entries.Add(path, node);
        if (_entries.Count <= _capacity)
            return;

        var oldest = _lru.Last!;
        _lru.RemoveLast();
        _entries.Remove(oldest.Value.Path);
    }

    public bool Remove(string path)
    {
        if (!_entries.Remove(path, out var node))
            return false;
        _lru.Remove(node);
        return true;
    }

    public void Clear()
    {
        _entries.Clear();
        _lru.Clear();
    }
}
