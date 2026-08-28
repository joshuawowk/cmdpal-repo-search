namespace RepoSearch.Core;

/// <summary>
/// The result categories from the spec, in priority order. The numeric values ARE the
/// primary sort key, so order here is load-bearing.
/// </summary>
public enum ResultKind
{
    /// <summary>Spec type 1: my GitHub repo together with its local clone.</summary>
    PairedOwn = 0,

    /// <summary>
    /// Not in the spec, but real: a local clone of someone else's repo (this machine has
    /// espressif/esp-idf and FeloDeck/Feishin-Plugin). It has a working copy, so it is more
    /// actionable than any remote-only result, but it isn't mine — hence its own rung.
    /// </summary>
    PairedForeign = 1,

    /// <summary>Spec type 2: a local repo with no remote at all.</summary>
    LocalOnly = 2,

    /// <summary>Spec type 3: one of my GitHub repos with no local clone.</summary>
    OwnRemote = 3,

    /// <summary>Spec type 4: somebody else's public GitHub repo.</summary>
    PublicRemote = 4,
}

/// <summary>One row in the palette. Carries whichever halves exist.</summary>
public sealed class SearchResult
{
    public required ResultKind Kind { get; init; }

    /// <summary>Every local clone of this repo. Usually 0 or 1, but this machine has remotes with two.</summary>
    public IReadOnlyList<LocalRepo> Locals { get; init; } = [];

    public GitHubRepo? Remote { get; init; }

    public LocalRepo? PrimaryLocal => Locals.Count > 0 ? Locals[0] : null;
    public bool HasLocal => Locals.Count > 0;
    public bool HasRemote => Remote is not null;

    /// <summary>Score from the match; higher sorts earlier within a <see cref="Kind"/>.</summary>
    public double Score { get; set; }

    /// <summary>Filled in lazily — status is far too slow to compute during search.</summary>
    public GitStatusInfo? Status { get; set; }

    public string Name => Remote?.Name ?? PrimaryLocal?.Name ?? "?";

    public string DisplayName => Remote?.FullName ?? PrimaryLocal?.Name ?? "?";

    public string? Description => Remote?.Description;

    public string? WebUrl => Remote?.HtmlUrl ?? PrimaryLocal?.Origin?.HtmlUrl;

    public string? LocalPath => PrimaryLocal?.Path;

    /// <summary>Stable identity for caching and de-duplication.</summary>
    public string Id => Remote is not null
        ? $"gh:{Remote.Key}"
        : $"local:{PrimaryLocal?.Path?.ToLowerInvariant()}";
}
