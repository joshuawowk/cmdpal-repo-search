using System.Diagnostics;
using System.Text;

namespace RepoSearch.Core;

public sealed record OperationResult(bool Success, string Message, string? Detail = null)
{
    public static OperationResult Ok(string message, string? detail = null) => new(true, message, detail);
    public static OperationResult Fail(string message, string? detail = null) => new(false, message, detail);
}

/// <summary>What a Sync would do, decided before touching the remote's history.</summary>
public enum SyncPlan
{
    UpToDate,
    Pull,          // behind only, and clean -> fast-forward
    Push,          // ahead only, and clean
    PullThenPush,  // diverged but clean -> only when rebase is allowed
    BlockedDirty,
    BlockedDiverged,
    BlockedNoUpstream,
    BlockedConflicts,
    BlockedDetached,
}

/// <summary>
/// The git side of the result actions: clone, sync, publish, and opening things.
///
/// Every git call goes through <see cref="GitStatusReader.RunAsync"/>-style process handling
/// with a timeout, because on this machine a git spawn costs ~670ms and a status on a large
/// working tree can take 20s+.
/// </summary>
public sealed class GitOperations
{
    private readonly string _git;
    public GitOperations(string? gitPath = null) => _git = gitPath ?? ToolLocator.Git;

    // ------------------------------------------------------------------ open actions

    /// <summary>Opens the folder in File Explorer.</summary>
    public static OperationResult OpenInExplorer(string path)
    {
        if (!Directory.Exists(path)) return OperationResult.Fail($"Folder no longer exists: {path}");

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return OperationResult.Ok($"Opened {Path.GetFileName(path)} in Explorer");
        }
        catch (Exception ex) { return OperationResult.Fail("Could not open Explorer", ex.Message); }
    }

    public static OperationResult OpenInVSCode(string path)
    {
        if (!Directory.Exists(path)) return OperationResult.Fail($"Folder no longer exists: {path}");

        var code = ToolLocator.VSCode;
        if (code is null) return OperationResult.Fail("VS Code was not found on this machine");

        try
        {
            // code.cmd is a batch script, so it needs a shell to run it.
            Process.Start(new ProcessStartInfo(code, $"\"{path}\"")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            return OperationResult.Ok($"Opening {Path.GetFileName(path)} in VS Code");
        }
        catch (Exception ex) { return OperationResult.Fail("Could not launch VS Code", ex.Message); }
    }

    public static OperationResult OpenWeb(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return OperationResult.Fail("This result has no web URL");

        try
        {
            ToolLocator.OpenWithShell(url);
            return OperationResult.Ok("Opened in browser");
        }
        catch (Exception ex) { return OperationResult.Fail("Could not open the browser", ex.Message); }
    }

    /// <summary>
    /// Opens a repo in VS Code without cloning, via the GitHub Repositories extension's
    /// URI handler. Falls back to the website when VS Code can't take the URI.
    /// </summary>
    public static OperationResult OpenRemoteInVSCode(string htmlUrl)
    {
        if (string.IsNullOrWhiteSpace(htmlUrl)) return OperationResult.Fail("This result has no web URL");

        try
        {
            ToolLocator.OpenWithShell($"vscode://GitHub.remotehub/open?url={Uri.EscapeDataString(htmlUrl)}");
            return OperationResult.Ok("Opening remote repository in VS Code");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Could not open in VS Code. Is the 'GitHub Repositories' extension installed?", ex.Message);
        }
    }

    // ------------------------------------------------------------------ clone

    public async Task<OperationResult> CloneAsync(
        string cloneUrl,
        string targetParentDir,
        string folderName,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        try { Directory.CreateDirectory(targetParentDir); }
        catch (Exception ex) { return OperationResult.Fail($"Cannot create {targetParentDir}", ex.Message); }

        var target = Path.Combine(targetParentDir, folderName);

        // Never clone onto an existing non-empty folder; pick a free suffix instead.
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            var i = 2;
            while (Directory.Exists($"{target}-{i}")) i++;
            target = $"{target}-{i}";
        }

        progress?.Report($"Cloning into {Path.GetFileName(target)}...");

        var (code, _, err) = await RunAsync(
            targetParentDir,
            $"clone --progress \"{cloneUrl}\" \"{target}\"",
            TimeSpan.FromMinutes(15), ct).ConfigureAwait(false);

        return code == 0
            ? OperationResult.Ok($"Cloned to {target}", target)
            : OperationResult.Fail("Clone failed", Tail(err));
    }

    // ------------------------------------------------------------------ sync

    /// <summary>
    /// Decides what Sync should do. Sync is deliberately conservative: it fast-forwards or
    /// pushes, and refuses anything that could lose work or start a merge the user didn't ask for.
    /// </summary>
    public static SyncPlan PlanSync(GitStatusInfo status, bool allowRebase = false)
    {
        if (status.Error is not null) return SyncPlan.BlockedDirty;
        if (status.Detached) return SyncPlan.BlockedDetached;
        if (status.Conflicts > 0) return SyncPlan.BlockedConflicts;
        if (!status.HasUpstream) return SyncPlan.BlockedNoUpstream;

        // Tracked changes only: untracked files are never at risk from a fast-forward or push,
        // and plenty of these repos carry untracked build output permanently.
        var dirty = status.HasTrackedChanges;

        if (status.Ahead == 0 && status.Behind == 0) return SyncPlan.UpToDate;
        if (dirty) return SyncPlan.BlockedDirty;

        if (status.Behind > 0 && status.Ahead == 0) return SyncPlan.Pull;
        if (status.Ahead > 0 && status.Behind == 0) return SyncPlan.Push;

        return allowRebase ? SyncPlan.PullThenPush : SyncPlan.BlockedDiverged;
    }

    public static string ExplainPlan(SyncPlan plan, GitStatusInfo s) => plan switch
    {
        SyncPlan.UpToDate => "Already in sync with the remote",
        SyncPlan.Pull => $"Fast-forward {s.Behind} commit(s) from {s.Upstream}",
        SyncPlan.Push => $"Push {s.Ahead} commit(s) to {s.Upstream}",
        SyncPlan.PullThenPush => $"Rebase onto {s.Upstream}, then push {s.Ahead}",
        SyncPlan.BlockedDirty => "Working tree has uncommitted changes - commit or stash first",
        SyncPlan.BlockedDiverged => $"Branch has diverged ({s.Ahead} ahead, {s.Behind} behind) - resolve manually",
        SyncPlan.BlockedNoUpstream => "Branch has no upstream to sync with",
        SyncPlan.BlockedConflicts => "Repo has unresolved merge conflicts",
        SyncPlan.BlockedDetached => "HEAD is detached - check out a branch first",
        _ => "Cannot sync",
    };

    /// <summary>
    /// Fetches, re-reads status, then fast-forwards and/or pushes. Refuses to act when the
    /// plan is blocked, so the caller can surface a precise reason.
    /// </summary>
    public async Task<OperationResult> SyncAsync(
        string repoPath,
        bool allowRebase = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Fetching...");

        var (fetchCode, _, fetchErr) = await RunAsync(
            repoPath, "fetch --prune --no-tags", TimeSpan.FromMinutes(3), ct).ConfigureAwait(false);

        if (fetchCode != 0) return OperationResult.Fail("Fetch failed", Tail(fetchErr));

        // Re-read after fetching: ahead/behind is only meaningful against fresh remote refs.
        var status = await new GitStatusReader(_git)
            .ReadAsync(repoPath, UntrackedMode.None, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        var plan = PlanSync(status, allowRebase);
        if (plan is SyncPlan.UpToDate) return OperationResult.Ok("Already up to date");

        if (plan is SyncPlan.BlockedDirty or SyncPlan.BlockedDiverged or SyncPlan.BlockedNoUpstream
                 or SyncPlan.BlockedConflicts or SyncPlan.BlockedDetached)
            return OperationResult.Fail(ExplainPlan(plan, status));

        if (plan is SyncPlan.Pull or SyncPlan.PullThenPush)
        {
            progress?.Report($"Updating from {status.Upstream}...");

            var pullArgs = plan == SyncPlan.PullThenPush ? "pull --rebase" : "merge --ff-only @{u}";
            var (pullCode, _, pullErr) = await RunAsync(
                repoPath, pullArgs, TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);

            if (pullCode != 0) return OperationResult.Fail("Could not update from remote", Tail(pullErr));
        }

        if (plan is SyncPlan.Push or SyncPlan.PullThenPush)
        {
            progress?.Report("Pushing...");

            var (pushCode, _, pushErr) = await RunAsync(
                repoPath, "push", TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);

            if (pushCode != 0) return OperationResult.Fail("Push failed", Tail(pushErr));
        }

        return OperationResult.Ok(ExplainPlan(plan, status) + " - done");
    }

    // ------------------------------------------------------------------ publish (Init)

    /// <summary>
    /// Points an existing local repo at a freshly created GitHub repo and pushes.
    /// The repo must already exist on GitHub and be empty.
    /// </summary>
    public async Task<OperationResult> AddRemoteAndPushAsync(
        string repoPath,
        string cloneUrl,
        string remoteName = "origin",
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // Refuse on an empty repo: there is no commit to push and `push -u` would fail obscurely.
        var (revCode, _, _) = await RunAsync(repoPath, "rev-parse --verify HEAD", TimeSpan.FromSeconds(30), ct)
            .ConfigureAwait(false);

        if (revCode != 0)
            return OperationResult.Fail("This repo has no commits yet - make a commit before publishing");

        var existing = RepoScanner.Read(repoPath)?.Remotes;
        var verb = existing is not null && existing.ContainsKey(remoteName) ? "set-url" : "add";

        var (remoteCode, _, remoteErr) = await RunAsync(
            repoPath, $"remote {verb} {remoteName} \"{cloneUrl}\"", TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        if (remoteCode != 0) return OperationResult.Fail("Could not set the remote", Tail(remoteErr));

        progress?.Report("Pushing to GitHub...");

        var (pushCode, _, pushErr) = await RunAsync(
            repoPath, $"push -u {remoteName} HEAD", TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);

        return pushCode == 0
            ? OperationResult.Ok("Published to GitHub")
            : OperationResult.Fail("Push failed", Tail(pushErr));
    }

    // ------------------------------------------------------------------ plumbing

    private static string Tail(string text, int lines = 4)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var parts = text.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" | ", parts.TakeLast(lines).Select(p => p.Trim()));
    }

    private async Task<(int Code, string Out, string Err)> RunAsync(
        string workdir, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_git, arguments)
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Never let a network operation block on an interactive credential prompt; the
        // extension has no console to answer it on.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_PAGER"] = "cat";

        using var proc = new Process { StartInfo = psi };

        try { proc.Start(); }
        catch (Exception ex) { return (-1, string.Empty, ex.Message); }

        var outTask = proc.StandardOutput.ReadToEndAsync(ct);
        var errTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return (-1, string.Empty, ct.IsCancellationRequested ? "cancelled" : "timed out");
        }

        return (proc.ExitCode,
                await outTask.ConfigureAwait(false),
                await errTask.ConfigureAwait(false));
    }
}
