using System.Diagnostics;

namespace RepoSearch.Core;

/// <summary>How thoroughly to look for untracked files. This is the single biggest cost lever.</summary>
public enum UntrackedMode
{
    /// <summary>Skip the untracked walk entirely (-uno). Measured 1.5s vs 23s on a repo with node_modules.</summary>
    None,
    /// <summary>Normal untracked detection. Accurate, but walks ignored trees too.</summary>
    Normal,
}

/// <summary>
/// Computes <see cref="GitStatusInfo"/> by invoking git once per repo.
///
/// Cost model measured on this machine (OneDrive-backed repos, AV active):
///   git.exe spawn floor .................. ~670 ms
///   status, untracked=normal ............. 1.2 s - 23 s   (node_modules dominates)
///   status, untracked=no ................. 0.7 s - 1.5 s
/// So: one invocation per repo, a hard timeout, results cached, and never on the UI path.
/// </summary>
public sealed class GitStatusReader
{
    private readonly string _gitPath;
    public GitStatusReader(string gitPath = "git") => _gitPath = gitPath;

    public async Task<GitStatusInfo> ReadAsync(
        string workdir,
        UntrackedMode untracked = UntrackedMode.Normal,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        var uFlag = untracked == UntrackedMode.None ? "no" : "normal";

        // One invocation carries branch, upstream, divergence and all file counts.
        var args = $"status --porcelain=v2 --branch --untracked-files={uFlag} --ignore-submodules=dirty";

        var (exitCode, stdout, _, timedOut) = await RunAsync(workdir, args, limit, ct).ConfigureAwait(false);

        if (timedOut)
        {
            // Retry once without the untracked walk, which is nearly always the culprit.
            if (untracked == UntrackedMode.Normal)
                return await ReadAsync(workdir, UntrackedMode.None, limit, ct).ConfigureAwait(false);

            return new GitStatusInfo { Error = "status timed out" };
        }

        if (exitCode != 0) return new GitStatusInfo { Error = "not a git repo" };

        var parsed = ParsePorcelainV2(stdout, untrackedWalked: untracked == UntrackedMode.Normal);

        // Stash count comes free from the reflog file; no second git spawn.
        return parsed with { Stashes = CountStashes(workdir) };
    }

    /// <summary>Reads the stash reflog directly. Each line is one stash entry.</summary>
    public static int CountStashes(string workdir)
    {
        try
        {
            var gitDir = RepoScanner.ResolveGitDir(workdir);
            if (gitDir is null) return 0;

            var log = Path.Combine(gitDir, "logs", "refs", "stash");
            if (!File.Exists(log)) return 0;

            var count = 0;
            foreach (var line in File.ReadLines(log))
                if (!string.IsNullOrWhiteSpace(line)) count++;
            return count;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Parses `git status --porcelain=v2 --branch`.
    ///
    /// Header lines:   # branch.oid|head|upstream|ab
    /// Entry lines:    "1 XY ..." ordinary, "2 XY ..." renamed/copied,
    ///                 "u XY ..." unmerged, "? path" untracked, "! path" ignored.
    /// In XY, X is the index (staged) state and Y the working-tree state;
    /// '.' means unchanged in that half.
    /// </summary>
    public static GitStatusInfo ParsePorcelainV2(string output, bool untrackedWalked = true)
    {
        string? branch = null, upstream = null;
        var detached = false;
        int ahead = 0, behind = 0;
        int ia = 0, im = 0, id = 0;
        int wa = 0, wm = 0, wd = 0;
        int untracked = 0, conflicts = 0, stashes = 0;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line[0] == '#')
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;

                switch (parts[1])
                {
                    case "branch.head":
                        if (parts[2] == "(detached)") detached = true;
                        else branch = parts[2];
                        break;

                    case "branch.upstream":
                        upstream = parts[2];
                        break;

                    case "branch.ab":
                        // "# branch.ab +2 -1"
                        foreach (var token in parts.Skip(2))
                        {
                            if (token.Length < 2) continue;
                            if (!int.TryParse(token[1..], out var n)) continue;
                            if (token[0] == '+') ahead = n;
                            else if (token[0] == '-') behind = n;
                        }
                        break;

                    case "stash":
                        int.TryParse(parts[2], out stashes);
                        break;
                }
                continue;
            }

            switch (line[0])
            {
                case '?':
                    untracked++;
                    break;

                case '!':
                    break; // ignored entry; only present with --ignored

                case 'u':
                    conflicts++;
                    break;

                case '1':
                case '2':
                {
                    // "1 XY ..." — XY starts at index 2.
                    if (line.Length < 4) break;
                    var x = line[2];
                    var y = line[3];

                    switch (x)
                    {
                        case 'A': ia++; break;
                        case 'M': case 'T': case 'R': case 'C': im++; break;
                        case 'D': id++; break;
                    }

                    switch (y)
                    {
                        case 'A': wa++; break;
                        case 'M': case 'T': case 'R': case 'C': wm++; break;
                        case 'D': wd++; break;
                    }
                    break;
                }
            }
        }

        return new GitStatusInfo
        {
            Branch = branch,
            Upstream = upstream,
            Detached = detached,
            Ahead = ahead,
            Behind = behind,
            IndexAdded = ia,
            IndexModified = im,
            IndexDeleted = id,
            WorkingAdded = wa,
            WorkingModified = wm,
            WorkingDeleted = wd,
            Untracked = untracked,
            UntrackedUnknown = !untrackedWalked,
            Conflicts = conflicts,
            Stashes = stashes,
        };
    }

    internal async Task<(int ExitCode, string StdOut, string StdErr, bool TimedOut)> RunAsync(
        string workdir, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_gitPath, arguments)
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Keep git from consulting the user's pager, prompting for credentials, or
        // stalling on an interactive terminal.
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";

        using var proc = new Process { StartInfo = psi };

        try { proc.Start(); }
        catch (Exception ex) { return (-1, string.Empty, ex.Message, false); }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return (-1, string.Empty, string.Empty, TimedOut: !ct.IsCancellationRequested);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (proc.ExitCode, stdout, stderr, false);
    }
}
