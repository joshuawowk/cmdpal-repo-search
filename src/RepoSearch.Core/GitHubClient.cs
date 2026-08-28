using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RepoSearch.Core;

public sealed class GitHubException(string message, HttpStatusCode? status = null)
    : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// A thin GitHub REST client over HttpClient + System.Text.Json.
///
/// Deliberately not Octokit: this ships inside an MSIX where every transitive assembly is
/// payload, and we use ~8 endpoints. Hand-rolling keeps the extension small and lets us
/// control caching, cancellation and rate-limit handling precisely.
/// </summary>
public sealed class GitHubClient : IDisposable
{
    private const string ApiRoot = "https://api.github.com";
    private readonly HttpClient _http;

    public GitHubClient(string token, HttpMessageHandler? handler = null)
    {
        // Redirects are followed by hand below. .NET's automatic follower drops the
        // Authorization header, which turns a renamed PRIVATE repo's 301 into a silent 404.
        _http = new HttpClient(
            handler ?? new HttpClientHandler { AllowAutoRedirect = false },
            disposeHandler: true);

        _http.BaseAddress = new Uri(ApiRoot);
        _http.Timeout = TimeSpan.FromSeconds(30);

        var h = _http.DefaultRequestHeaders;
        h.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        h.UserAgent.Add(new ProductInfoHeaderValue("cmdpal-repo-search", "1.0"));
        h.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token))
            h.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static readonly RepoSearchJsonContext Json = RepoSearchJsonContext.Default;

    /// <summary>Remaining core-API calls reported by the last response, if seen.</summary>
    public int? RateLimitRemaining { get; private set; }
    public DateTimeOffset? RateLimitResetsAt { get; private set; }

    private void CaptureRateLimit(HttpResponseMessage res)
    {
        if (res.Headers.TryGetValues("x-ratelimit-remaining", out var rem) &&
            int.TryParse(rem.FirstOrDefault(), out var r))
            RateLimitRemaining = r;

        if (res.Headers.TryGetValues("x-ratelimit-reset", out var reset) &&
            long.TryParse(reset.FirstOrDefault(), out var epoch))
            RateLimitResetsAt = DateTimeOffset.FromUnixTimeSeconds(epoch);
    }

    private const int MaxRedirects = 5;

    /// <summary>
    /// Sends a request, following GitHub's redirects manually.
    ///
    /// A repo that has been renamed answers /repos/{owner}/{name} with 301 -> /repositories/{id}.
    /// .NET's built-in redirect follower strips Authorization, so a renamed PRIVATE repo comes
    /// back 404 and looks deleted. Re-issuing the request ourselves keeps the token attached
    /// (it lives on DefaultRequestHeaders), and we refuse to leave api.github.com so the token
    /// can never be forwarded to another host.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        CaptureRateLimit(res);

        for (var hop = 0; hop < MaxRedirects; hop++)
        {
            if (res.StatusCode is not (HttpStatusCode.MovedPermanently
                                    or HttpStatusCode.Found
                                    or HttpStatusCode.TemporaryRedirect
                                    or HttpStatusCode.PermanentRedirect))
                return res;

            var location = res.Headers.Location;
            if (location is null) return res;

            var target = location.IsAbsoluteUri ? location : new Uri(_http.BaseAddress!, location);
            if (!string.Equals(target.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
                return res;   // never carry the token off GitHub's API host

            // 301/302 downgrade non-GET/HEAD to GET; 307/308 preserve the method and body.
            var preserveMethod = res.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
            var method = preserveMethod || req.Method == HttpMethod.Get || req.Method == HttpMethod.Head
                ? req.Method
                : HttpMethod.Get;

            var next = new HttpRequestMessage(method, target);
            if (preserveMethod && req.Content is not null) next.Content = req.Content;

            res.Dispose();
            res = await _http.SendAsync(next, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            CaptureRateLimit(res);
        }

        return res;
    }

    private static async Task<GitHubException> ToError(HttpResponseMessage res)
    {
        var body = string.Empty;
        try { body = await res.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }

        var detail = res.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "GitHub rejected the token (401). Check the token in settings.",
            HttpStatusCode.Forbidden when body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                => "GitHub rate limit hit. Try again shortly.",
            HttpStatusCode.Forbidden => "GitHub refused the request (403). The token may lack the needed scope.",
            HttpStatusCode.NotFound => "Not found on GitHub (404).",
            _ => $"GitHub returned {(int)res.StatusCode} {res.ReasonPhrase}.",
        };
        return new GitHubException(detail, res.StatusCode);
    }

    public async Task<GitHubUser> GetAuthenticatedUserAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/user");
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw await ToError(res).ConfigureAwait(false);

        return await res.Content.ReadFromJsonAsync(Json.GitHubUser, ct).ConfigureAwait(false)
               ?? throw new GitHubException("Empty /user response.");
    }

    /// <summary>
    /// Every repo the token can see as owner/collaborator/org member, newest push first.
    /// ~240 repos = 3 pages at per_page=100. Cached by the caller; never called per keystroke.
    /// </summary>
    public async Task<List<GitHubRepo>> ListMyReposAsync(
        string affiliation = "owner,collaborator,organization_member",
        int maxPages = 20,
        CancellationToken ct = default)
    {
        var all = new List<GitHubRepo>();

        for (var page = 1; page <= maxPages; page++)
        {
            var url = $"/user/repos?affiliation={Uri.EscapeDataString(affiliation)}&per_page=100&sort=pushed&page={page}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) throw await ToError(res).ConfigureAwait(false);

            var batch = await res.Content.ReadFromJsonAsync(Json.ListGitHubRepo, ct).ConfigureAwait(false);
            if (batch is null || batch.Count == 0) break;

            all.AddRange(batch);
            if (batch.Count < 100) break;
        }

        return all;
    }

    /// <summary>
    /// Public repo search. Rate limited to 30 req/min, so callers must debounce; this is the
    /// only per-keystroke network call in the extension.
    /// </summary>
    public async Task<List<GitHubRepo>> SearchPublicReposAsync(
        string term, int limit = 15, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term)) return [];

        var q = Uri.EscapeDataString(term);
        var url = $"/search/repositories?q={q}&sort=stars&order=desc&per_page={Math.Clamp(limit, 1, 100)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var res = await SendAsync(req, ct).ConfigureAwait(false);

        // A tripped search limit should degrade to "no public results", not break the page.
        if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) return [];
        if (!res.IsSuccessStatusCode) throw await ToError(res).ConfigureAwait(false);

        var payload = await res.Content.ReadFromJsonAsync(Json.GitHubSearchResponse, ct).ConfigureAwait(false);
        return payload?.Items ?? [];
    }

    public async Task<bool> IsStarredAsync(string owner, string repo, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/user/starred/{owner}/{repo}");
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        return res.StatusCode == HttpStatusCode.NoContent;   // 204 starred, 404 not
    }

    public async Task SetStarAsync(string owner, string repo, bool starred, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(
            starred ? HttpMethod.Put : HttpMethod.Delete, $"/user/starred/{owner}/{repo}");
        req.Content = new StringContent(string.Empty);   // PUT requires a body, even an empty one
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw await ToError(res).ConfigureAwait(false);
    }

    /// <summary>
    /// Forks a repo. GitHub returns 202 Accepted and creates the fork asynchronously, so this
    /// polls until the new repo actually resolves before returning it.
    /// </summary>
    public async Task<GitHubRepo> ForkAsync(
        string owner, string repo, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/repos/{owner}/{repo}/forks"))
        {
            req.Content = new StringContent(string.Empty);
            using var res = await SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) throw await ToError(res).ConfigureAwait(false);
        }

        var me = await GetAuthenticatedUserAsync(ct).ConfigureAwait(false);

        // Forks usually materialise in a few seconds; poll rather than guess.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Waiting for fork {me.Login}/{repo}...");

            var forked = await TryGetRepoAsync(me.Login, repo, ct).ConfigureAwait(false);
            if (forked is not null) return forked;

            await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(false);
        }

        throw new GitHubException($"Fork of {owner}/{repo} was requested but did not appear in time.");
    }

    public async Task<GitHubRepo?> TryGetRepoAsync(string owner, string repo, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/repos/{owner}/{repo}");
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        if (!res.IsSuccessStatusCode) throw await ToError(res).ConfigureAwait(false);

        return await res.Content.ReadFromJsonAsync(Json.GitHubRepo, ct).ConfigureAwait(false);
    }

    /// <summary>Creates a repo under the authenticated user. Left empty so a local repo can be pushed into it.</summary>
    public async Task<GitHubRepo> CreateRepoAsync(
        string name, string? description = null, bool isPrivate = true, CancellationToken ct = default)
    {
        var body = new CreateRepoRequest
        {
            Name = name,
            Description = description,
            Private = isPrivate,
            AutoInit = false,   // must stay empty; we push an existing history into it
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/user/repos")
        {
            Content = JsonContent.Create(body, Json.CreateRepoRequest),
        };
        using var res = await SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw await ToError(res).ConfigureAwait(false);

        return await res.Content.ReadFromJsonAsync(Json.GitHubRepo, ct).ConfigureAwait(false)
               ?? throw new GitHubException("GitHub accepted the repo but returned no body.");
    }

    public void Dispose() => _http.Dispose();
}
