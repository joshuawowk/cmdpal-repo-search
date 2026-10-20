<#
.SYNOPSIS
    Builds distributable MSIX packages (x64, ARM64) and an .msixbundle.

.DESCRIPTION
    Command Palette discovers extensions only through AppExtensionCatalog
    ("com.microsoft.commandpalette"), which enumerates packaged apps. An unpackaged EXE that just
    registers its COM CLSID is never found, so the distributable must be an MSIX.

    In-build MSIX tooling stays disabled (see the csproj) so the loose-folder dev loop in
    install.ps1 keeps working without a Windows SDK. This script instead publishes a normal
    folder per architecture, stamps AppxManifest.xml, and packs it with makeappx.exe from the
    Windows SDK (present on GitHub's windows-2022 runners).

    Signing is optional: set SIGNING_PFX_BASE64 and SIGNING_PFX_PASSWORD and the packages are
    signed with signtool. The certificate subject must equal -Publisher. Unsigned output is
    what Partner Center expects for a Microsoft Store submission (the Store signs it).

.EXAMPLE
    pwsh -File build\build-msix.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    # Package version. Accepts 0.1.0, v0.1.0 or 0.1.0.0; defaults to AppxManifest.xml.
    [string]$Version = '',

    # Package identity. Default to the values in AppxManifest.xml; override with the Partner
    # Center identity (or env IDENTITY_NAME / IDENTITY_PUBLISHER) for a Store build.
    [string]$IdentityName = $env:IDENTITY_NAME,
    [string]$Publisher = $env:IDENTITY_PUBLISHER,

    [string[]]$Platforms = @('x64', 'ARM64'),

    [string]$OutputDir = 'artifacts'
)

$ErrorActionPreference = 'Stop'

$repoRoot       = Split-Path -Parent $PSScriptRoot
$project        = Join-Path $repoRoot 'src\RepoSearch.Extension\RepoSearch.Extension.csproj'
$sourceManifest = Join-Path $repoRoot 'src\RepoSearch.Extension\AppxManifest.xml'
$outRoot        = if ([IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $repoRoot $OutputDir }
$workRoot       = Join-Path $outRoot 'work'

function Write-Step($message) { Write-Host "==> $message" -ForegroundColor Cyan }

function Find-SdkTool([string]$name) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem -Path $kits -Filter $name -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\10\.[\d.]+\\x64\\' } |
        Sort-Object FullName |
        Select-Object -Last 1
    if (-not $tool) { throw "$name not found under $kits. Install the Windows SDK." }
    return $tool.FullName
}

# ---------------------------------------------------------------- version + identity

[xml]$template = Get-Content -Raw $sourceManifest
if (-not $Version) { $Version = $template.Package.Identity.Version }
$Version = ($Version.TrimStart('v') -split '[-+]')[0]
$parts = @($Version.Split('.'))
while ($parts.Count -lt 4) { $parts += '0' }
if ($parts.Count -ne 4 -or ($parts | Where-Object { $_ -notmatch '^\d+$' })) {
    throw "Version '$Version' is not a valid MSIX version (major.minor.build.revision)."
}
$Version = $parts -join '.'
if (-not $IdentityName) { $IdentityName = $template.Package.Identity.Name }
if (-not $Publisher)    { $Publisher    = $template.Package.Identity.Publisher }

Write-Step "Version $Version, identity $IdentityName ($Publisher)"

$makeappx = Find-SdkTool 'makeappx.exe'
Write-Step "Using $makeappx"

Remove-Item -Recurse -Force $workRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outRoot, $workRoot | Out-Null
$bundleDir = Join-Path $workRoot 'bundle'
New-Item -ItemType Directory -Force -Path $bundleDir | Out-Null

$packages = @()

foreach ($platform in $Platforms) {
    $rid  = if ($platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }
    $arch = $platform.ToLowerInvariant()
    $publishDir = Join-Path $workRoot "publish\$rid"

    Write-Step "Publishing $platform ($rid)"
    & dotnet publish $project -c Release -p:Platform=$platform -r $rid --self-contained true `
        -p:Version=$Version -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $platform ($LASTEXITCODE)" }

    # PublicFolder="Public" in the AppExtension; makeappx drops empty folders, so keep a file.
    $public = Join-Path $publishDir 'Public'
    New-Item -ItemType Directory -Force -Path $public | Out-Null
    Set-Content -Path (Join-Path $public 'README.txt') -Value 'Repository Search for Command Palette'

    [xml]$manifest = Get-Content -Raw (Join-Path $publishDir 'AppxManifest.xml')
    $manifest.Package.Identity.SetAttribute('Name', $IdentityName)
    $manifest.Package.Identity.SetAttribute('Publisher', $Publisher)
    $manifest.Package.Identity.SetAttribute('Version', $Version)
    $manifest.Package.Identity.SetAttribute('ProcessorArchitecture', $arch)
    $manifest.Save((Join-Path $publishDir 'AppxManifest.xml'))

    $msix = Join-Path $outRoot "RepositorySearch_${Version}_$arch.msix"
    Write-Step "Packing $msix"
    & $makeappx pack /o /h SHA256 /d $publishDir /p $msix
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed for $platform ($LASTEXITCODE)" }

    Copy-Item $msix $bundleDir
    $packages += $msix
}

$bundle = Join-Path $outRoot "RepositorySearch_${Version}_Bundle.msixbundle"
Write-Step "Bundling $bundle"
& $makeappx bundle /o /bv $Version /d $bundleDir /p $bundle
if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed ($LASTEXITCODE)" }
$packages += $bundle

# ---------------------------------------------------------------- optional signing

if ($env:SIGNING_PFX_BASE64) {
    $signtool = Find-SdkTool 'signtool.exe'
    $pfx = Join-Path $workRoot 'signing.pfx'
    [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SIGNING_PFX_BASE64))
    try {
        foreach ($p in $packages) {
            Write-Step "Signing $p"
            & $signtool sign /fd SHA256 /f $pfx /p $env:SIGNING_PFX_PASSWORD `
                /tr http://timestamp.digicert.com /td SHA256 $p
            if ($LASTEXITCODE -ne 0) { throw "signtool failed for $p ($LASTEXITCODE)" }
        }
    }
    finally {
        Remove-Item -Force $pfx -ErrorAction SilentlyContinue
    }
}
else {
    Write-Warning 'SIGNING_PFX_BASE64 not set: packages are UNSIGNED (fine for Partner Center; not installable by end users as-is).'
}

Remove-Item -Recurse -Force $workRoot
Get-ChildItem $outRoot | ForEach-Object { Write-Host ("{0,-60} {1,12:N0} bytes" -f $_.Name, $_.Length) }
