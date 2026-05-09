using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using DotNetEnv;
using GitHub_Commits.Models;
using Newtonsoft.Json;

namespace GitHub_Commits;

public class Program
{
    private static string _frontendRoot = "";
    private static readonly RequestQueue RequestQueue = new(maxSize: 100);
    private static readonly Logger Log = Logger.Instance;
    private const int WorkerCount = 4;

    public static async Task Main(string[] args)
    {
        var root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
        Env.Load(Path.Combine(root, ".env"));
        _frontendRoot = Path.Combine(root, "Frontend");

        GitHubService.Initialize(Environment.GetEnvironmentVariable("GH_TOKEN"));

        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
                 RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" :
                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Unknown OS";
        Log.Info($"Running on: {os} ({RuntimeInformation.OSDescription})");

        for (int i = 0; i < WorkerCount; i++)
        {
            var worker = new Thread(ProcessRequests) { IsBackground = true };
            worker.Start();
        }
        Log.Info($"{WorkerCount} worker threads started.");

        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:1738/");
        listener.Start();
        Log.Info("Server running at http://localhost:1738/");

        while (true)
        {
            var context = await listener.GetContextAsync();
            RequestQueue.Enqueue(context);
        }
    }

    private static void ProcessRequests()
    {
        while (true)
        {
            var context = RequestQueue.Dequeue();
            HandleRequest(context).GetAwaiter().GetResult();
        }
    }

    private static async Task HandleRequest(HttpListenerContext context)
    {
        var response = context.Response;
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var query = context.Request.Url?.Query.TrimStart('?') ?? "";

        try
        {
            if (string.IsNullOrEmpty(query))
            {
                await ServeStaticFile(response, path);
                return;
            }

            var queryParams = ParseQuery(query);

            if (!queryParams.TryGetValue("owner", out var owner) || string.IsNullOrEmpty(owner))
                throw new Exception("Missing required parameter: owner.");
            if (!queryParams.TryGetValue("repo", out var repo) || string.IsNullOrEmpty(repo))
                throw new Exception("Missing required parameter: repo.");

            var cacheKey = $"{owner}/{repo}";
            var sw = Stopwatch.StartNew();

            Dictionary<string, int> commitCounts;
            bool fromCache;

            switch (Cache.Check(cacheKey, out var cached))
            {
                case CacheStatus.Hit:
                    commitCounts = cached!.CommitCounts;
                    fromCache = true;
                    Log.Cache($"HIT  {cacheKey}");
                    break;

                case CacheStatus.ShouldFetch:
                    try
                    {
                        commitCounts = await GitHubService.FetchCommits(owner, repo);
                        Cache.Write(cacheKey, new CacheEntry(commitCounts));
                        fromCache = false;
                        Log.Cache($"MISS {cacheKey} — cache size: {Cache.Count()}");
                    }
                    catch
                    {
                        Cache.CancelInProgress(cacheKey);
                        throw;
                    }
                    break;

                case CacheStatus.Wait:
                    Log.Cache($"WAIT {cacheKey} — concurrent fetch in progress");
                    cached = Cache.WaitForResult(cacheKey);
                    if (cached == null)
                        throw new Exception("Concurrent fetch failed. Please try again.");
                    commitCounts = cached.CommitCounts;
                    fromCache = true;
                    break;

                default:
                    throw new Exception("Unexpected cache state.");
            }

            sw.Stop();
            var json = BuildJsonResponse(owner, repo, commitCounts, fromCache, sw.Elapsed.TotalMilliseconds);
            await SendResponse(response, json, HttpStatusCode.OK, "application/json; charset=utf-8");
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
            var errorJson = JsonConvert.SerializeObject(new { message = e.Message });
            await SendResponse(response, errorJson, HttpStatusCode.BadRequest, "application/json; charset=utf-8");
        }
    }

    private static async Task ServeStaticFile(HttpListenerResponse response, string path)
    {
        var fileName = path == "/" ? "index.html" : path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.Combine(_frontendRoot, fileName);

        if (!File.Exists(filePath))
        {
            await SendResponse(response, "Not found.", HttpStatusCode.NotFound, "text/plain");
            return;
        }

        var contentType = Path.GetExtension(fileName).ToLower() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            _ => "application/octet-stream"
        };

        var bytes = await File.ReadAllBytesAsync(filePath);
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        response.StatusCode = 200;
        await response.OutputStream.WriteAsync(bytes);
        response.OutputStream.Close();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>();
        foreach (var part in query.Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2)
                result[kv[0]] = Uri.UnescapeDataString(kv[1]);
        }
        return result;
    }

    private static string BuildJsonResponse(
        string owner, string repo,
        Dictionary<string, int> commitCounts,
        bool fromCache, double totalTimeMs)
    {
        var contributors = commitCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new { login = kv.Key, commits = kv.Value })
            .ToList();

        return JsonConvert.SerializeObject(new
        {
            owner,
            repo,
            contributors,
            totalCommits = commitCounts.Values.Sum(),
            fromCache,
            totalTimeMs = Math.Round(totalTimeMs, 2)
        });
    }

    private static async Task SendResponse(
        HttpListenerResponse response, string body, HttpStatusCode statusCode, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        response.StatusCode = (int)statusCode;
        await response.OutputStream.WriteAsync(bytes);
        response.OutputStream.Close();
    }
}
