using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace RepoSearch.Core;

public sealed class StatusCacheEntry
{
    [JsonPropertyName("head")] public string? Head { get; set; }

    /// <summary>Last-write ticks of .git/index — changes whenever staging changes.</summary>
    [JsonPropertyName("indexTicks")] public long IndexTicks { get; set; }

    [JsonPropertyName("computedAt")] public DateTimeOffset ComputedAt { get; set; }
    [JsonPropertyName("status")] public GitStatusInfo? Status { get; set; }
}

public sealed class StatusCache
{
    [JsonPropertyName("entries")]
    public Dictionary<string, StatusCacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Caches git status per repo, keyed by path and validated with a cheap fingerprint.
///
/// This exists because status is the single most expensive thing the extension can do: measured
/// 1.2s-23s per repo on this machine. Reading .git/HEAD and stat-ing .git/index costs ~20-40ms,
/// so we can tell "nothing changed, reuse the cached status" ~50x cheaper than recomputing it.
/// </summary>
public sealed class StatusCacheStore
{
    private readonly string _path;
    private readonly ConcurrentDictionary<string, StatusCacheEntry> _entries;
    private int _dirty;

    public StatusCacheStore(string path)
    {
        _path = path;
        var loaded = JsonStore.Load<StatusCache>(path, RepoSearchJsonContext.Default.StatusCache);
        _entries = new ConcurrentDictionary<string, StatusCacheEntry>(
            loaded?.Entries ?? new Dictionary<string, StatusCacheEntry>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cheap change detector: current HEAD contents plus the .git/index timestamp. Committing,
    /// switching branch, or staging anything moves at least one of them.
    /// </summary>
    public static (string? Head, long IndexTicks) Fingerprint(string repoPath)
    {
        try
        {
            var gitDir = RepoScanner.ResolveGitDir(repoPath);
            if (gitDir is null) return (null, 0);

            string? head = null;
            try { head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim(); } catch { }

            long ticks = 0;
            try
            {
                var index = new FileInfo(Path.Combine(gitDir, "index"));
                if (index.Exists) ticks = index.LastWriteTimeUtc.Ticks;
            }
            catch { }

            return (head, ticks);
        }
        catch { return (null, 0); }
    }

    /// <summary>
    /// Returns the cached status when the repo looks untouched and the entry is younger than
    /// <paramref name="ttl"/>. The TTL still applies because ahead/behind can change on the
    /// remote without anything local moving.
    /// </summary>
    public GitStatusInfo? TryGet(string repoPath, TimeSpan ttl, DateTimeOffset now)
    {
        if (!_entries.TryGetValue(repoPath, out var entry) || entry.Status is null) return null;
        if (now - entry.ComputedAt > ttl) return null;

        var (head, ticks) = Fingerprint(repoPath);
        if (entry.Head != head || entry.IndexTicks != ticks) return null;

        return entry.Status;
    }

    /// <summary>Returns any cached status regardless of age — used to paint a row instantly while a refresh runs.</summary>
    public GitStatusInfo? PeekStale(string repoPath) =>
        _entries.TryGetValue(repoPath, out var entry) ? entry.Status : null;

    public void Set(string repoPath, GitStatusInfo status, DateTimeOffset now)
    {
        var (head, ticks) = Fingerprint(repoPath);
        _entries[repoPath] = new StatusCacheEntry
        {
            Head = head,
            IndexTicks = ticks,
            ComputedAt = now,
            Status = status,
        };
        Interlocked.Exchange(ref _dirty, 1);
    }

    /// <summary>Drops entries for repos that no longer exist, then persists if anything changed.</summary>
    public void Flush()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 0) return;

        foreach (var key in _entries.Keys)
            if (!Directory.Exists(key)) _entries.TryRemove(key, out _);

        JsonStore.Save(_path, new StatusCache { Entries = new Dictionary<string, StatusCacheEntry>(_entries, StringComparer.OrdinalIgnoreCase) },
            RepoSearchJsonContext.Default.StatusCache);
    }
}
