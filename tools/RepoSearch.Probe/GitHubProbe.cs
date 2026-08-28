using System.Diagnostics;
using RepoSearch.Core;

namespace RepoSearch.Probe;

/// <summary>
/// Exercises the GitHub half and the merge/ranking against this machine's real data.
/// The token is read from the environment; it is never stored in source.
/// </summary>
public static class GitHubProbe
{
    public static async Task RunAsync(string root)
    {
        var token = Environment.GetEnvironmentVariable("GH_TOKEN")
                    ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        if (string.IsNullOrWhiteSpace(token))
        {
            Console.WriteLine("\n=== GitHub probe SKIPPED (set GH_TOKEN) ===");
            return;
        }

        using var gh = new GitHubClient(token);

        Console.WriteLine("\n=== GitHub ===");
        var sw = Stopwatch.StartNew();
        var me = await gh.GetAuthenticatedUserAsync();
        Console.WriteLine($"  authenticated as {me.Login} ({me.Name}) in {sw.ElapsedMilliseconds}ms");

        sw.Restart();
        var mine = await gh.ListMyReposAsync();
        sw.Stop();
        Console.WriteLine($"  {mine.Count} repos via /user/repos in {sw.ElapsedMilliseconds}ms " +
                          $"(rate limit remaining {gh.RateLimitRemaining})");
        Console.WriteLine($"  private={mine.Count(r => r.Private)} forks={mine.Count(r => r.Fork)} " +
                          $"archived={mine.Count(r => r.Archived)}");

        var locals = new RepoScanner().Scan([root], maxDepth: 3);

        // Cache + rename resolution, exactly as the extension will do it.
        var cachePath = Path.Combine(Path.GetTempPath(), "cmdpal-repo-search-probe", "catalog.json");
        var catalog = new RepoCatalog(gh, cachePath);
        var now = DateTimeOffset.UtcNow;

        sw.Restart();
        await catalog.RefreshAsync(now);
        Console.WriteLine($"  catalog refreshed + persisted in {sw.ElapsedMilliseconds}ms -> {cachePath}");
        Console.WriteLine($"  cache file size: {new FileInfo(cachePath).Length / 1024}KB");

        var probe = new RepoIndex(locals, catalog.Repos, catalog.Login, catalog.ResolveKey);
        var unmatched = probe.UnmatchedLocalKeys().ToList();
        Console.WriteLine($"  unmatched local origins before rename resolution: {unmatched.Count}");
        foreach (var k in unmatched) Console.WriteLine($"    {k}");

        sw.Restart();
        var renamed = await catalog.ResolveRenamesAsync(unmatched);
        Console.WriteLine($"  resolved {renamed} rename(s) in {sw.ElapsedMilliseconds}ms");

        var index = new RepoIndex(locals, catalog.Repos, catalog.Login, catalog.ResolveKey);
        var stillUnmatched = index.UnmatchedLocalKeys().ToList();
        Console.WriteLine($"  unmatched AFTER resolution: {stillUnmatched.Count}");
        foreach (var k in stillUnmatched) Console.WriteLine($"    {k}  (expected: foreign repos only)");

        Console.WriteLine($"  index: {index.LocalCount} local, {index.RemoteCount} remote");

        foreach (var query in new[] { "epicor", "docker", "feishin", "snap", "airbyte" })
        {
            Console.WriteLine($"\n  --- query \"{query}\" (local + owned only, no public search) ---");
            var results = index.Build(query, publicHits: null, now, limit: 8);
            foreach (var r in results)
                Console.WriteLine($"    [{(int)r.Kind}] {r.Kind,-14} {r.Score,7:F1}  {r.DisplayName,-46} " +
                                  $"{(r.HasLocal ? $"{r.Locals.Count} clone(s)" : "-")}");
        }

        // One live public search, to confirm the 4th result type and measure latency.
        Console.WriteLine("\n  --- query \"kubernetes\" WITH public search ---");
        sw.Restart();
        var hits = await gh.SearchPublicReposAsync("kubernetes", limit: 10);
        sw.Stop();
        Console.WriteLine($"    search returned {hits.Count} in {sw.ElapsedMilliseconds}ms");

        foreach (var r in index.Build("kubernetes", hits, now, limit: 10))
            Console.WriteLine($"    [{(int)r.Kind}] {r.Kind,-14} {r.Score,7:F1}  {r.DisplayName,-46} " +
                              $"{(r.Remote?.Stars is > 0 ? $"★{r.Remote.Stars}" : "")}");
    }
}
