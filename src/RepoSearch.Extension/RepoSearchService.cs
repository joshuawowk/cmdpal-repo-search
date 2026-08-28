using RepoSearch.Core;

namespace RepoSearch.Extension;

/// <summary>
/// Owns all the data behind the page: the local repo index, the GitHub catalog, and the
/// status cache.
///
/// The shape here is driven by measurements on this machine:
///   * scanning 51 repos by reading .git directly ......... 90 ms   -> safe to do inline
///   * listing 245 GitHub repos ........................... 1.5 s   -> cached on disk, TTL'd
///   * one public search .................................. 0.7 s   -> debounced per keystroke
///   * git status for ONE repo ............................ 1.2-23 s -> lazy, cached, never inline
/// So a keystroke only ever filters in-memory data; everything slow happens in the background.
/// </summary>
public sealed class RepoSearchService : IDisposable
{
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan StatusTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LocalRescanInterval = TimeSpan.FromMinutes(2);

    private readonly SettingsManager _settings;
    private readonly StatusCacheStore _statusCache;
    private readonly GitStatusReader _statusReader = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private GitHubClient? _gh;
    private RepoCatalog? _catalog;
    private IReadOnlyList<LocalRepo> _locals = [];
    private DateTimeOffset _localsScannedAt = DateTimeOffset.MinValue;
    private RepoIndex? _index;

    public RepoSearchService(SettingsManager settings)
    {
        _settings = settings;
        _statusCache = new StatusCacheStore(SettingsManager.StatusCachePath);
    }

    /// <summary>Non-fatal problem worth surfacing in the UI (missing token, GitHub unreachable).</summary>
    public string? Warning { get; private set; }

    public TokenSource TokenSource { get; private set; } = TokenSource.None;
    public bool HasGitHub => _gh is not null;
    public string? Login => _catalog?.Login;

    // ------------------------------------------------------------------ initialisation

    /// <summary>
    /// Builds the local index and, when a token is available, the GitHub catalog. Safe to call
    /// repeatedly; the expensive halves are TTL'd.
    /// </summary>
    public async Task EnsureReadyAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RescanLocalsIfStale(now, ct);

            if (_gh is null)
            {
                var (token, source) = TokenStore.Resolve();
                TokenSource = source;

                if (string.IsNullOrEmpty(token))
                {
                    Warning = "No GitHub token. Local repositories only - add a token in settings.";
                    RebuildIndex();
                    return;
                }

                _gh = new GitHubClient(token);
                _catalog = new RepoCatalog(_gh, SettingsManager.CatalogCachePath);
            }

            if (_catalog is not null && (_catalog.IsEmpty || _catalog.IsStale(CatalogTtl, now)))
            {
                try
                {
                    await _catalog.RefreshAsync(now, ct).ConfigureAwait(false);
                    Warning = null;
                }
                catch (GitHubException ex)
                {
                    // Stale cache still beats no results.
                    Warning = _catalog.IsEmpty ? ex.Message : $"{ex.Message} Showing cached repositories.";
                }
                catch (HttpRequestException)
                {
                    Warning = _catalog.IsEmpty
                        ? "Could not reach GitHub. Local repositories only."
                        : "Could not reach GitHub. Showing cached repositories.";
                }
            }

            RebuildIndex();

            // Renames are resolved once and cached forever, so this costs nothing after the
            // first run. Without it, clones pointing at pre-rename URLs never pair up.
            if (_catalog is not null && _index is not null)
            {
                var unmatched = _index.UnmatchedLocalKeys().ToList();
                if (unmatched.Count > 0)
                {
                    try
                    {
                        if (await _catalog.ResolveRenamesAsync(unmatched, ct).ConfigureAwait(false) > 0)
                            RebuildIndex();
                    }
                    catch (GitHubException) { /* keep the unresolved rows rather than failing */ }
                    catch (HttpRequestException) { }
                }
            }
        }
        finally { _initLock.Release(); }
    }

    private void RescanLocalsIfStale(DateTimeOffset now, CancellationToken ct)
    {
        if (_locals.Count > 0 && now - _localsScannedAt < LocalRescanInterval) return;

        var roots = _settings.LocalRoots;
        if (roots.Count == 0) return;

        _locals = new RepoScanner().Scan(roots, _settings.ScanDepth, ct);
        _localsScannedAt = now;
    }

    private void RebuildIndex() =>
        _index = new RepoIndex(
            _locals,
            _catalog?.Repos ?? [],
            _catalog?.Login ?? string.Empty,
            _catalog is null ? null : _catalog.ResolveKey);

    /// <summary>Forces a full refresh on the next query (used by the explicit Refresh command).</summary>
    public void Invalidate()
    {
        _localsScannedAt = DateTimeOffset.MinValue;
        _catalog = null;
        _gh?.Dispose();
        _gh = null;
    }

    // ------------------------------------------------------------------ querying

    /// <summary>True once the index exists, so a synchronous caller can search without blocking.</summary>
    public bool IsWarm => _index is not null;

    private int _warming;

    /// <summary>
    /// Kicks off initialisation without waiting. Used by the global-search fallback handler,
    /// which is called synchronously on a CmdPal worker thread and must never block on IO.
    /// </summary>
    public void WarmInBackground()
    {
        if (Interlocked.Exchange(ref _warming, 1) == 1) return;

        _ = Task.Run(async () =>
        {
            try { await EnsureReadyAsync(DateTimeOffset.UtcNow, CancellationToken.None).ConfigureAwait(false); }
            catch { /* the page surfaces failures; global search just stays empty */ }
            finally { Interlocked.Exchange(ref _warming, 0); }
        });
    }

    /// <summary>Instant, in-memory: local repos plus the cached GitHub catalog. No IO.</summary>
    public List<SearchResult> SearchLocal(string query, DateTimeOffset now, int limit = 60) =>
        _index?.Build(query, publicHits: null, now, limit) ?? [];

    /// <summary>Local results merged with public GitHub hits.</summary>
    public List<SearchResult> Merge(string query, IEnumerable<GitHubRepo>? publicHits, DateTimeOffset now, int limit = 60) =>
        _index?.Build(query, publicHits, now, limit) ?? [];

    /// <summary>
    /// Public repo search. Returns empty rather than throwing so a rate limit or an offline
    /// machine degrades to "your repos only".
    /// </summary>
    public async Task<List<GitHubRepo>> SearchPublicAsync(string query, CancellationToken ct)
    {
        if (_gh is null || !_settings.SearchPublicRepos) return [];
        if (query.Length < 3) return [];

        try
        {
            return await _gh.SearchPublicReposAsync(query, limit: 15, ct).ConfigureAwait(false);
        }
        catch (GitHubException) { return []; }
        catch (HttpRequestException) { return []; }
        catch (TaskCanceledException) { return []; }
    }

    // ------------------------------------------------------------------ status

    /// <summary>Cached status without touching git. Null when we have never computed one.</summary>
    public GitStatusInfo? PeekStatus(string repoPath) => _statusCache.PeekStale(repoPath);

    /// <summary>
    /// Computes status for the given repos, newest-visible-first, honouring the cache.
    /// Runs off the UI path; callers refresh rows when <paramref name="onUpdated"/> fires.
    /// </summary>
    public async Task RefreshStatusAsync(
        IEnumerable<LocalRepo> repos,
        DateTimeOffset now,
        Action<LocalRepo, GitStatusInfo> onUpdated,
        CancellationToken ct)
    {
        if (!_settings.ShowGitStatus) return;

        var pending = new List<LocalRepo>();
        foreach (var repo in repos)
        {
            if (_statusCache.TryGet(repo.Path, StatusTtl, now) is { } fresh)
            {
                onUpdated(repo, fresh);
                continue;
            }
            pending.Add(repo);
        }

        if (pending.Count == 0) return;

        // Bounded parallelism: git spawns are ~670ms of pure overhead each here, so some
        // concurrency helps a lot, but too much thrashes an already slow disk.
        using var gate = new SemaphoreSlim(4);

        var tasks = pending.Select(async repo =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var status = await _statusReader
                    .ReadAsync(repo.Path, _settings.UntrackedMode, TimeSpan.FromSeconds(20), ct)
                    .ConfigureAwait(false);

                if (status.Error is null) _statusCache.Set(repo.Path, status, now);
                onUpdated(repo, status);
            }
            catch (OperationCanceledException) { }
            finally { gate.Release(); }
        });

        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) { }

        _statusCache.Flush();
    }

    public GitHubClient? GitHub => _gh;

    public void Dispose()
    {
        _statusCache.Flush();
        _gh?.Dispose();
        _initLock.Dispose();
    }
}
