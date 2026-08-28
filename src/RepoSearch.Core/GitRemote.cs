using System.Text.RegularExpressions;

namespace RepoSearch.Core;

/// <summary>
/// A remote URL reduced to the identity we can match on.
/// Real data on this machine mixes "JoshuaWowk" and "joshuawowk" in origin URLs while the
/// API login is "joshuawowk", and mixes ".git" / no-".git" suffixes, so every comparison
/// here is deliberately case-insensitive and suffix-insensitive.
/// </summary>
public sealed record GitRemote(string RawUrl, string? Host, string? Owner, string? Name)
{
    public bool IsGitHub => string.Equals(Host, "github.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>"owner/name" lowercased; the join key between local clones and GitHub repos.</summary>
    public string? Key => Owner is null || Name is null ? null : $"{Owner}/{Name}".ToLowerInvariant();

    public string? HtmlUrl => IsGitHub && Key is not null ? $"https://github.com/{Owner}/{Name}" : null;

    private static readonly Regex Scp = new(@"^(?<user>[^@/]+)@(?<host>[^:/]+):(?<path>.+)$", RegexOptions.Compiled);

    public static GitRemote? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var raw = url.Trim();
        string? host = null, path = null;

        // scp-like: git@github.com:owner/repo.git
        var m = Scp.Match(raw);
        if (m.Success && !raw.Contains("://", StringComparison.Ordinal))
        {
            host = m.Groups["host"].Value;
            path = m.Groups["path"].Value;
        }
        else if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            // https://, http://, ssh://, git://  (also file:// — host stays empty, which is fine)
            host = uri.Host;
            path = uri.AbsolutePath;
        }
        else
        {
            // bare "github.com/owner/repo"
            var slash = raw.IndexOf('/');
            if (slash <= 0) return new GitRemote(raw, null, null, null);
            host = raw[..slash];
            path = raw[slash..];
        }

        if (string.IsNullOrEmpty(path)) return new GitRemote(raw, host, null, null);

        var segments = path.Trim('/')
                           .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return new GitRemote(raw, host, null, null);

        // Take the LAST two segments so gist/enterprise sub-paths still resolve.
        var owner = segments[^2];
        var name = segments[^1];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        if (owner.Length == 0 || name.Length == 0) return new GitRemote(raw, host, null, null);
        return new GitRemote(raw, host, owner, name);
    }
}
