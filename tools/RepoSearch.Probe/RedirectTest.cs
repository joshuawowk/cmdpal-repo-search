using RepoSearch.Core;

namespace RepoSearch.Probe;

/// <summary>
/// Renamed repos answer /repos/{owner}/{name} with a 301 to /repositories/{id}.
/// This confirms the client follows that redirect (with auth intact) instead of erroring.
/// </summary>
public static class RedirectTest
{
    public static async Task RunAsync(string token)
    {
        Console.WriteLine();
        Console.WriteLine("=== rename/redirect diagnosis ===");

        using var gh = new GitHubClient(token);

        string[] keys =
        [
            "joshuawowk/drone-sentinel",           // renamed -> SentinelRF_Detector
            "joshuawowk/epicor-bpm-newpartmessage",// renamed -> epicor-bpm-general
            "joshuawowk/definitely-not-a-real-repo",
        ];

        foreach (var key in keys)
        {
            var slash = key.IndexOf('/');
            try
            {
                var repo = await gh.TryGetRepoAsync(key[..slash], key[(slash + 1)..]);
                Console.WriteLine($"  {key,-42} -> {(repo is null ? "NULL (404)" : repo.FullName)}");
            }
            catch (GitHubException ex)
            {
                Console.WriteLine($"  {key,-42} -> THREW status={ex.Status} msg={ex.Message}");
            }
        }
    }
}
