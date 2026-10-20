# Microsoft Store listing - copy/paste fields for Partner Center

Everything below is written to be pasted straight into Partner Center
(Apps and games > the product > Submission > Store listings > English (United States)).
Limits quoted here are the ones Partner Center documented when this file was written
(2026-10-20); the dashboard is the authority, so if a field refuses the text, trust the dashboard
and tell Claude. Docs: <https://learn.microsoft.com/en-us/windows/powertoys/command-palette/publish-extension-store>

Values that depend on your Partner Center account (Package/Identity/Name, Publisher,
PublisherDisplayName, Store ID) are deliberately not in this file; see `store/PARTNER-CENTER.md`.

## Product name

Primary (the display name already in `src/RepoSearch.Extension/AppxManifest.xml`):

```
Repository Search
```

This is a generic name and may already be taken. If Partner Center says it is unavailable, reserve one
of these instead, in this order:

```
Repository Search for Command Palette
```

```
Repo Search - Git and GitHub for Command Palette
```

If you reserve anything other than "Repository Search", tell Claude which one. The package's
`DisplayName` in `AppxManifest.xml` has to match a reserved name or the upload is rejected, and Claude
must change it before the package is rebuilt (`store/PARTNER-CENTER.md`, step 3). Avoid "Official" and
avoid putting "PowerToys" or "Microsoft" first in the name.

## Short description

Optional in Partner Center for a PC app (limit 1,000 characters); also reusable as the Gallery
`shortDescription` (limit 200). 197 characters:

```
Search your local git repositories and GitHub in one list from PowerToys Command Palette. Open, clone, fork, star and sync. Requires PowerToys; independent, not affiliated with Microsoft or GitHub.
```

## Description

2327 characters as written below (limit 10,000; 2346 if Partner Center counts each line break as two).
It says that PowerToys with Command Palette is required, that the project is independent and not
affiliated with Microsoft or GitHub, and it does not use the word "Official". Paste it as plain text;
Partner Center does not render Markdown, so the headings are in capitals.

```
Repository Search is a Command Palette extension for people who keep a lot of git repositories. Type part of a name and it searches the folders on your PC and GitHub at the same time, with your own repositories ranked first.

REQUIRES MICROSOFT POWERTOYS. This is an extension, not a standalone app: it does nothing until Microsoft PowerToys is installed and Command Palette is enabled. After installing, restart Command Palette and Repository Search appears in its command list. Git for Windows is needed for the status, clone and sync actions.

Independent project. Repository Search is not affiliated with, endorsed by or sponsored by Microsoft or GitHub. PowerToys, Command Palette and GitHub are trademarks of their respective owners.

WHAT YOU CAN DO
- Find a repository by name whether it is cloned on disk, only on GitHub, or both.
- See which GitHub repositories you already have a local clone of, and open the clone in File Explorer, Visual Studio Code or the browser.
- Read a compact git status summary (branch, ahead/behind, changed files, stashes) on every local row.
- Sync a clone safely: it fast-forwards or pushes, and refuses when there are uncommitted changes, a diverged branch or conflicts.
- Clone repositories you have not cloned yet.
- Star, fork, or fork and clone other people's public repositories.
- Publish a local repository that has no remote to a new GitHub repository (private by default).
- Let your top matches appear in Command Palette's main search, without opening the extension first. This can be switched off.

PRIVACY AND YOUR GITHUB TOKEN
Searching your local folders needs no account and no token, and in that mode the extension makes no network requests. Searching GitHub (your repositories and public ones) needs a GitHub personal access token, which is optional. The extension looks for one in Windows Credential Manager, then in the GH_TOKEN and GITHUB_TOKEN environment variables, then from the GitHub CLI (gh). A token you paste into the settings is stored in Windows Credential Manager, not in a settings file, and is sent only to api.github.com. There is no telemetry, no analytics and no advertising. Full policy: https://github.com/joshuawowk/cmdpal-repo-search/blob/main/docs/PRIVACY.md

Free and open source (MIT license): https://github.com/joshuawowk/cmdpal-repo-search
```

## Product features

20 of 20 (limit 20; each at most 200 characters). Paste one per box.

1. Search local and GitHub repositories together in one ranked list
2. Your own repositories first: GitHub repos paired with their local clones rank above everything else
3. Finds local clones by reading .git metadata directly instead of running git, so scanning stays fast
4. Open a repository in File Explorer, Visual Studio Code or your browser
5. Git status summary on every local row: branch, ahead/behind, changed files and stashes
6. Safe Sync: fast-forwards or pushes, and refuses uncommitted changes, diverged branches and conflicts
7. Clone any GitHub repository into a folder you choose
8. Star, fork, or fork and clone other people's public repositories
9. Init: publish a local repository with no remote to a new GitHub repository, private by default
10. Top matches appear in Command Palette's main search, with a setting to turn it off
11. Works with no account: local search needs no token and makes no network requests
12. Optional GitHub token, stored in Windows Credential Manager and sent only to api.github.com
13. Reads a token from Credential Manager, GH_TOKEN/GITHUB_TOKEN or the GitHub CLI, in that order
14. GitHub repository list cached on disk, so typing only filters memory
15. Follows renamed GitHub repositories so older clones still pair up with the right repo
16. Choose which folders to scan, how deep to look, and whether to include public GitHub results
17. Matches repositories case-insensitively by remote URL, not by folder name
18. No telemetry, no analytics, no advertising
19. Free and open source under the MIT license
20. Native builds for x64 and ARM64 Windows PCs

## Search terms

7 of 7 (limit 7; each at most 30 characters). Paste one per box.

1. github
2. git repository
3. repository search
4. command palette
5. powertoys
6. clone repo
7. developer tools

## Category

- Category: **Developer tools**
- Subcategory: pick the closest match offered under Developer tools (if there is a choice, "Developer
  tools" or "Utilities and tools"); the Gallery entry already uses developer-tools, productivity and
  utilities-and-tools.

## Privacy policy URL

```
https://github.com/joshuawowk/cmdpal-repo-search/blob/main/docs/PRIVACY.md
```

The package uses the `internetClient` capability and sends your GitHub token and search text to
GitHub, so a privacy policy is required. If a Partner Center question asks whether the
product handles personal information or credentials, the accurate answer is that it uses an optional
GitHub token that the user supplies, stored locally in Windows Credential Manager and sent only to
api.github.com; nothing is sent to the developer. The policy describes this.

## Support contact

- Support contact info (email or URL): `josh@jwowk.net`
- Developer website / product website: `https://github.com/joshuawowk/cmdpal-repo-search`
- Support/issue tracker (use in the description or as the support URL if you prefer a link):
  `https://github.com/joshuawowk/cmdpal-repo-search/issues`
- Copyright and trademark info: `Copyright (c) 2026 Josh Wowk. MIT License.`
- Additional license terms: leave blank (the standard Microsoft Store terms apply), or link to
  `https://github.com/joshuawowk/cmdpal-repo-search/blob/main/LICENSE`

## Properties and system requirements

- Minimum OS: Windows 10 version 2004 (build 19041) or later, x64 and ARM64. This is what the package
  manifest declares (`MinVersion="10.0.19041.0"`).
- Additional requirements to state (the Description also says this): Microsoft PowerToys with Command
  Palette enabled; Git for Windows for status, clone, sync and publish.
- Declared capabilities: `internetClient`, and the restricted capability `runFullTrust`. Partner
  Center asks for a justification for the restricted one; it is in "Notes for certification" below.

## Age rating notes

Complete the IARC questionnaire (Submission > Age ratings) with these answers. They describe the
product as of this file; the questionnaire is yours to answer truthfully, and the rating it generates is
what counts.

- Product type: utility / productivity / developer tool (not a game, not a social app).
- Violence, blood, fear, sexual content, nudity, profanity, drugs, alcohol, tobacco, gambling,
  simulated gambling: **No** to all. The extension generates no such content.
- User-generated content / users can interact or chat with each other: **No**. The extension has no
  social features. It displays GitHub repository names and descriptions that GitHub returns, which
  other people wrote, but it provides no way to publish or exchange content with other users.
- Shares the user's location: **No**.
- Collects personal information / requires an account: it uses an optional GitHub token that the user
  provides; nothing is collected by or sent to the developer.
- Unrestricted internet access / web browsing: **No**. It calls only api.github.com. Some actions open
  the user's own browser at a github.com page.
- In-app purchases or ads: **No**.
- Expected outcome: the lowest age category (for example IARC 3+ / ESRB Everyone). Confirm in the
  dashboard.

## Notes for certification

Paste this into "Notes for certification" (Submission > Properties/Submission options). No test account
or token is needed or supplied.

```
WHAT THIS IS
Repository Search is an extension for Command Palette, part of Microsoft PowerToys. It is not a
standalone app, so it has no window of its own. It adds a "Repository Search" command to Command
Palette that searches local git repositories and GitHub. No account, sign-in or token is required to
test it. No credentials are provided or needed.

INSTALL POWERTOYS AND ENABLE COMMAND PALETTE (about 5 minutes)
1. On a Windows 10 (2004+) or Windows 11 PC, install PowerToys: open a terminal and run
   winget install Microsoft.PowerToys
   (or install "PowerToys" from the Microsoft Store, or from https://github.com/microsoft/PowerToys).
2. Open PowerToys Settings, choose "Command Palette" in the left list, and turn "Enable Command
   Palette" on. The default shortcut to open it is Win+Alt+Space.
3. Optional but recommended: install Git for Windows (winget install Git.Git). It is needed only
   for the git status, clone, sync and publish actions, not for finding repositories.

INSTALL AND EXERCISE THE EXTENSION
4. Install this package from the Store listing (or from the submitted package).
5. Restart Command Palette so it picks up the new extension: run its "Reload" command, or exit
   PowerToys from its tray icon and start it again.
6. Create a test repository for it to find. In a terminal:
   mkdir %USERPROFILE%\source\repos\certification-test
   cd %USERPROFILE%\source\repos\certification-test
   git init
   (The extension scans %USERPROFILE%\source\repos by default. Another folder can be set in the
   extension's settings: Command Palette > Settings > Extensions > Repository Search.)
7. Press Win+Alt+Space, type "Repository Search" and press Enter. Type "certification". The test
   repository appears as a local repository (with a git status summary when Git for Windows is
   installed), and its actions (Enter for
   the default, Ctrl+K for more) include opening it in File Explorer or in Visual Studio Code (if
   installed). A "No GitHub token" notice is expected and normal.
8. Optional, to see the GitHub half (needs your own free GitHub account and a personal access token
   that you create yourself; none is supplied): open the extension's settings, paste a token into
   "GitHub token (write-only)" and save. The token moves into Windows Credential Manager and the
   field clears. Your repositories and public GitHub results then appear in the same list. Typing
   "clear" in the token field removes it. This step is not required to verify the listing.

CAPABILITY JUSTIFICATION
- runFullTrust (restricted capability): Command Palette extensions are out-of-process COM servers
  that Command Palette starts as a full-trust desktop process. The extension needs it to read the
  user's configured repository folders and to start git.exe, File Explorer and Visual Studio Code.
- internetClient: the only network destination is https://api.github.com, for the GitHub features
  above. With no token configured the extension makes no network requests.

PRIVACY
No telemetry, analytics or advertising. Privacy policy:
https://github.com/joshuawowk/cmdpal-repo-search/blob/main/docs/PRIVACY.md

AFFILIATION
Independent project. Not affiliated with or endorsed by Microsoft or GitHub. Source (MIT):
https://github.com/joshuawowk/cmdpal-repo-search
```

## Screenshots and logos

Screenshots must be captured on Windows; see `store/PARTNER-CENTER.md`, step 5. Suggested captures:
the Repository Search list with local and GitHub rows, the actions menu on a paired repository, and the
settings page. The Store icon is taken from the package unless you upload one; if Partner Center
requires a 1:1 logo at 300x300 or larger, the repository only has a 256x256 source PNG
(`assets-source/repo-search-icon-256.png`) and a vector source (`assets-source/repo-search-icon.svg`);
ask Claude to export a larger one.
