namespace RepoSearch.Core;

/// <summary>
/// Scores how well a repo name matches the query. Pure and deterministic so it can be tested
/// without touching the filesystem or the network.
/// </summary>
public static class MatchScorer
{
    public const double NoMatch = double.NegativeInfinity;

    /// <summary>
    /// Name-match quality in [0, 100]. Returns <see cref="NoMatch"/> when the candidate
    /// should be filtered out entirely.
    /// </summary>
    public static double ScoreName(string candidate, string query)
    {
        if (string.IsNullOrEmpty(query)) return 1;             // empty query lists everything
        if (string.IsNullOrEmpty(candidate)) return NoMatch;

        var c = candidate.ToLowerInvariant();
        var q = query.ToLowerInvariant();

        if (c == q) return 100;
        if (c.StartsWith(q, StringComparison.Ordinal)) return 80 + LengthBonus(q.Length, c.Length);

        // Match at a word boundary ("wyze" in "docker-wyze-bridge") beats a mid-word hit.
        var idx = c.IndexOf(q, StringComparison.Ordinal);
        if (idx > 0 && IsBoundary(c[idx - 1])) return 65 + LengthBonus(q.Length, c.Length);
        if (idx >= 0) return 50 + LengthBonus(q.Length, c.Length);

        // Subsequence ("dwb" -> "docker-wyze-bridge"), the weakest accepted match.
        return IsSubsequence(c, q) ? 25 + LengthBonus(q.Length, c.Length) : NoMatch;
    }

    private static bool IsBoundary(char ch) => ch is '-' or '_' or '.' or '/' or ' ';

    /// <summary>Rewards covering more of the candidate, so "chroma" beats "chromadb-utils" for "chroma".</summary>
    private static double LengthBonus(int queryLen, int candidateLen) =>
        candidateLen == 0 ? 0 : 10.0 * queryLen / candidateLen;

    public static bool IsSubsequence(string text, string pattern)
    {
        var p = 0;
        foreach (var ch in text)
        {
            if (p < pattern.Length && ch == pattern[p]) p++;
            if (p == pattern.Length) return true;
        }
        return pattern.Length == 0;
    }

    /// <summary>
    /// Final ordering key. Kind dominates — the spec's priority list is absolute — and score
    /// only breaks ties inside a kind.
    /// </summary>
    public static double ScoreResult(SearchResult r, string query, DateTimeOffset now)
    {
        // Best name across every identity this result is known by: folder name, remote name,
        // and full "owner/name". Folder names diverge from remote names on this machine
        // (epicor-bpm-general -> epicor-bpm-newPartMessage), so all are considered.
        var best = NoMatch;

        foreach (var local in r.Locals)
            best = Math.Max(best, ScoreName(local.Name, query));

        if (r.Remote is not null)
        {
            best = Math.Max(best, ScoreName(r.Remote.Name, query));
            // Full-name matches count for less: matching the owner shouldn't outrank a repo name.
            best = Math.Max(best, ScoreName(r.Remote.FullName, query) - 15);
        }

        if (double.IsNegativeInfinity(best)) return NoMatch;

        var score = best;

        // Recency: a repo pushed today edges out one untouched for years.
        if (r.Remote?.PushedAt is { } pushed)
        {
            var days = Math.Max(0, (now - pushed).TotalDays);
            score += 8.0 / (1.0 + days / 30.0);
        }

        // Popularity, heavily damped — only meaningful for public results.
        if (r.Kind == ResultKind.PublicRemote && r.Remote is { Stars: > 0 } gh)
            score += Math.Min(10.0, Math.Log10(gh.Stars + 1) * 3.0);

        if (r.Remote?.Archived == true) score -= 12;
        if (r.Remote?.Fork == true && r.Kind != ResultKind.PublicRemote) score -= 4;

        return score;
    }

    /// <summary>Sorts by the spec's priority tiers first, then by score, then stably by name.</summary>
    public static IEnumerable<SearchResult> Order(IEnumerable<SearchResult> results) =>
        results.OrderBy(r => (int)r.Kind)
               .ThenByDescending(r => r.Score)
               .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
}
