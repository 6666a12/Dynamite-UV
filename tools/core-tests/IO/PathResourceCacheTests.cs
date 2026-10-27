using DynamiteUniverse.Shared.IO;

namespace CoreTests;

internal static class PathResourceCacheTests
{
    public static void Run(Action<bool, string> check)
    {
        var cache = new PathResourceCache<string>(2);
        check(cache.Capacity == 2 && cache.Count == 0, "resource cache reports configured capacity");

        cache.Set("a", "A");
        cache.Set("b", "B");
        check(cache.TryGet("a", out var first) && first == "A", "resource cache returns stored value");
        cache.Set("c", "C");
        check(!cache.TryGet("b", out _) && cache.TryGet("a", out _) &&
              cache.TryGet("c", out _), "resource cache evicts least recently used entry");

        cache.Set("a", "A2");
        check(cache.Count == 2 && cache.TryGet("a", out var updated) && updated == "A2",
            "resource cache updates existing entry without growing");
        check(cache.Remove("a") && !cache.Remove("a") && cache.Count == 1,
            "resource cache removes entries consistently");
        cache.Clear();
        check(cache.Count == 0 && !cache.TryGet("c", out _), "resource cache clears entries");

        try
        {
            _ = new PathResourceCache<string>(0);
            check(false, "resource cache rejects non-positive capacity");
        }
        catch (ArgumentOutOfRangeException)
        {
            check(true, "resource cache rejects non-positive capacity");
        }
    }
}
