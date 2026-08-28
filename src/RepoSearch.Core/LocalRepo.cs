namespace RepoSearch.Core;

/// <summary>A git working copy found on disk. Populated WITHOUT invoking git.exe.</summary>
public sealed class LocalRepo
{
    public required string Path { get; init; }
    public required string Name { get; init; }

    /// <summary>Resolved .git directory (handles the "gitdir:" pointer file used by worktrees/submodules).</summary>
    public required string GitDir { get; init; }

    /// <summary>Current branch, or null when HEAD is detached.</summary>
    public string? Branch { get; init; }

    /// <summary>Commit HEAD points at, when detached.</summary>
    public string? DetachedSha { get; init; }

    public IReadOnlyDictionary<string, GitRemote> Remotes { get; init; } =
        new Dictionary<string, GitRemote>(StringComparer.OrdinalIgnoreCase);

    public GitRemote? Origin =>
        Remotes.TryGetValue("origin", out var o) ? o : Remotes.Values.FirstOrDefault();

    public bool HasRemote => Origin is not null;

    public string? GitHubKey => Origin?.IsGitHub == true ? Origin.Key : null;

    public string DisplayHead =>
        Branch ?? (DetachedSha is { Length: >= 7 } s ? $"({s[..7]}...)" : "(detached)");
}
