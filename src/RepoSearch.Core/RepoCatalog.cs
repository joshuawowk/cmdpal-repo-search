using System.Text.Json.Serialization;

namespace RepoSearch.Core;

public sealed class CatalogCache
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
    [JsonPropertyName("fetchedAt")] public DateTimeOffset FetchedAt { get; set; }
    [JsonPropertyName("repos")] public List<GitHubRepo> Repos { get; set; } = [];

    /// <summary>
    /// Maps a stale "owner/name" from a local clone's origin URL onto the repo's current
    /// full name. Renames are permanent, so these entries never expire.
    /// </summary>
    [JsonPropertyName("renames")] public Dictionary<string, string> Renames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keys confirmed to resolve to nothing (deleted repos), so we stop re-querying them.</summary>
    [JsonPropertyName("missing")] public List<string> Missing { get; set; } = [];
}

/// <summary>
/// Owns the authenticated user's repo list and keeps it on disk so search never waits on the
/// network. Measured: /user/repos returns 245 repos in ~1.8s, far too slow per keystroke.
/// </summary>
public sealed class RepoCatalog
{
    private readonly GitHubClient _gh;
    private readonly string _cachePath;
    private CatalogCache _cache;

    public RepoCatalog(GitHubClient gh, string cachePath)
    {
        _gh = gh;
        _cachePath = cachePath;
        _cache = JsonStore.Load(cachePath, RepoSearchJsonContext.Default.CatalogCache) ?? new CatalogCache();
    }

    public string Login => _cache.Login;
    public IReadOnlyList<GitHubRepo> Repos => _cache.Repos;
    public DateTimeOffset FetchedAt => _cache.FetchedAt;
    public bool IsEmpty => _cache.Repos.Count == 0;

    public bool IsStale(TimeSpan ttl, DateTimeOffset now) => now - _cache.FetchedAt > ttl;

    /// <summary>Refreshes the owned-repo list from GitHub and persists it.</summary>
    public async Task RefreshAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var me = await _gh.GetAuthenticatedUserAsync(ct).ConfigureAwait(false);
        var repos = await _gh.ListMyReposAsync(ct: ct).ConfigureAwait(false);

        _cache = new CatalogCache
        {
            Login = me.Login,
            FetchedAt = now,
            Repos = repos,
            Renames = _cache.Renames,
            Missing = _cache.Missing,
        };
        JsonStore.Save(_cachePath, _cache, RepoSearchJsonContext.Default.CatalogCache);
    }

    /// <summary>Applies a known rename, if we've recorded one for this key.</summary>
    public string ResolveKey(string key) =>
        _cache.Renames.TryGetValue(key, out var current) ? current.ToLowerInvariant() : key;

    /// <summary>
    /// For local clones whose origin doesn't match any owned repo, asks GitHub directly:
    /// GET /repos/{owner}/{name} follows renames, so a stale clone URL still resolves.
    /// Results are cached permanently (renames) or as tombstones (deleted repos).
    ///
    /// Real example on this machine: a clone pointing at joshuawowk/drone-sentinel resolves to
    /// joshuawowk/SentinelRF_Detector.
    /// </summary>
    public async Task<int> ResolveRenamesAsync(IEnumerable<string> unmatchedKeys, CancellationToken ct = default)
    {
        var known = _cache.Repos.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = _cache.Missing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolved = 0;

        foreach (var key in unmatchedKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            if (_cache.Renames.ContainsKey(key) || missing.Contains(key)) continue;

            var slash = key.IndexOf('/');
            if (slash <= 0) continue;

            GitHubRepo? repo;
            try
            {
                repo = await _gh.TryGetRepoAsync(key[..slash], key[(slash + 1)..], ct).ConfigureAwait(false);
            }
            catch (GitHubException)
            {
                continue;   // transient; retry on a later refresh rather than tombstoning
            }

            if (repo is null)
            {
                _cache.Missing.Add(key);
                missing.Add(key);
                continue;
            }

            if (!string.Equals(repo.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                _cache.Renames[key] = repo.FullName;
                resolved++;
            }

            // Pick up repos the listing didn't include (renamed, or an affiliation we didn't ask for).
            if (known.Add(repo.Key)) _cache.Repos.Add(repo);
        }

        if (resolved > 0 || missing.Count != _cache.Missing.Count) JsonStore.Save(_cachePath, _cache, RepoSearchJsonContext.Default.CatalogCache);
        return resolved;
    }
}
