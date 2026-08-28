using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using RepoSearch.Core;
using RepoSearch.Extension.Commands;

namespace RepoSearch.Extension.Pages;

/// <summary>
/// The search page. Always queries local repositories and GitHub, and orders results by the
/// spec's priority: paired, local-only, own remote, public.
///
/// Timing model (all numbers measured on this machine):
///   keystroke -> filter cached data ......... instant, no IO
///   +250ms debounce -> public GitHub search .. ~700ms, cancelled by the next keystroke
///   background -> git status per repo ....... 1.2-23s, cached, rows update in place
/// </summary>
internal sealed partial class RepoSearchPage : DynamicListPage, IDisposable
{
    private static readonly TimeSpan PublicSearchDebounce = TimeSpan.FromMilliseconds(250);

    private readonly SettingsManager _settings;
    private readonly RepoSearchService _service;
    private readonly RepoRowBuilder _builder;

    private readonly Lock _gate = new();
    private CancellationTokenSource? _cts;
    private int _generation;

    private IListItem[] _items = [];
    private List<SearchResult> _lastResults = [];
    private long _lastPublishTicks;

    public RepoSearchPage(SettingsManager settings, RepoSearchService service)
    {
        _settings = settings;
        _service = service;
        _builder = new RepoRowBuilder(settings, service);

        // An explicit Id keeps the user's pins and aliases stable; without one CmdPal
        // synthesises an id from the titles, so any wording change would break them.
        Id = "repo-search.page";
        Name = "Search";
        Title = "Repository Search";
        Icon = Glyphs.Repo;
        PlaceholderText = "Search your repositories, locally and on GitHub...";
        ShowDetails = true;

        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = "Type to search repositories",
            Subtitle = "Your repositories are matched first, then public GitHub repositories.",
            Icon = Glyphs.Repo,
        };

        // Warm the caches so the first keystroke already has data to filter.
        StartQuery(string.Empty);
    }

    public override IListItem[] GetItems() => _items;

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        // The host can re-set identical text; without this guard every one re-queries GitHub.
        if (string.Equals(oldSearch, newSearch, StringComparison.Ordinal)) return;
        StartQuery(newSearch);
    }

    /// <summary>Drops all caches and re-queries. Bound to the Refresh command.</summary>
    public void ForceRefresh()
    {
        _service.Invalidate();
        StartQuery(SearchText);
    }

    private void StartQuery(string query)
    {
        CancellationToken token;
        int generation;

        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            generation = ++_generation;
        }

        IsLoading = true;
        _ = Task.Run(() => RunQueryAsync(query, generation, token), CancellationToken.None);
    }

    private async Task RunQueryAsync(string query, int generation, CancellationToken ct)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;

            await _service.EnsureReadyAsync(now, ct).ConfigureAwait(false);
            if (IsSuperseded(generation)) return;

            // 1. Local repos + the cached GitHub catalog. No IO, so this paints immediately.
            var results = _service.SearchLocal(query, now);
            Publish(results, generation, force: true);

            // 2. Git status, in the background. Rows update in place as each repo reports.
            var statusTask = RefreshStatusesAsync(results, generation, now, ct);

            // 3. Public GitHub search, debounced so typing doesn't burn the 30/min search limit.
            if (_settings.SearchPublicRepos && query.Length >= 3)
            {
                await Task.Delay(PublicSearchDebounce, ct).ConfigureAwait(false);
                if (IsSuperseded(generation)) return;

                var hits = await _service.SearchPublicAsync(query, ct).ConfigureAwait(false);
                if (IsSuperseded(generation)) return;

                if (hits.Count > 0)
                {
                    var merged = _service.Merge(query, hits, now);
                    Publish(merged, generation, force: true);
                }
            }

            await statusTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* superseded by a newer query */ }
        catch (Exception ex)
        {
            // Never let a query fault kill the extension process.
            new ToastStatusMessage($"Repository search failed: {ex.Message}").Show();
        }
        finally
        {
            // Only the newest query owns the spinner.
            if (!IsSuperseded(generation)) IsLoading = false;
        }
    }

    private async Task RefreshStatusesAsync(
        List<SearchResult> results, int generation, DateTimeOffset now, CancellationToken ct)
    {
        if (!_settings.ShowGitStatus) return;

        // Only rows actually on screen are worth 1-23 seconds of git each.
        var visible = results
            .Where(r => r.HasLocal)
            .Take(15)
            .ToList();

        if (visible.Count == 0) return;

        var byPath = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in visible)
            if (r.PrimaryLocal is { } l) byPath[l.Path] = r;

        await _service.RefreshStatusAsync(
            visible.Select(r => r.PrimaryLocal!),
            now,
            (repo, status) =>
            {
                if (byPath.TryGetValue(repo.Path, out var result)) result.Status = status;
                Publish(results, generation, force: false);   // throttled inside
            },
            ct).ConfigureAwait(false);

        Publish(results, generation, force: true);
    }

    /// <summary>
    /// Rebuilds the rows and notifies the host. Unforced calls are throttled, because a status
    /// sweep can fire ~50 updates and rebuilding the whole list for each would thrash the UI.
    /// </summary>
    private void Publish(List<SearchResult> results, int generation, bool force)
    {
        if (IsSuperseded(generation)) return;

        var ticks = Environment.TickCount64;
        if (!force && ticks - Interlocked.Read(ref _lastPublishTicks) < 300) return;
        Interlocked.Exchange(ref _lastPublishTicks, ticks);

        var items = new List<IListItem>(results.Count + 5);

        // Grouping only works via command-less header rows: CmdPal ignores ListItem.Section on
        // any item that has a Command. Section(title, items) prepends the right header for us.
        foreach (var group in results.GroupBy(r => r.Kind).OrderBy(g => (int)g.Key))
        {
            var rows = group.Select(_builder.Build).Cast<IListItem>().ToArray();
            if (rows.Length == 0) continue;
            items.AddRange(new Section(SectionTitle(group.Key), rows));
        }

        _lastResults = results;
        _items = [.. items];

        UpdateEmptyContent();
        RaiseItemsChanged(_items.Length);
    }

    private void UpdateEmptyContent()
    {
        if (_items.Length > 0) return;

        var (title, subtitle) = _service.Warning is { Length: > 0 } warning
            ? ("Repository search needs attention", warning)
            : string.IsNullOrEmpty(SearchText)
                ? ("Type to search repositories", "Your repositories are matched first, then public GitHub repositories.")
                : ($"No repositories match \"{SearchText}\"", "Try a shorter search, or check your local folders in settings.");

        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = title,
            Subtitle = subtitle,
            Icon = _service.Warning is { Length: > 0 } ? Glyphs.Warning : Glyphs.Repo,
        };
    }

    private static string SectionTitle(ResultKind kind) => kind switch
    {
        ResultKind.PairedOwn => "Your repositories (local + GitHub)",
        ResultKind.PairedForeign => "Cloned from GitHub",
        ResultKind.LocalOnly => "Local only",
        ResultKind.OwnRemote => "Your GitHub repositories",
        _ => "Public GitHub repositories",
    };

    private bool IsSuperseded(int generation) => Volatile.Read(ref _generation) != generation;

    public void Dispose()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
