using Microsoft.CommandPalette.Extensions.Toolkit;
using RepoSearch.Core;

namespace RepoSearch.Extension;

/// <summary>
/// Extension settings, persisted as JSON next to the caches.
///
/// The GitHub token is deliberately NOT one of these: settings.json is plain text. The token
/// field here is write-only plumbing that moves a pasted value into Windows Credential Manager
/// and then clears itself. See <see cref="TokenStore"/>.
/// </summary>
public sealed partial class SettingsManager : JsonSettingsManager
{
    private const string Ns = "RepoSearch";

    private static string Key(string name) => $"{Ns}.{name}";

    private static readonly string DefaultRoot =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "OneDrive - Getinge AB", "Repositories");

    private readonly TextSetting _localRoots = new(
        Key(nameof(LocalRoots)),
        "Local repository folders",
        "One folder per line. Each is scanned for git repositories.",
        DefaultRoot)
    { Multiline = true };

    private readonly TextSetting _scanDepth = new(
        Key(nameof(ScanDepth)),
        "Scan depth",
        "How many folder levels below each root to search for repositories.",
        "3");

    private readonly ToggleSetting _searchPublic = new(
        Key(nameof(SearchPublicRepos)),
        "Search public GitHub repositories",
        "Also return other people's public repos, after your own results.",
        true);

    private readonly ToggleSetting _showInGlobalResults = new(
        Key(nameof(ShowInGlobalResults)),
        "Show repositories in global search",
        "Adds your top repository matches to Command Palette's main results, so you don't have "
        + "to open Repository Search first. Individual slots can be toggled under "
        + "Settings > Fallback commands.",
        true);

    private readonly ToggleSetting _showGitStatus = new(
        Key(nameof(ShowGitStatus)),
        "Show git status",
        "Show a posh-git style status summary for local repositories.",
        true);

    private readonly ToggleSetting _includeUntracked = new(
        Key(nameof(IncludeUntracked)),
        "Count untracked files in status",
        "Accurate but much slower: scanning untracked files took 23s on one repo here versus " +
        "1.5s without. Leave off unless you need the '!' marker.",
        false);

    private readonly ToggleSetting _allowRebase = new(
        Key(nameof(AllowRebaseOnSync)),
        "Let Sync rebase diverged branches",
        "Off by default: Sync only fast-forwards or pushes, and refuses diverged branches.",
        false);

    private readonly ToggleSetting _newRepoPrivate = new(
        Key(nameof(NewRepoPrivate)),
        "Create new GitHub repos as private",
        "Applies to the Init action when publishing a local repo.",
        true);

    private readonly TextSetting _cloneRoot = new(
        Key(nameof(CloneRoot)),
        "Clone destination",
        "Folder new clones are created in. Defaults to the first local repository folder.",
        DefaultRoot);

    private readonly TextSetting _token = new(
        Key("TokenEntry"),
        "GitHub token (write-only)",
        "Paste a personal access token to store it in Windows Credential Manager. It is not " +
        "saved in settings. Leave blank to keep the existing token; type 'clear' to remove it.",
        string.Empty);

    public IReadOnlyList<string> LocalRoots =>
        (_localRoots.Value ?? string.Empty)
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToList();

    public int ScanDepth =>
        int.TryParse(_scanDepth.Value, out var d) ? Math.Clamp(d, 1, 8) : 3;

    public bool SearchPublicRepos => _searchPublic.Value;
    public bool ShowGitStatus => _showGitStatus.Value;
    public bool ShowInGlobalResults => _showInGlobalResults.Value;
    public bool IncludeUntracked => _includeUntracked.Value;
    public bool AllowRebaseOnSync => _allowRebase.Value;
    public bool NewRepoPrivate => _newRepoPrivate.Value;

    public string CloneRoot =>
        string.IsNullOrWhiteSpace(_cloneRoot.Value)
            ? (LocalRoots.FirstOrDefault() ?? DefaultRoot)
            : _cloneRoot.Value!;

    public UntrackedMode UntrackedMode => IncludeUntracked ? UntrackedMode.Normal : UntrackedMode.None;

    public static string SettingsFolder
    {
        get
        {
            // BaseSettingsPath does not create the directory; the first save throws without this.
            var dir = Utilities.BaseSettingsPath("RepoSearchForCommandPalette");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string CatalogCachePath => Path.Combine(SettingsFolder, "catalog.json");
    public static string StatusCachePath => Path.Combine(SettingsFolder, "status-cache.json");

    public SettingsManager()
    {
        FilePath = Path.Combine(SettingsFolder, "settings.json");

        Settings.Add(_localRoots);
        Settings.Add(_scanDepth);
        Settings.Add(_searchPublic);
        Settings.Add(_showGitStatus);
        Settings.Add(_showInGlobalResults);
        Settings.Add(_includeUntracked);
        Settings.Add(_allowRebase);
        Settings.Add(_newRepoPrivate);
        Settings.Add(_cloneRoot);
        Settings.Add(_token);

        LoadSettings();

        Settings.SettingsChanged += (_, _) =>
        {
            CaptureToken();
            SaveSettings();
            SettingsChangedExternal?.Invoke();
        };
    }

    /// <summary>Raised after settings are saved, so the page can rebuild its index.</summary>
    public event Action? SettingsChangedExternal;

    /// <summary>
    /// Moves a pasted token into Credential Manager and blanks the field, so the secret never
    /// reaches settings.json.
    /// </summary>
    private void CaptureToken()
    {
        var entered = _token.Value;
        if (string.IsNullOrWhiteSpace(entered)) return;

        if (entered.Trim().Equals("clear", StringComparison.OrdinalIgnoreCase))
            TokenStore.DeleteCredential(TokenStore.CredentialTarget);
        else
            TokenStore.WriteCredential(TokenStore.CredentialTarget, entered.Trim());

        _token.Value = string.Empty;
    }
}
