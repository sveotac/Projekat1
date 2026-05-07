namespace GitHub_Commits.Models;

public enum CacheStatus { Hit, ShouldFetch, Wait }

public static class Cache
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, CacheEntry> Dict = new();
    private static readonly HashSet<string> InProgress = new();
    private const int MaxEntries = 100;

    // Atomically checks the cache and decides what this thread should do.
    // Hit         — entry is in cache, returned via out param
    // ShouldFetch — nobody is fetching this key yet, this thread should fetch
    // Wait        — another thread is already fetching, this thread should wait
    public static CacheStatus Check(string key, out CacheEntry? entry)
    {
        lock (Lock)
        {
            if (Dict.TryGetValue(key, out entry))
            {
                entry.LastAccessed = DateTime.Now;
                return CacheStatus.Hit;
            }

            if (InProgress.Contains(key))
            {
                entry = null;
                return CacheStatus.Wait;
            }

            InProgress.Add(key);
            entry = null;
            return CacheStatus.ShouldFetch;
        }
    }

    // Blocks the calling thread until the in-progress fetch for key completes.
    // Returns the cache entry if the fetch succeeded, null if it failed.
    public static CacheEntry? WaitForResult(string key)
    {
        lock (Lock)
        {
            while (InProgress.Contains(key))
                Monitor.Wait(Lock);

            Dict.TryGetValue(key, out var entry);
            return entry;
        }
    }

    public static void Write(string key, CacheEntry entry)
    {
        lock (Lock)
        {
            Dict[key] = entry;
            InProgress.Remove(key);
            if (Dict.Count > MaxEntries)
                EvictLeastRecentlyUsed();
            Monitor.PulseAll(Lock);
        }
    }

    // Called when a fetch fails — releases waiting threads so they can handle the error.
    public static void CancelInProgress(string key)
    {
        lock (Lock)
        {
            InProgress.Remove(key);
            Monitor.PulseAll(Lock);
        }
    }

    public static int Count()
    {
        lock (Lock)
            return Dict.Count;
    }

    private static void EvictLeastRecentlyUsed()
    {
        var lruKey = Dict.MinBy(kv => kv.Value.LastAccessed).Key;
        Dict.Remove(lruKey);
    }
}
