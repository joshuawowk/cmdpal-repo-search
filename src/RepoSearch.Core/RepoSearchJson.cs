using System.Text.Json;
using System.Text.Json.Serialization;

namespace RepoSearch.Core;

/// <summary>Body for POST /user/repos. A concrete type keeps the payload trim-safe.</summary>
public sealed class CreateRepoRequest
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("private")] public bool Private { get; set; }

    /// <summary>Must stay false: we push an existing history into the new repo.</summary>
    [JsonPropertyName("auto_init")] public bool AutoInit { get; set; }
}

/// <summary>
/// Source-generated serialization for everything crossing the wire or hitting disk.
///
/// The extension publishes trimmed (the CmdPal template sets PublishTrimmed in Release), which
/// strips the metadata reflection-based System.Text.Json needs. Using a generated context keeps
/// serialization working after trimming instead of failing at runtime.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GitHubUser))]
[JsonSerializable(typeof(GitHubRepo))]
[JsonSerializable(typeof(List<GitHubRepo>))]
[JsonSerializable(typeof(GitHubSearchResponse))]
[JsonSerializable(typeof(CreateRepoRequest))]
[JsonSerializable(typeof(CatalogCache))]
[JsonSerializable(typeof(StatusCache))]
public sealed partial class RepoSearchJsonContext : JsonSerializerContext
{
}
