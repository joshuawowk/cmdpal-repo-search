using System.Text.Json.Serialization;

namespace RepoSearch.Core;

/// <summary>
/// The subset of GitHub's repo payload we actually render or rank on.
/// Deliberately narrow: the owned-repo list is cached to disk and 240 full payloads
/// would be megabytes for fields we never read.
/// </summary>
public sealed class GitHubRepo
{
    [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("clone_url")] public string CloneUrl { get; set; } = "";
    [JsonPropertyName("ssh_url")] public string? SshUrl { get; set; }
    [JsonPropertyName("default_branch")] public string? DefaultBranch { get; set; }
    [JsonPropertyName("pushed_at")] public DateTimeOffset? PushedAt { get; set; }
    [JsonPropertyName("stargazers_count")] public int Stars { get; set; }
    [JsonPropertyName("forks_count")] public int Forks { get; set; }
    [JsonPropertyName("open_issues_count")] public int OpenIssues { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("private")] public bool Private { get; set; }
    [JsonPropertyName("fork")] public bool Fork { get; set; }
    [JsonPropertyName("archived")] public bool Archived { get; set; }
    [JsonPropertyName("owner")] public GitHubOwner? Owner { get; set; }

    /// <summary>Lowercased "owner/name" — the join key against <see cref="GitRemote.Key"/>.</summary>
    [JsonIgnore]
    public string Key => FullName.ToLowerInvariant();

    [JsonIgnore]
    public string OwnerLogin => Owner?.Login ?? (FullName.Contains('/') ? FullName[..FullName.IndexOf('/')] : "");
}

public sealed class GitHubOwner
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }
}

public sealed class GitHubSearchResponse
{
    [JsonPropertyName("total_count")] public int TotalCount { get; set; }
    [JsonPropertyName("incomplete_results")] public bool Incomplete { get; set; }
    [JsonPropertyName("items")] public List<GitHubRepo> Items { get; set; } = [];
}

public sealed class GitHubUser
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }
}
