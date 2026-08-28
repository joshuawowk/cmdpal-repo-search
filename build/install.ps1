<#
.SYNOPSIS
    Builds Repository Search and registers it with Command Palette.

.DESCRIPTION
    Deploys by "loose folder registration": dotnet publish produces a normal folder, and
    Add-AppxPackage -Register grants it package identity straight from AppxManifest.xml.

    This needs NO Visual Studio, NO Windows SDK, NO makeappx/signtool and NO signing
    certificate. It does need Developer Mode, which this machine already has enabled.

    After registering, Command Palette must be restarted to pick up a newly registered
    extension; -RestartCmdPal does that for you.

.EXAMPLE
    pwsh -File build\install.ps1 -RestartCmdPal
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',

    [switch]$RestartCmdPal,

    # Skip the build and just re-register what is already published.
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot 'src\RepoSearch.Extension'
$project    = Join-Path $projectDir 'RepoSearch.Extension.csproj'
$rid        = if ($Platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }

# Build output is redirected out of OneDrive by Directory.Build.props.
$publishDir = Join-Path $env:LOCALAPPDATA "cmdpal-repo-search-build\RepoSearch.Extension\bin\$Platform\$Configuration\net10.0-windows10.0.26100.0\$rid\publish"

function Write-Step($message) { Write-Host "==> $message" -ForegroundColor Cyan }

# ---------------------------------------------------------------- preflight

$devMode = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' `
    -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue

if (-not $devMode -or $devMode.AllowDevelopmentWithoutDevLicense -ne 1) {
    Write-Warning @'
Developer Mode appears to be OFF. Add-AppxPackage -Register needs it.
Enable it at Settings > System > For developers > Developer Mode, then re-run this script.
'@
}

# ---------------------------------------------------------------- build

if (-not $NoBuild) {
    Write-Step "Publishing $Configuration/$Platform ..."

    & dotnet publish $project `
        -c $Configuration `
        -p:Platform=$Platform `
        -r $rid `
        --self-contained true `
        --nologo

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}

if (-not (Test-Path $publishDir)) { throw "Publish folder not found: $publishDir" }

$manifest = Join-Path $publishDir 'AppxManifest.xml'
if (-not (Test-Path $manifest)) { throw "AppxManifest.xml missing from $publishDir" }

# The manifest declares PublicFolder="Public"; registration fails if the folder is absent.
$publicFolder = Join-Path $publishDir 'Public'
if (-not (Test-Path $publicFolder)) {
    Write-Step 'Creating the Public folder required by the AppExtension declaration'
    New-Item -ItemType Directory -Path $publicFolder | Out-Null
}

# ---------------------------------------------------------------- register

Write-Step "Registering package from $publishDir"

try {
    Add-AppxPackage -Register $manifest -ForceUpdateFromAnyVersion
}
catch {
    Write-Error @"
Registration failed: $($_.Exception.Message)

Most common causes:
  * Developer Mode is off (see above).
  * A previous copy is still registered - remove it with build\uninstall.ps1 and retry.
  * The extension process is still running - close Command Palette and retry.
"@
    throw
}

$pkg = Get-AppxPackage -Name 'JoshuaWowk.RepositorySearchForCommandPalette'
if (-not $pkg) { throw 'Package did not appear after registration.' }

Write-Host ''
Write-Host "Registered $($pkg.Name) $($pkg.Version)" -ForegroundColor Green
Write-Host "  install location: $($pkg.InstallLocation)"

# ---------------------------------------------------------------- restart CmdPal

if ($RestartCmdPal) {
    Write-Step 'Restarting Command Palette so it rescans the extension catalog'

    Get-Process -Name 'Microsoft.CmdPal.UI' -ErrorAction SilentlyContinue | ForEach-Object {
        $_ | Stop-Process -Force
    }

    Start-Sleep -Seconds 2
    Start-Process 'shell:AppsFolder\Microsoft.CommandPalette_8wekyb3d8bbwe!App'
    Write-Host 'Command Palette restarted.' -ForegroundColor Green
}
else {
    Write-Host ''
    Write-Host 'Restart Command Palette to pick up the extension (or re-run with -RestartCmdPal).' -ForegroundColor Yellow
}
