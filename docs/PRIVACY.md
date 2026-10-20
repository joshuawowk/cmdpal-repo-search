# Privacy Policy - Repository Search for Command Palette

**Effective date: 2026-10-20**

Repository Search ("the extension") is a free, open-source extension for the Command Palette in
Microsoft PowerToys. It is published by Josh Wowk ("I", "me"), an individual developer. It is
independent software and is not affiliated with or endorsed by Microsoft or GitHub.

## Short version

- The extension has **no telemetry and no analytics**, shows no ads, and has no accounts or servers
  of its own. I do not receive any data from it.
- Everything it reads from your computer stays on your computer, **except** the specific requests
  to GitHub (`api.github.com`) and the `git` operations you start yourself, described below.
- With **no GitHub token**, the extension makes **no network requests at all**. It only searches
  local folders.
- If you add a GitHub token, it is stored in Windows Credential Manager and is sent only to
  `api.github.com`.
- The only third-party service the extension itself talks to is GitHub.

## What the extension reads on your computer

All of this happens locally:

- **Folders you configure.** It scans the folders listed in the extension's settings (by default,
  whichever of `source\repos`, `Repos`, `Repositories` and `Documents\GitHub` exist under your user
  profile) down to the scan depth you set, looking for git repositories.
- **Git metadata.** For each repository found it reads `.git/HEAD` and `.git/config` directly
  (current branch and remote URLs), and the `.git/index` timestamp and stash log to tell whether
  anything changed.
- **Git status.** To show the status summary, it runs `git status` inside those repositories.
  This is local; it does not contact a remote.
- **A GitHub token, if one is available.** In this order: Windows Credential Manager, the
  `GH_TOKEN` or `GITHUB_TOKEN` environment variables, then the output of `gh auth token` (the
  GitHub CLI, if you have it installed and signed in). The extension stops at the first one it finds.
- **Your own settings** (below).

The extension does not read the contents of your source files, only the repository metadata listed above.

## What is stored on your computer

- **Settings** (`settings.json`): your folder list, scan depth and on/off toggles. It does **not**
  contain your token.
- **GitHub repository list cache** (`catalog.json`): a copy of the list of repositories your token
  can see (name, description, URLs, language, star and fork counts, private/fork/archived flags,
  owner login), refreshed about every 6 hours. If your token can see private repositories, their
  names and descriptions are in this file. It also records repository renames and repositories that
  no longer exist.
- **Status cache** (`status-cache.json`): the local paths of your repositories and their last git
  status summary.
- **Your token**, if you save one (see below).

These files live in a per-extension folder in your Windows user profile (for a packaged app like
this one, normally under `%LOCALAPPDATA%\Packages\<package family name>`). They are never uploaded
by the extension. You can delete them at any time, and the project's `build\uninstall.ps1 -RemoveData`
script deletes that folder. No log files are written.

## What is sent, to whom, and when

### To GitHub (`https://api.github.com`) - only if a token is available

When a token is found, the extension makes requests to the GitHub REST API. Each request carries your
token (as a standard `Authorization: Bearer` header), a `User-Agent` of `cmdpal-repo-search/1.0`,
and the usual network information GitHub sees from any client, such as your IP address.

| When | What is sent |
|---|---|
| On first use and then about every 6 hours, or when you run Refresh | Requests for your profile (`/user`) and for the list of repositories you own, collaborate on or can see through an organization (`/user/repos`). |
| After the list refresh | For each local clone whose remote is on `github.com` and that is **not** in your own repository list (for example a clone of someone else's project, or a repository that was renamed), a lookup of that repository by its `owner/name` (`/repos/{owner}/{name}`). This sends the owner and name taken from the clone's remote URL. Remotes on other hosts (GitLab, Azure DevOps, self-hosted and so on) are never looked up. Results are cached. |
| As you type on the Repository Search page, and only if **Search public GitHub repositories** is on | Your search text, once it is at least 3 characters long, after a 250 ms pause in typing, to GitHub's repository search (`/search/repositories`). Search text typed into Command Palette's main list is **not** sent anywhere: the global-search results use only local repositories and the cached list. |
| When you choose **Star** | A check of whether you have starred that repository, then a star or unstar request. |
| When you choose **Fork** or **Fork & Clone** | A fork request for that repository, then lookups of your profile and the new fork. |
| When you choose **Init** | A request to create a new repository in your GitHub account. The name is the local folder's name; no description is sent; it is private unless you turn that setting off. |

The token is attached to requests to `api.github.com` only. If GitHub answers with a redirect, the
extension follows it only when it stays on `api.github.com`, so the token cannot be forwarded to any
other host. GitHub's handling of these requests is covered by
[GitHub's Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

### Through git, only when you start an action

When you choose **Clone**, **Fork & Clone**, **Sync** or **Init**, the extension runs the `git`
program installed on your computer:

- **Clone** uses the repository's clone URL as returned by GitHub (an `https://github.com/...` URL).
- **Sync** runs `git fetch --prune --no-tags` and then, depending on the state of the repository,
  a fast-forward merge, a rebase pull (only if you enabled it) or a `git push`.
- **Init** adds a remote pointing at the newly created GitHub repository and pushes to it.

These commands contact whatever remote the repository is configured with, using your own git
configuration and credentials (for example Git Credential Manager or SSH keys). For Sync that is
whatever host is in the repository's `.git/config`, not necessarily GitHub. **The extension does not
give your GitHub token to git**; it only sets `GIT_TERMINAL_PROMPT=0` so git never waits for a
password prompt, and `GIT_PAGER=cat`.

### Things handed to Windows or other programs

Actions such as **Web**, **Explorer** and **VS Code** ask Windows to open a `https://github.com/...`
web address in your default browser, open a folder in File Explorer, or launch Visual Studio Code
(including a `vscode://` link that carries the repository's web address). What those programs do
afterwards is governed by their own privacy policies.

### Nothing else

The extension makes no other network connections. It does not contact any server run by me.

## Token storage

If you paste a token into the extension's settings, it is moved straight into **Windows Credential
Manager** as a generic credential named `cmdpal-repo-search:github`, kept for your Windows user on
that PC (it is not roamed to other devices), and the settings field is cleared, so the token is never
written to `settings.json`. Tokens found in environment variables or via the GitHub CLI are read
when needed and are not copied anywhere by the extension.

To remove a stored token: type `clear` into the token field in the extension's settings; or delete
the `cmdpal-repo-search:github` entry in Control Panel > Credential Manager > Windows Credentials; or
run `cmdkey /delete:cmdpal-repo-search:github`. You can also revoke the token itself on GitHub at any
time, which stops it working everywhere.

## Analytics, advertising and third parties

- The extension contains **no analytics, telemetry, crash reporting or advertising code**, and
  includes no third-party tracking libraries. The only network client in it talks to
  `api.github.com`.
- I do not sell or share data, because I do not have any.
- This policy covers the extension only. PowerToys and Command Palette (Microsoft), Windows, and the
  Microsoft Store have their own data practices and are not covered here. Through Microsoft Partner
  Center I may see aggregate, anonymous figures such as download counts and crash statistics; these do
  not identify you.

This was checked by reading the source code at commit `679848a` (2026-10-20). Because the code can
change, you can re-check any version yourself in the public repository, for example:

```
git grep -n -i -E "telemetry|analytics|appinsights|sentry|HttpClient" -- src
```

## Children

The extension is a developer tool and is not directed at children. I do not knowingly collect any
personal information from anyone.

## Your choices

- Do not add a token (and do not set `GH_TOKEN`/`GITHUB_TOKEN` or sign in to `gh`) and the extension
  never touches the network.
- Turn off **Search public GitHub repositories** to stop search text being sent to GitHub.
- Turn off **Show repositories in global search** or the individual fallback slots to keep the extension
  out of the main Command Palette list.
- Delete the stored token or the extension's cached files at any time, or uninstall the extension.

## Changes to this policy

If the extension's data practices change, I will update this page and its effective date before the
change ships. Earlier versions remain available in the repository's git history.

## Contact

Josh Wowk - [josh@jwowk.net](mailto:josh@jwowk.net)
Questions and issues: <https://github.com/joshuawowk/cmdpal-repo-search/issues>
