# Publishing Repository Search to the Microsoft Store - walkthrough

Why the Store: Command Palette only discovers extensions that are installed as MSIX packages, and an
MSIX only installs on other people's PCs if it is signed by a trusted certificate. The Store signs the
package for you, so there is no certificate to buy.

Written 2026-10-20. Items marked *(confirm)* are details from Microsoft's documentation that I could not
re-check from here; the Partner Center page in front of you wins if it disagrees.

Who does what: **you** do the steps that need your Microsoft login or a Windows PC (1, 2, 5, 6).
**Claude** does the repository side (3, 7). Step 4 is a button click in GitHub that either of you can do.

Files in this kit:

- `store/listing.md` - every listing field, ready to paste.
- `docs/PRIVACY.md` - the privacy policy; public URL
  <https://github.com/joshuawowk/cmdpal-repo-search/blob/main/docs/PRIVACY.md> (it only works once these
  files are pushed to `main`, which is part of the commit that added them).

## Before you start: current state of the build

- The release workflow (`.github/workflows/release-extension.yml`) **does** produce a `.msixbundle`.
  One run builds `RepositorySearch_<version>_x64.msix`, `RepositorySearch_<version>_arm64.msix` and
  `RepositorySearch_<version>_Bundle.msixbundle`, and uploads all three in one artifact called
  `repository-search-msix`.
- The package is **unsigned** on purpose. `build/build-msix.ps1` documents this: "Unsigned output is
  what Partner Center expects for a Microsoft Store submission (the Store signs it)." The signing
  secrets (`SIGNING_PFX_BASE64`, `SIGNING_PFX_PASSWORD`) stay unset.
- As of 2026-10-20 (`gh variable list -R joshuawowk/cmdpal-repo-search` returned nothing), the repository
  has **no** `MSIX_IDENTITY_NAME` / `MSIX_IDENTITY_PUBLISHER` variables. The two successful workflow runs
  from that day therefore built packages with the placeholder development identity
  (`CN=Joshua Wowk`). **Those artifacts cannot be uploaded to Partner Center.** Build again after
  step 3.

## 1. Create a free individual Partner Center account

1. Go to <https://partner.microsoft.com/dashboard/registration> and sign in with a **personal**
   Microsoft account (not an employer or school account, because the developer account is permanent
   and public).
2. Choose the **Apps and games** program (the Windows/Xbox app developer program) and the **Individual**
   account type. Registration is free for individuals according to Microsoft's current publishing guide
   *(confirm on the registration page; company accounts are different and are not needed here)*.
3. Fill in contact details and your publisher display name. This name is shown on the Store listing. Use
   the same spelling you want to appear in the package (`AppxManifest.xml` currently says "Joshua
   Wowk"; LICENSE says "Josh Wowk"). Whatever you choose, Claude will make the manifest match in
   step 3.
4. Complete the identity verification Microsoft asks for *(confirm)*. This can take from minutes to a few
   days, and you cannot reserve names or submit until it is done.

## 2. Reserve the product name

1. In the dashboard open **Apps and games**, choose **New product**, then **MSIX or PWA app**.
2. Try **`Repository Search`** first.
3. If it is unavailable, reserve the next one from `store/listing.md`, in order:
   `Repository Search for Command Palette`, then `Repo Search - Git and GitHub for Command Palette`.
4. Tell Claude which name you got. If it is not exactly `Repository Search`, the package's display name
   must be changed to match before the upload (step 3).
5. A reserved name is held for a limited time unless you submit *(Microsoft documents roughly three months;
   confirm)*, so don't leave it too long.

## 3. Copy the product identity and send it to Claude

1. In the product, open **Product management > Product identity**.
2. Copy these three values exactly, including the `CN=` prefix. Also copy the **Package Family Name**
   if shown (it is optional but useful for the Gallery entry):
   - `Package/Identity/Name` - looks like `12345YourName.RepositorySearch`
   - `Package/Identity/Publisher` - looks like `CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX`
   - `Package/Properties/PublisherDisplayName` - your publisher display name
3. Send them to Claude in the chat. They identify the package; they are not secrets.
4. Claude then:
   - sets the repository **variables** (Settings > Secrets and variables > Actions > Variables)
     `MSIX_IDENTITY_NAME` = the Name and `MSIX_IDENTITY_PUBLISHER` = the Publisher;
   - compares `PublisherDisplayName` with `src/RepoSearch.Extension/AppxManifest.xml` (currently
     `Joshua Wowk`) and, if different, updates the manifest, commits and pushes;
   - if you reserved a name other than `Repository Search`, updates `DisplayName` in the manifest the same
     way.

   **How the workflow uses the variables** (read from `.github/workflows/release-extension.yml` and
   `build/build-msix.ps1`): the "Build MSIX packages" step passes `vars.MSIX_IDENTITY_NAME` and
   `vars.MSIX_IDENTITY_PUBLISHER` as the environment variables `IDENTITY_NAME` and `IDENTITY_PUBLISHER`.
   `build-msix.ps1` reads those as its `-IdentityName` and `-Publisher` parameters, and, for each of x64 and
   ARM64, writes them into the `Name=` and `Publisher=` attributes of the `<Identity>` element in the
   copy of `AppxManifest.xml` that goes into the package (along with `Version` and
   `ProcessorArchitecture`). If a variable is empty, the script **silently falls back** to the value
   committed in `AppxManifest.xml` (the development identity), and Partner Center will reject the
   upload. **Only `Name` and `Publisher` come from variables.** `PublisherDisplayName` and `DisplayName`
   are not stamped; they are whatever is committed in `AppxManifest.xml`, which is why Claude edits the
   file when they differ.

## 4. Build the package

Do this only after Claude confirms step 3 is done.

1. On GitHub open the repository's **Actions** tab, choose **Release extension**, then **Run workflow**.
   Branch `main`; leave **publish** unticked. (Claude can start it instead with `gh workflow run`.)
   This builds only. It does not create a tag, a release or a draft release.
2. Wait for the run to go green (the earlier runs took about 1.5 minutes).
3. On the run's summary page, under **Artifacts**, download **`repository-search-msix`** (a zip). It
   contains three files.
4. **Upload this file to Partner Center: `RepositorySearch_<version>_Bundle.msixbundle`.** The bundle
   holds both the x64 and ARM64 packages. Don't upload the two `.msix` files separately as well.
   Without a `v*` tag the version is taken from the manifest, which is `0.1.0.0`; the Store needs a
   higher version for each later submission; for an update, Claude will ask you to push a `v*` tag, which sets the version.
5. Artifacts expire after the repository's retention period (GitHub's default is 90 days), so upload
   promptly or re-run.

## 5. Upload the package, fill in the listing, capture screenshots

1. In the product choose **Start your submission**.
2. **Pricing and availability:** Free. Leave the default markets and visibility unless you want
   otherwise.
3. **Properties:** category **Developer tools**; paste the privacy policy URL, the support contact, and the
   system requirements text from `store/listing.md`. Partner Center will list the capabilities it found in
   the package (`internetClient`, and `runFullTrust`, a restricted capability that needs a justification
   which is already written in the Notes for certification).
4. **Age ratings:** run the questionnaire using the answers in `store/listing.md`.
5. **Packages:** drag in the `.msixbundle` from step 4 and wait for validation to pass. If it shows an
   identity or publisher error, the variables from step 3 were not set when the package was built; tell
   Claude and rebuild.
6. **Store listings (English - United States):** paste the product name, description, short description,
   features and search terms from `store/listing.md`.
7. **Screenshots - you have to do this on Windows; it can't be produced on Linux or from this repository.**
   - The Store requires **at least one** screenshot, PNG, **at least 1366 x 768 pixels** for a PC
     screenshot, and accepts up to 10 *(per Microsoft's listing documentation; confirm in the dashboard)*.
   - Install PowerToys and the extension (the package from step 4 can't be installed by hand because it is
     unsigned; use the local build route in the README: `pwsh -File build\install.ps1 -RestartCmdPal`),
     open Command Palette, and capture the Repository Search list with Win+Shift+S or the Snipping Tool at
     a size of 1366 x 768 or larger.
   - Suggested shots: the main list with local and GitHub rows; the actions menu on a repository; the
     settings page.
   - **Check each screenshot before uploading:** it must not show private repository names, your
     employer's repositories, folder paths you'd rather not publish, or a token. Use public repositories or
     a scratch folder.
   - If asked for a 1:1 store logo of 300 x 300 or more, tell Claude; the repository's source art is only
     256 x 256 and Claude can export a larger one from the SVG.
8. **Submission options > Notes for certification:** paste the block from `store/listing.md`.

## 6. Submit

1. Review the submission summary, then choose **Submit to the Store**.
2. Certification typically takes up to about three business days; many submissions finish within one day,
   and a first submission can take longer *(Microsoft's stated range; confirm in the dashboard)*. You get
   an email and the dashboard shows the status.
3. If it fails, open the certification report, copy the failure text and send it to Claude. The most likely
   causes are the package identity not matching the reservation, or a question about the `runFullTrust`
   capability or the extension needing PowerToys (the Notes for certification answer both).
4. When it passes, the listing goes live (by default as soon as it's certified).

## 7. After it goes live

1. Open the product's **Product identity** page, or the Store page URL, and copy the Store product ID:
   12 characters starting with `9`, as in `https://apps.microsoft.com/detail/9XXXXXXXXXXX`.
2. Send the ID to Claude. Claude will then:
   - update the README "Install" section: replace "Microsoft Store (coming soon)" with the Store link and
     the `winget install --source msstore 9XXXXXXXXXXX` command;
   - update `gallery/joshuawowk/repository-search/extension.json`: add an install source
     `{"type": "msstore", "id": "9XXXXXXXXXXX"}`, add the package family name for installed-state detection
     if you sent it, and **remove the existing `winget` install source**, because no WinGet package is
     published and the Gallery only accepts sources that exist;
   - fork `microsoft/CmdPal-Extensions`, add `extensions/joshuawowk/repository-search/` (the JSON, the
     icon and, if you send them, the screenshots) and open the pull request. Merge and the later refresh
     of the Gallery feed are done by Microsoft's maintainers.

## Things not to do

- Don't upload a package built before step 3 (wrong identity).
- Don't push a `v*` tag or publish a release for this: the Store route doesn't need one, and a tag creates
  a draft GitHub release. (A tag is only for later updates, when a higher version number is needed.)
- Don't put a real GitHub token in anything you hand to certification testers.
