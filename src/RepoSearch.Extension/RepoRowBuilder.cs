using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using RepoSearch.Core;
using RepoSearch.Extension.Commands;

namespace RepoSearch.Extension;

/// <summary>
/// Turns a <see cref="SearchResult"/> into a palette row, with exactly the actions the spec
/// assigns to that result type.
///
/// This runs inside GetItems(), a documented hot path, so it must stay allocation-cheap and
/// must never do IO. Git status is read from cache only; a missing status renders as blank
/// and fills in later when the background refresh raises ItemsChanged.
/// </summary>
internal sealed class RepoRowBuilder
{
    private readonly SettingsManager _settings;
    private readonly RepoSearchService _service;

    public RepoRowBuilder(SettingsManager settings, RepoSearchService service)
    {
        _settings = settings;
        _service = service;
    }

    public ListItem Build(SearchResult r) => r.Kind switch
    {
        ResultKind.PairedOwn or ResultKind.PairedForeign => BuildPaired(r),
        ResultKind.LocalOnly => BuildLocalOnly(r),
        ResultKind.OwnRemote => BuildOwnRemote(r),
        _ => BuildPublic(r),
    };

    // ------------------------------------------------------------------ type 1 + the foreign variant

    private ListItem BuildPaired(SearchResult r)
    {
        var local = r.PrimaryLocal!;
        var foreign = r.Kind == ResultKind.PairedForeign;

        var more = new List<IContextItem>
        {
            new CommandContextItem(VSCodeCommand(local.Path)),
            new CommandContextItem(WebCommand(r.WebUrl)),
            new CommandContextItem(new SyncCommand(local.Path, _settings.AllowRebaseOnSync)),
        };

        // A clone of someone else's repo can still be starred and forked.
        if (foreign && _service.GitHub is { } gh && r.Remote is { } remote)
        {
            more.Add(new CommandContextItem(new StarCommand(gh, remote.OwnerLogin, remote.Name)));
            more.Add(new CommandContextItem(new ForkCommand(gh, remote.OwnerLogin, remote.Name, false, _settings.CloneRoot)));
        }

        return new ListItem(ExplorerCommand(local.Path))
        {
            Title = r.Name,
            Subtitle = Subtitle(r),
            Icon = Glyphs.Repo,
            TextToSuggest = r.Name,
            Tags = [.. Tags(r)],
            MoreCommands = [.. more],
            Details = BuildDetails(r),
        };
    }

    // ------------------------------------------------------------------ type 2

    private ListItem BuildLocalOnly(SearchResult r)
    {
        var local = r.PrimaryLocal!;

        var more = new List<IContextItem>
        {
            new CommandContextItem(VSCodeCommand(local.Path)),
        };

        // Init only makes sense with no remote at all. A repo on a non-GitHub host lands in
        // this bucket too, and publishing it to GitHub would be wrong.
        if (!local.HasRemote && _service.GitHub is { } gh)
        {
            more.Add(new CommandContextItem(new InitCommand(gh, local.Path, local.Name, _settings.NewRepoPrivate)));
        }
        else if (local.Origin is { } origin && origin.RawUrl.Length > 0)
        {
            more.Add(new CommandContextItem(WebCommand(origin.RawUrl)));
        }

        return new ListItem(ExplorerCommand(local.Path))
        {
            Title = r.Name,
            Subtitle = Subtitle(r),
            Icon = Glyphs.Folder,
            TextToSuggest = r.Name,
            Tags = [.. Tags(r)],
            MoreCommands = [.. more],
            Details = BuildDetails(r),
        };
    }

    // ------------------------------------------------------------------ type 3

    private ListItem BuildOwnRemote(SearchResult r)
    {
        var remote = r.Remote!;

        var more = new List<IContextItem>
        {
            new CommandContextItem(new ImmediateCommand(
                "VS Code", $"repo-search.vscode-remote.{remote.Key}", Glyphs.Code,
                () => GitOperations.OpenRemoteInVSCode(remote.HtmlUrl))),
            new CommandContextItem(new CloneCommand(remote.CloneUrl, _settings.CloneRoot, remote.Name)),
        };

        return new ListItem(WebCommand(remote.HtmlUrl))
        {
            Title = remote.Name,
            Subtitle = Subtitle(r),
            Icon = Glyphs.Cloud,
            TextToSuggest = remote.Name,
            Tags = [.. Tags(r)],
            MoreCommands = [.. more],
            Details = BuildDetails(r),
        };
    }

    // ------------------------------------------------------------------ type 4

    private ListItem BuildPublic(SearchResult r)
    {
        var remote = r.Remote!;
        var more = new List<IContextItem>();

        if (_service.GitHub is { } gh)
        {
            more.Add(new CommandContextItem(new StarCommand(gh, remote.OwnerLogin, remote.Name)));
            more.Add(new CommandContextItem(new ForkCommand(gh, remote.OwnerLogin, remote.Name, false, _settings.CloneRoot)));
            more.Add(new CommandContextItem(new ForkCommand(gh, remote.OwnerLogin, remote.Name, true, _settings.CloneRoot)));
        }

        more.Add(new CommandContextItem(new CloneCommand(remote.CloneUrl, _settings.CloneRoot, remote.Name)));

        return new ListItem(WebCommand(remote.HtmlUrl))
        {
            Title = remote.FullName,
            Subtitle = Subtitle(r),
            Icon = Glyphs.Globe,
            TextToSuggest = remote.Name,
            Tags = [.. Tags(r)],
            MoreCommands = [.. more],
            Details = BuildDetails(r),
        };
    }

    // ------------------------------------------------------------------ shared pieces

    private static InvokableCommand ExplorerCommand(string path) =>
        new ImmediateCommand("Explorer", $"repo-search.explorer.{path.ToLowerInvariant()}", Glyphs.Folder,
            () => GitOperations.OpenInExplorer(path));

    private static InvokableCommand VSCodeCommand(string path) =>
        new ImmediateCommand("VS Code", $"repo-search.vscode.{path.ToLowerInvariant()}", Glyphs.Code,
            () => GitOperations.OpenInVSCode(path));

    private static InvokableCommand WebCommand(string? url) =>
        new ImmediateCommand("Web", $"repo-search.web.{url?.ToLowerInvariant()}", Glyphs.Globe,
            () => GitOperations.OpenWeb(url));

    private string Subtitle(SearchResult r)
    {
        var parts = new List<string>(3);

        if (r.PrimaryLocal is { } local)
        {
            parts.Add(local.Path);
            if (r.Locals.Count > 1) parts.Add($"+{r.Locals.Count - 1} more clone(s)");
        }
        else if (r.Remote is { } remote)
        {
            if (!string.IsNullOrWhiteSpace(remote.Description)) parts.Add(remote.Description!);
            else parts.Add(remote.FullName);
        }

        return string.Join("  -  ", parts);
    }

    /// <summary>
    /// Row tags. The first is the posh-git status when we have one; the rest are the flags
    /// that change what a row means (private, fork, archived, stars).
    /// </summary>
    private List<Tag> Tags(SearchResult r)
    {
        var tags = new List<Tag>(4);

        if (_settings.ShowGitStatus && r.PrimaryLocal is { } local)
        {
            var status = r.Status ?? _service.PeekStatus(local.Path);

            if (status is not null)
            {
                tags.Add(new Tag(status.Format(includeBrackets: false))
                {
                    ToolTip = StatusToolTip(status),
                });
            }
            else
            {
                // Branch is free (read straight from .git/HEAD); the counts arrive later.
                tags.Add(new Tag(local.DisplayHead) { ToolTip = "Reading git status..." });
            }
        }

        if (r.Remote is { } remote)
        {
            if (remote.Private) tags.Add(new Tag("private"));
            if (remote.Fork) tags.Add(new Tag("fork"));
            if (remote.Archived) tags.Add(new Tag("archived"));
            if (r.Kind == ResultKind.PublicRemote && remote.Stars > 0)
                tags.Add(new Tag($"{remote.Stars:N0} stars"));
            if (!string.IsNullOrEmpty(remote.Language)) tags.Add(new Tag(remote.Language!));
        }

        return tags;
    }

    private static string StatusToolTip(GitStatusInfo s)
    {
        if (s.Error is not null) return s.Error;

        var lines = new List<string>
        {
            s.Detached ? "detached HEAD" : $"branch {s.Branch}",
            s.HasUpstream
                ? $"upstream {s.Upstream}: {s.Ahead} ahead, {s.Behind} behind"
                : "no upstream branch",
            $"staged: {s.IndexAdded} added, {s.IndexModified} modified, {s.IndexDeleted} deleted",
            $"working: {s.WorkingAdded} added, {s.WorkingModified} modified, {s.WorkingDeleted} deleted",
        };

        if (s.Conflicts > 0) lines.Add($"{s.Conflicts} conflicted file(s)");
        if (s.Stashes > 0) lines.Add($"{s.Stashes} stash entry(ies)");
        lines.Add(s.UntrackedUnknown ? "untracked files not counted" : $"{s.Untracked} untracked file(s)");

        return string.Join("\n", lines);
    }

    private Details BuildDetails(SearchResult r)
    {
        var metadata = new List<IDetailsElement>();

        void Add(string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                metadata.Add(new DetailsElement { Key = key, Data = new DetailsLink(string.Empty, value) });
        }

        foreach (var local in r.Locals)
            Add(r.Locals.Count > 1 ? $"Local ({local.Name})" : "Local", local.Path);

        if (r.PrimaryLocal is { } primary)
        {
            Add("Branch", primary.DisplayHead);

            var status = r.Status ?? _service.PeekStatus(primary.Path);
            if (status is not null) Add("Status", status.Format());
        }

        if (r.Remote is { } remote)
        {
            if (!string.IsNullOrWhiteSpace(remote.HtmlUrl))
                metadata.Add(new DetailsElement
                {
                    Key = "Remote",
                    Data = new DetailsLink(remote.HtmlUrl, remote.FullName),
                });

            if (remote.PushedAt is { } pushed) Add("Last push", pushed.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            if (!string.IsNullOrEmpty(remote.DefaultBranch)) Add("Default branch", remote.DefaultBranch!);
            if (remote.Stars > 0) Add("Stars", remote.Stars.ToString("N0"));
            if (remote.OpenIssues > 0) Add("Open issues", remote.OpenIssues.ToString("N0"));
        }

        return new Details
        {
            Title = r.DisplayName,
            Body = r.Description ?? KindDescription(r.Kind),
            Metadata = [.. metadata],
        };
    }

    private static string KindDescription(ResultKind kind) => kind switch
    {
        ResultKind.PairedOwn => "Your repository, cloned locally.",
        ResultKind.PairedForeign => "Someone else's repository, cloned locally.",
        ResultKind.LocalOnly => "A local repository with no GitHub remote.",
        ResultKind.OwnRemote => "Your GitHub repository. No local clone.",
        _ => "A public GitHub repository.",
    };
}
