# Repository Search — a PowerToys Command Palette extension

Search your git repositories **locally and on GitHub at the same time**, with your own repos
ranked first, and act on whichever one you find.

Built against Command Palette **0.12** and the `Microsoft.CommandPalette.Extensions` **0.12.260812002** SDK.

---

## What it does

Every query always searches **both** your local disk and GitHub. Results come back in the
priority order from the spec:

| # | Result type | Actions |
|---|---|---|
| 1 | Your GitHub repo **paired with its local clone** | Explorer · VS Code · Web · Sync |
| 2 | A **local repo with no remote** | Explorer · VS Code · Init |
| 3 | One of **your GitHub repos**, not cloned | Web · VS Code · Clone |
| 4 | Someone else's **public GitHub repo** | Web · Star · Fork · Fork & Clone · Clone |

There's a fifth case the spec doesn't name but this machine actually has: a **local clone of
someone else's repo** (`espressif/esp-idf`, for one). It gets the pair actions *plus* Star and
Fork, and sorts just below your own paired repos.

Each local row carries a [posh-git](https://github.com/dahlbyk/posh-git)-style status summary:

```
[main ↓24 +0 ~0 -0 | +0 ~0 -0 $1]      24 behind, 1 stash
[master ↓1 +0 ~0 -0 | +0 ~1 -0 !]      1 behind, 1 modified file, working tree dirty
[(detached) +0 ~0 -0 | +0 ~13 -0 !]    detached HEAD, 13 modified
[main ≡ +0 ~0 -0 | +0 ~0 -0]           clean and in sync
```

The format follows posh-git's real defaults, verified against its source rather than from
memory — diverged branches render **behind-first** with no `↕` glyph
(`BranchBehindAndAheadDisplay = Full`), untracked files are counted in the working `+` column
(`GitUtils.ps1` maps `?` to `filesAdded`), and the trailing marker is `!` for a dirty working
tree, `~` for index-only, nothing when clean.

---

## Global search

The extension contributes its top matches straight to Command Palette's main list, so you can
type a repo name without opening Repository Search first. It registers **5 fallback slots**,
each of which rewrites itself as you type and hides again when it has nothing to show.

Global rows use the **local repositories and the cached GitHub catalog only** - never a live
GitHub search, which would mean a network round-trip on every keystroke of every unrelated
query you type into the palette. Queries shorter than 2 characters are ignored for the same
reason.

Each slot appears individually under **Settings > Fallback commands**, where it can be turned
off; that is how you cap how many repositories reach global search. Each slot also has an
**Include in global results** toggle, which controls *placement*:

| Include in global results | Where repository rows appear |
|---|---|
| off (default for third-party extensions) | at the bottom, under the "Fallbacks" separator |
| on | scored and interleaved with the rest of the global results |

Command Palette defaults that toggle to on only for its own built-in extensions
(`FallbackSettings(bool isBuiltIn)`), so turning it on is a deliberate user choice an extension
cannot make for you. Either way the rows do show up - the toggle only decides where.

To turn the whole thing off, use **Show repositories in global search** in this extension's own
settings.

---

## Install

Requires Developer Mode (Settings → System → For developers). No Visual Studio, no Windows SDK,
no signing certificate.

```powershell
pwsh -File build\install.ps1 -RestartCmdPal
```

To remove it:

```powershell
pwsh -File build\uninstall.ps1 -RemoveData -RemoveToken
```

### GitHub token

Open **Command Palette → Repository Search → Settings**, paste a personal access token into
*GitHub token (write-only)* and save. The value is moved straight into **Windows Credential
Manager** (`cmdpal-repo-search:github`) and the field is cleared, so the token never lands in
`settings.json` or in this repo. Type `clear` to remove it.

Resolution order: Credential Manager → `GH_TOKEN`/`GITHUB_TOKEN` → `gh auth token`.

Scopes needed: `repo` (private repos, create), `workflow` if you push workflow files.
Without a token the extension still works against local repositories only.

---

## How it performs

This machine's repositories live on OneDrive, which is pathologically slow for file-heavy git
operations. Measured here:

| operation | cost |
|---|---|
| spawn `git.exe` (any command) | **~670 ms** floor |
| `git status` with untracked scan | 1.2 s – **23 s** per repo |
| `git status --untracked-files=no` | 0.7 – 1.5 s per repo |
| read `.git/HEAD` + `.git/config` directly | **~20 ms** per repo |
| list 245 GitHub repos | ~1.5 s |
| one public GitHub search | ~0.7 s |

That shaped the whole design:

- **Discovery never invokes git.** `RepoScanner` reads `.git/HEAD` and `.git/config` itself,
  so scanning 51 repos takes **90 ms** instead of ~35 s.
- **Keystrokes only filter memory.** The GitHub catalog is cached on disk (6 h TTL) and the
  public search is debounced 250 ms, well inside GitHub's 30-requests-per-minute search limit.
- **Status is lazy and cached.** Only the top 15 visible local rows get a `git status`, capped
  at 4 concurrent, cached with a fingerprint of `.git/HEAD` + `.git/index` mtime so unchanged
  repos are never recomputed. Untracked scanning is **off by default** — it is the single
  biggest cost (23 s vs 1.5 s on a repo with `node_modules`).

---

## Things the real data forced

Real repositories are messier than the spec assumes. All of these are handled:

- **Renamed repos.** Three local clones point at pre-rename URLs
  (`drone-sentinel` → `SentinelRF_Detector`). GitHub answers those with a `301`, so the
  extension follows it and caches the mapping permanently.
  *This required following redirects by hand:* .NET's automatic redirect handler **strips the
  `Authorization` header**, which turns a renamed **private** repo into a silent `404`.
- **Case-inconsistent owners.** Origin URLs say both `JoshuaWowk` and `joshuawowk` while the
  API login is lowercase, so every comparison is case-insensitive.
- **Folder name ≠ repo name.** `epicor-bpm-general` points at `epicor-bpm-newPartMessage`.
  Matching is always on the normalised remote URL, never the folder name.
- **One remote, several clones.** `airbyte`/`airbyte_old` and `snap1`/`esparagus-snapclient`
  are two clones of one repo each; they collapse into a single row that lists every clone.
- **Nested repos** (a repo inside another repo) are skipped by default.

---

## Layout

```
src/RepoSearch.Core/          Platform-agnostic engine (no CmdPal dependency)
  RepoScanner.cs              Fast local discovery, reads .git directly
  GitRemote.cs                Remote URL -> canonical "owner/name"
  GitStatusReader.cs          porcelain=v2 parsing, timeouts, one spawn per repo
  GitStatusInfo.cs            posh-git formatting
  GitHubClient.cs             REST client, manual auth-preserving redirects
  RepoCatalog.cs              Cached repo list + permanent rename map
  RepoIndex.cs                Joins local + remote into the 4 result types
  MatchScorer.cs              Ranking; result type dominates, score breaks ties
  GitOperations.cs            Clone / Sync / publish, and the Sync safety rules
  TokenStore.cs               Windows Credential Manager via P/Invoke
src/RepoSearch.Extension/     The Command Palette extension itself
tools/RepoSearch.Probe/       Test harness — runs the engine against real repos
build/                        install.ps1 / uninstall.ps1
```

`Directory.Build.props` redirects `bin`/`obj` to `%LOCALAPPDATA%` so OneDrive never syncs
build output.

Run the harness (33 assertions plus live checks against your real repos):

```bash
cd tools/RepoSearch.Probe && GH_TOKEN=<token> dotnet run -c Release
```

---

## Sync is deliberately conservative

`Sync` fetches, then re-reads status, then acts. It **never** merges or resolves for you:

| situation | what happens |
|---|---|
| behind only, clean | fast-forward (`merge --ff-only`) |
| ahead only, clean | `push` |
| in sync | nothing |
| diverged | **refused**, unless you enable rebase in settings |
| uncommitted changes | **refused** — commit or stash first |
| conflicts / detached / no upstream | **refused**, with the reason |

Untracked files alone never block a sync.

---

## Building without Visual Studio

This machine has only the .NET 10 SDK — no VS, no Windows SDK. Two things had to be worked
around, both in `RepoSearch.Extension.csproj`:

1. `CsWinRTGenerateProjection=false` — otherwise `cswinrt.exe` fails with *"Could not find the
   Windows SDK path in the registry"*, because CsWinRT resolves `TargetPlatformVersion`
   through a Windows Kits registry key that doesn't exist here. The SDK ships a pre-projected
   managed Toolkit assembly, so no projection is needed.
2. `EnableMsixTooling=false` and no `Microsoft.Windows.SDK.BuildTools.MSIX` reference —
   in-build MSIX packaging needs `Windows Kits\10\Platforms\UAP\<ver>\UAP.props` and fails
   `APPX3217` without it.

Deployment instead uses **loose-folder registration**: `dotnet publish` produces a normal
folder and `Add-AppxPackage -Register AppxManifest.xml` grants it package identity, needing
neither packaging tools nor a certificate. `AppxManifest.xml` is therefore hand-written
(concrete `Executable`/`EntryPoint`, `Windows.FullTrustApplication`, a real
`<Resource Language="en-us"/>` since there's no PRI index).

> The CLSID must stay identical in three places: the `[Guid]` on `RepoSearchExtension`,
> `com:Class Id`, and `CreateInstance ClassId`. A mismatch registers fine and then the
> extension silently never appears.

Also worth knowing: `net9.0` is impossible — the Toolkit ships in a `lib/net8.0-windows…`
folder but is compiled against .NET 10, so a net9 build dies with `CS1705`.

---

## Settings

| Setting | Default | Notes |
|---|---|---|
| Local repository folders | `…\OneDrive - Getinge AB\Repositories` | One per line |
| Scan depth | `3` | Levels below each root |
| Search public GitHub repositories | on | Turn off for own-repos-only |
| Show git status | on | |
| Show repositories in global search | on | Adds top matches to the palette's main list |
| Count untracked files in status | **off** | The 23 s vs 1.5 s lever |
| Let Sync rebase diverged branches | off | |
| Create new GitHub repos as private | on | Used by *Init* |
| Clone destination | first local root | |
| GitHub token | — | Write-only; moves to Credential Manager |
