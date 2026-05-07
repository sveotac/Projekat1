namespace GitHub_Commits.Models;

public class CacheEntry
{
    public Dictionary<string, int> CommitCounts { get; set; }
    public DateTime LastAccessed { get; set; }

    public CacheEntry(Dictionary<string, int> commitCounts)
    {
        CommitCounts = commitCounts;
        LastAccessed = DateTime.Now;
    }

}
