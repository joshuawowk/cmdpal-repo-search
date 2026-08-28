using System.Text.RegularExpressions;

namespace RepoSearch.Core;

/// <summary>
/// Finds local repos by walking the filesystem and reading .git metadata files directly.
///
/// Why not shell out to git? Measured on this machine, spawning git.exe costs ~670ms flat
/// (AV scanning) while reading .git/HEAD costs ~20-40ms. Across ~50 repos that is the
/// difference between a 35s scan and a 1s scan, so discovery never invokes git.
/// </summary>
public sealed class RepoScanner
{
    /// <summary>Directories never worth descending into while hunting for repo roots.</summary>
    public static readonly string[] DefaultIgnored =
    [
        "node_modules", ".git", "bin", "obj", ".venv", "venv", "__pycache__",
        ".vs", ".vscode", "dist", "build", "target", "vendor", ".gradle",
        "Pods", ".terraform", ".next", ".nuxt", "packages", "AppData",
    ];

    private readonly HashSet<string> _ignored;

    public RepoScanner(IEnumerable<string>? ignoredDirectoryNames = null) =>
        _ignored = new HashSet<string>(ignoredDirectoryNames ?? DefaultIgnored, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LocalRepo> Scan(IEnumerable<string> roots, int maxDepth = 3, CancellationToken ct = default)
    {
        var found = new List<LocalRepo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            Walk(root, 0, maxDepth, found, seen, ct);
        }

        return found;
    }

    private void Walk(string dir, int depth, int maxDepth, List<LocalRepo> found, HashSet<string> seen, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var dotGit = Path.Combine(dir, ".git");
        if (Directory.Exists(dotGit) || File.Exists(dotGit))
        {
            if (seen.Add(Normalize(dir)))
            {
                var repo = Read(dir);
                if (repo is not null) found.Add(repo);
            }

            // A repo is a leaf for our purposes; don't descend hunting for submodules.
            return;
        }

        if (depth >= maxDepth) return;

        string[] children;
        try { children = Directory.GetDirectories(dir); }
        catch (UnauthorizedAccessException) { return; }
        catch (DirectoryNotFoundException) { return; }
        catch (IOException) { return; }

        foreach (var child in children)
        {
            if (_ignored.Contains(Path.GetFileName(child))) continue;

            // Junctions and symlinks can form cycles. OneDrive placeholders are NOT reparse
            // points once hydrated, so this does not skip real content on this machine.
            try
            {
                if ((new DirectoryInfo(child).Attributes & FileAttributes.ReparsePoint) != 0) continue;
            }
            catch { continue; }

            Walk(child, depth + 1, maxDepth, found, seen, ct);
        }
    }

    private static string Normalize(string dir)
    {
        try { return Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar); }
        catch { return dir; }
    }

    /// <summary>Reads a single repo's metadata from disk. Returns null if it isn't really a repo.</summary>
    public static LocalRepo? Read(string workdir)
    {
        var gitDir = ResolveGitDir(workdir);
        if (gitDir is null) return null;

        ReadHead(gitDir, out var branch, out var sha);

        return new LocalRepo
        {
            Path = workdir,
            Name = Path.GetFileName(workdir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            GitDir = gitDir,
            Branch = branch,
            DetachedSha = sha,
            Remotes = ReadRemotes(Path.Combine(gitDir, "config")),
        };
    }

    /// <summary>
    /// .git is normally a directory, but is a file containing a "gitdir:" pointer for
    /// worktrees and submodules.
    /// </summary>
    public static string? ResolveGitDir(string workdir)
    {
        var dotGit = Path.Combine(workdir, ".git");
        if (Directory.Exists(dotGit)) return dotGit;
        if (!File.Exists(dotGit)) return null;

        try
        {
            var line = File.ReadAllText(dotGit).Trim();
            const string prefix = "gitdir:";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            var target = line[prefix.Length..].Trim();
            if (!Path.IsPathRooted(target)) target = Path.GetFullPath(Path.Combine(workdir, target));
            return Directory.Exists(target) ? target : null;
        }
        catch { return null; }
    }

    private static void ReadHead(string gitDir, out string? branch, out string? sha)
    {
        branch = null;
        sha = null;

        try
        {
            var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
            const string refPrefix = "ref:";

            if (head.StartsWith(refPrefix, StringComparison.Ordinal))
            {
                var refName = head[refPrefix.Length..].Trim();
                const string heads = "refs/heads/";
                branch = refName.StartsWith(heads, StringComparison.Ordinal) ? refName[heads.Length..] : refName;
            }
            else if (head.Length >= 7)
            {
                sha = head;
            }
        }
        catch { /* unreadable HEAD is not fatal; the repo still lists */ }
    }

    private static readonly Regex RemoteSectionRe =
        new(@"^\s*\[\s*remote\s+""(?<name>[^""]+)""\s*\]", RegexOptions.Compiled);

    private static readonly Regex AnySectionRe = new(@"^\s*\[", RegexOptions.Compiled);

    private static readonly Regex UrlRe = new(@"^\s*url\s*=\s*(?<url>.+?)\s*$", RegexOptions.Compiled);

    /// <summary>Minimal INI read of .git/config, for remote URLs only.</summary>
    public static IReadOnlyDictionary<string, GitRemote> ReadRemotes(string configPath)
    {
        var result = new Dictionary<string, GitRemote>(StringComparer.OrdinalIgnoreCase);

        string[] lines;
        try { lines = File.ReadAllLines(configPath); }
        catch { return result; }

        string? current = null;
        foreach (var line in lines)
        {
            var section = RemoteSectionRe.Match(line);
            if (section.Success) { current = section.Groups["name"].Value; continue; }
            if (AnySectionRe.IsMatch(line)) { current = null; continue; }
            if (current is null) continue;

            var url = UrlRe.Match(line);

            // First url= wins; a later pushurl or extra url doesn't change fetch identity.
            if (url.Success && !result.ContainsKey(current))
            {
                var parsed = GitRemote.Parse(url.Groups["url"].Value);
                if (parsed is not null) result[current] = parsed;
            }
        }

        return result;
    }
}
