namespace GitHub_Commits.Models;

public enum CacheStatus { Hit, ShouldFetch, Wait }

public static class Cache
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, CacheEntry> Dict = new();
    private static readonly HashSet<string> InProgress = new();
    private const int MaxEntries = 5;

    // Proverava se keš i odlučuje se šta nit treba da radi
    // Hit - podaci su u kešu i vraćaju se uz pomoć out parametra
    // ShouldFetch — nijedna nit ne pribavlja podatke za ovaj ključ, treba da se fetch-uje
    // Wait - druga nit već pribavlja informacije, ova treba da sačeka
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

    // Blokira pozivajuću nit dok se poziv za fetch koji je u toku ne završi
    // Vraća podatke iz keša ukoliko ih ima, ukoliko ne, vraća null
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

    // Zove se kad fetch ne uspe i pušta sve ostale niti koji čekaju
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
