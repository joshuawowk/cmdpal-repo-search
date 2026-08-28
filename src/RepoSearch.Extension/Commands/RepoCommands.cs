using Microsoft.CommandPalette.Extensions.Toolkit;
using RepoSearch.Core;

namespace RepoSearch.Extension.Commands;

/// <summary>
/// Segoe Fluent / MDL2 glyphs used across rows and actions.
/// Kept as escape sequences so the source stays pure ASCII.
/// Note: IconInfo's parameterless constructor is internal, so always pass a string.
/// </summary>
internal static class Glyphs
{
    public static IconInfo Folder => new("\uE838");           // FolderOpen
    public static IconInfo Code => new("\uE943");             // Code
    public static IconInfo Globe => new("\uE774");            // Globe
    public static IconInfo Sync => new("\uE895");             // Sync
    public static IconInfo Upload => new("\uE898");           // Upload
    public static IconInfo Download => new("\uE896");         // Download
    public static IconInfo Star => new("\uE734");             // FavoriteStar
    public static IconInfo StarFilled => new("\uE735");       // FavoriteStarFill
    public static IconInfo Fork => new("\uE8AB");             // Switch, stands in for fork
    public static IconInfo Refresh => new("\uE72C");          // Refresh
    public static IconInfo Repo => new("\uE8B7");             // Folder
    public static IconInfo Cloud => new("\uE753");            // Cloud
    public static IconInfo Warning => new("\uE7BA");          // Warning
}

/// <summary>Sync: fetch, then fast-forward and/or push. Refuses anything that could lose work.</summary>
internal sealed partial class SyncCommand : AsyncActionCommand
{
    private readonly string _path;
    private readonly bool _allowRebase;

    public SyncCommand(string path, bool allowRebase)
    {
        _path = path;
        _allowRebase = allowRebase;
        Name = "Sync";
        Id = $"repo-search.sync.{path.ToLowerInvariant()}";
        Icon = Glyphs.Sync;
    }

    protected override string StartMessage => $"Syncing {Path.GetFileName(_path)}...";

    protected override Task<OperationResult> RunAsync(IProgress<string> progress, CancellationToken ct) =>
        new GitOperations().SyncAsync(_path, _allowRebase, progress, ct);
}

/// <summary>Clone: create a local copy under the configured clone root.</summary>
internal sealed partial class CloneCommand : AsyncActionCommand
{
    private readonly string _cloneUrl;
    private readonly string _targetRoot;
    private readonly string _folderName;
    private readonly bool _openAfter;

    public CloneCommand(string cloneUrl, string targetRoot, string folderName, bool openAfter = false)
    {
        _cloneUrl = cloneUrl;
        _targetRoot = targetRoot;
        _folderName = folderName;
        _openAfter = openAfter;
        Name = "Clone";
        Id = $"repo-search.clone.{cloneUrl.ToLowerInvariant()}";
        Icon = Glyphs.Download;
    }

    protected override string StartMessage => $"Cloning {_folderName}...";

    protected override async Task<OperationResult> RunAsync(IProgress<string> progress, CancellationToken ct)
    {
        var result = await new GitOperations()
            .CloneAsync(_cloneUrl, _targetRoot, _folderName, progress, ct).ConfigureAwait(false);

        // Detail carries the resolved destination, which may be suffixed to avoid a collision.
        if (result.Success && _openAfter && result.Detail is { Length: > 0 } dir)
            GitOperations.OpenInVSCode(dir);

        return result;
    }
}

/// <summary>Init: create a GitHub repo for a local-only repo and push into it.</summary>
internal sealed partial class InitCommand : AsyncActionCommand
{
    private readonly string _localPath;
    private readonly string _repoName;
    private readonly bool _private;
    private readonly GitHubClient _gh;

    public InitCommand(GitHubClient gh, string localPath, string repoName, bool isPrivate)
    {
        _gh = gh;
        _localPath = localPath;
        _repoName = repoName;
        _private = isPrivate;
        Name = "Init";
        Id = $"repo-search.init.{localPath.ToLowerInvariant()}";
        Icon = Glyphs.Upload;
    }

    protected override string StartMessage => $"Creating GitHub repo {_repoName}...";

    protected override async Task<OperationResult> RunAsync(IProgress<string> progress, CancellationToken ct)
    {
        GitHubRepo created;
        try
        {
            created = await _gh.CreateRepoAsync(
                _repoName,
                description: null,
                isPrivate: _private,
                ct).ConfigureAwait(false);
        }
        catch (GitHubException ex)
        {
            return OperationResult.Fail("Could not create the GitHub repository", ex.Message);
        }

        progress.Report($"Created {created.FullName}, pushing...");

        var push = await new GitOperations()
            .AddRemoteAndPushAsync(_localPath, created.CloneUrl, "origin", progress, ct).ConfigureAwait(false);

        // The repo exists either way; say so, so the user isn't left guessing.
        return push.Success
            ? OperationResult.Ok($"Published to {created.FullName}")
            : OperationResult.Fail($"Created {created.FullName} but the push failed", push.Detail ?? push.Message);
    }
}

/// <summary>Fork, optionally followed by a clone of the new fork.</summary>
internal sealed partial class ForkCommand : AsyncActionCommand
{
    private readonly GitHubClient _gh;
    private readonly string _owner;
    private readonly string _repo;
    private readonly bool _thenClone;
    private readonly string _cloneRoot;

    public ForkCommand(GitHubClient gh, string owner, string repo, bool thenClone, string cloneRoot)
    {
        _gh = gh;
        _owner = owner;
        _repo = repo;
        _thenClone = thenClone;
        _cloneRoot = cloneRoot;
        Name = thenClone ? "Fork & Clone" : "Fork";
        Id = $"repo-search.fork{(thenClone ? "clone" : "")}.{owner}/{repo}".ToLowerInvariant();
        Icon = Glyphs.Fork;
    }

    protected override string StartMessage => $"Forking {_owner}/{_repo}...";

    protected override async Task<OperationResult> RunAsync(IProgress<string> progress, CancellationToken ct)
    {
        GitHubRepo fork;
        try
        {
            fork = await _gh.ForkAsync(_owner, _repo, progress, ct).ConfigureAwait(false);
        }
        catch (GitHubException ex)
        {
            return OperationResult.Fail($"Could not fork {_owner}/{_repo}", ex.Message);
        }

        if (!_thenClone) return OperationResult.Ok($"Forked to {fork.FullName}");

        progress.Report($"Forked to {fork.FullName}, cloning...");

        var clone = await new GitOperations()
            .CloneAsync(fork.CloneUrl, _cloneRoot, fork.Name, progress, ct).ConfigureAwait(false);

        return clone.Success
            ? OperationResult.Ok($"Forked and cloned to {clone.Detail}")
            : OperationResult.Fail($"Forked to {fork.FullName} but the clone failed", clone.Detail ?? clone.Message);
    }
}

/// <summary>Star or unstar, resolving the current state first so the action reads correctly.</summary>
internal sealed partial class StarCommand : AsyncActionCommand
{
    private readonly GitHubClient _gh;
    private readonly string _owner;
    private readonly string _repo;

    public StarCommand(GitHubClient gh, string owner, string repo)
    {
        _gh = gh;
        _owner = owner;
        _repo = repo;
        Name = "Star";
        Id = $"repo-search.star.{owner}/{repo}".ToLowerInvariant();
        Icon = Glyphs.Star;
    }

    protected override string StartMessage => $"Starring {_owner}/{_repo}...";

    protected override async Task<OperationResult> RunAsync(IProgress<string> progress, CancellationToken ct)
    {
        try
        {
            var starred = await _gh.IsStarredAsync(_owner, _repo, ct).ConfigureAwait(false);
            await _gh.SetStarAsync(_owner, _repo, !starred, ct).ConfigureAwait(false);

            return OperationResult.Ok(starred
                ? $"Unstarred {_owner}/{_repo}"
                : $"Starred {_owner}/{_repo}");
        }
        catch (GitHubException ex)
        {
            return OperationResult.Fail($"Could not star {_owner}/{_repo}", ex.Message);
        }
    }
}
