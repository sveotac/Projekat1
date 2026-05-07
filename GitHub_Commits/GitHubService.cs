using System.Net;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace GitHub_Commits;

public static class GitHubService
{
    private static readonly HttpClient HttpClient = new();

    public static void Initialize(string? ghToken)
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "GitHub_Commits");
        if (ghToken != null)
            HttpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", ghToken);
    }

    public static async Task<Dictionary<string, int>> FetchCommits(string owner, string repo)
    {
        var statsUrl = $"https://api.github.com/repos/{owner}/{repo}/stats/contributors";
        var res = await HttpClient.GetAsync(statsUrl);

        if (res.StatusCode == HttpStatusCode.NotFound)
            throw new Exception($"Repository '{owner}/{repo}' not found.");
        if (!res.IsSuccessStatusCode && res.StatusCode != HttpStatusCode.Accepted)
            throw new Exception($"GitHub API error: {(int)res.StatusCode} {res.StatusCode}.");

        if (res.StatusCode == HttpStatusCode.OK)
        {
            var contributors = JArray.Parse(await res.Content.ReadAsStringAsync());
            if (contributors.Any())
            {
                var commitCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var contributor in contributors)
                {
                    var login = contributor["author"]?.Value<string>("login") ?? "Unknown";
                    var total = contributor.Value<int>("total");
                    commitCounts[login] = total;
                }
                return commitCounts;
            }
        }

        // 202 (computing) or empty stats — fall back to commits endpoint
        return await FetchCommitsByPage(owner, repo);
    }

    private static async Task<Dictionary<string, int>> FetchCommitsByPage(string owner, string repo)
    {
        var commitCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nextUrl = $"https://api.github.com/repos/{owner}/{repo}/commits?per_page=100";

        while (nextUrl != null)
        {
            var res = await HttpClient.GetAsync(nextUrl);

            if (res.StatusCode == HttpStatusCode.NotFound)
                throw new Exception($"Repository '{owner}/{repo}' not found.");
            if (!res.IsSuccessStatusCode)
                throw new Exception($"GitHub API error: {(int)res.StatusCode} {res.StatusCode}.");

            var commits = JArray.Parse(await res.Content.ReadAsStringAsync());

            foreach (var commit in commits)
            {
                var authorToken = commit["author"];
                var login = (authorToken != null && authorToken.Type == JTokenType.Object)
                    ? authorToken.Value<string>("login") ?? "Unknown"
                    : commit["commit"]?["author"]?.Value<string>("name") ?? "Unknown";

                commitCounts.TryGetValue(login, out var count);
                commitCounts[login] = count + 1;
            }

            nextUrl = GetNextPageUrl(res);
        }

        return commitCounts;
    }

    private static string? GetNextPageUrl(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var linkHeaders))
            return null;

        foreach (var header in linkHeaders)
            foreach (var part in header.Split(','))
            {
                if (!part.Contains("rel=\"next\"")) continue;
                var start = part.IndexOf('<') + 1;
                var end = part.IndexOf('>');
                if (start > 0 && end > start)
                    return part[start..end].Trim();
            }

        return null;
    }
}
