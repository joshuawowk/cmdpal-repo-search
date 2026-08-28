namespace RepoSearch.Core;

/// <summary>
/// Joins the three sources — local clones, my GitHub repos, and public search hits — into the
/// spec's result types, then ranks them.
///
/// The join key is always the normalised lowercase "owner/name" from the remote URL, never the
/// folder name: on this machine four folders disagree with their remote's repo name, and
/// remote owners are spelled both "JoshuaWowk" and "joshuawowk".
/// </summary>
public sealed class RepoIndex
{
    private readonly List<LocalRepo> _locals;
    private readonly Dictionary<string, GitHubRepo> _myRepos;
    private readonly string _myLogin;
    private readonly Func<string, string> _resolveKey;

    /// <param name="resolveKey">
    /// Maps a clone's origin key onto the repo's current key, so a clone whose URL predates a
    /// GitHub rename still pairs with the real repo. Identity by default.
    /// </param>
    public RepoIndex(
        IEnumerable<LocalRepo> locals,
        IEnumerable<GitHubRepo> myRepos,
        string myLogin,
        Func<string, string>? resolveKey = null)
    {
        _locals = locals.ToList();
        _myLogin = myLogin;
        _resolveKey = resolveKey ?? (k => k);
        _myRepos = new Dictionary<string, GitHubRepo>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in myRepos) _myRepos[r.Key] = r;
    }

    /// <summary>Origin keys of local clones that pair with no known repo — feed to rename resolution.</summary>
    public IEnumerable<string> UnmatchedLocalKeys() =>
        _locals.Select(l => l.GitHubKey)
               .Where(k => k is not null)
               .Select(k => _resolveKey(k!))
               .Where(k => !_myRepos.ContainsKey(k))
               .Distinct(StringComparer.OrdinalIgnoreCase);

    public int LocalCount => _locals.Count;
    public int RemoteCount => _myRepos.Count;

    private bool IsMine(string? owner) =>
        owner is not null && string.Equals(owner, _myLogin, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the merged, ranked result list.
    /// <paramref name="publicHits"/> is whatever the GitHub search returned (may be empty when
    /// the search is still in flight — the local half renders immediately either way).
    /// </summary>
    public List<SearchResult> Build(
        string query,
        IEnumerable<GitHubRepo>? publicHits,
        DateTimeOffset now,
        int limit = 60)
    {
        var results = new List<SearchResult>();
        var pairedRemoteKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // --- locals, grouped so one remote with several clones is ONE row -------------------
        // Grouping is on the RESOLVED key, so two clones of the same repo collapse into one row
        // even when one of them still points at the repo's pre-rename URL.
        var withGitHub = _locals.Where(l => l.GitHubKey is not null)
                                .GroupBy(l => _resolveKey(l.GitHubKey!), StringComparer.OrdinalIgnoreCase);

        foreach (var group in withGitHub)
        {
            var clones = group.OrderBy(l => l.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var key = group.Key;
            var owner = clones[0].Origin?.Owner;

            _myRepos.TryGetValue(key, out var remote);

            // Owned if the API listed it, or the URL owner is simply me (covers a repo the
            // listing missed — renamed, or private under an affiliation we didn't request).
            var mine = remote is not null || IsMine(owner);

            results.Add(new SearchResult
            {
                Kind = mine ? ResultKind.PairedOwn : ResultKind.PairedForeign,
                Locals = clones,
                Remote = remote ?? Stub(clones[0].Origin!),
            });
            pairedRemoteKeys.Add(key);
        }

        // Local repos with no GitHub remote (no remote at all, or a non-GitHub host).
        foreach (var local in _locals.Where(l => l.GitHubKey is null))
        {
            results.Add(new SearchResult
            {
                Kind = ResultKind.LocalOnly,
                Locals = [local],
                Remote = null,
            });
        }

        // --- my remotes that have no local clone -------------------------------------------
        foreach (var (key, repo) in _myRepos)
        {
            if (pairedRemoteKeys.Contains(key)) continue;
            results.Add(new SearchResult { Kind = ResultKind.OwnRemote, Remote = repo });
        }

        // --- other people's public repos ----------------------------------------------------
        if (publicHits is not null)
        {
            foreach (var hit in publicHits)
            {
                // Don't show a repo twice: it may already be paired, or already mine.
                if (pairedRemoteKeys.Contains(hit.Key)) continue;
                if (_myRepos.ContainsKey(hit.Key)) continue;
                if (IsMine(hit.OwnerLogin)) continue;

                results.Add(new SearchResult { Kind = ResultKind.PublicRemote, Remote = hit });
            }
        }

        // --- score, filter, order -----------------------------------------------------------
        var scored = new List<SearchResult>(results.Count);
        foreach (var r in results)
        {
            var score = MatchScorer.ScoreResult(r, query, now);
            if (double.IsNegativeInfinity(score)) continue;
            r.Score = score;
            scored.Add(r);
        }

        return MatchScorer.Order(scored).Take(limit).ToList();
    }

    /// <summary>
    /// A placeholder for a remote we know exists (a local clone points at it) but have no API
    /// payload for — someone else's repo we cloned. Enough to render a row and open the web URL.
    /// </summary>
    private static GitHubRepo Stub(GitRemote remote) => new()
    {
        FullName = $"{remote.Owner}/{remote.Name}",
        Name = remote.Name ?? "",
        HtmlUrl = remote.HtmlUrl ?? "",
        CloneUrl = remote.RawUrl,
        Owner = new GitHubOwner { Login = remote.Owner ?? "" },
    };
}
