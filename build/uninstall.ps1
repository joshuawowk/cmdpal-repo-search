<#
.SYNOPSIS
    Removes Repository Search from Command Palette.

.DESCRIPTION
    Unregisters the package. Optionally also deletes the cached repo catalog / git status
    cache, and the GitHub token stored in Windows Credential Manager.

.EXAMPLE
    pwsh -File build\uninstall.ps1 -RemoveData -RemoveToken
#>
[CmdletBinding()]
param(
    [switch]$RemoveData,
    [switch]$RemoveToken
)

$ErrorActionPreference = 'Stop'
$packageName = 'JoshuaWowk.RepositorySearchForCommandPalette'
$credentialTarget = 'cmdpal-repo-search:github'

function Write-Step($message) { Write-Host "==> $message" -ForegroundColor Cyan }

Get-Process -Name 'RepoSearch.Extension' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Step "Stopping running extension process $($_.Id)"
    $_ | Stop-Process -Force
}

$pkg = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue

if ($pkg) {
    Write-Step "Removing $($pkg.PackageFullName)"
    Remove-AppxPackage -Package $pkg.PackageFullName

    if ($RemoveData) {
        $state = Join-Path $env:LOCALAPPDATA "Packages\$($pkg.PackageFamilyName)"
        if (Test-Path $state) {
            Write-Step "Deleting cached data at $state"
            Remove-Item $state -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    Write-Host 'Package removed.' -ForegroundColor Green
}
else {
    Write-Host "Package '$packageName' is not registered." -ForegroundColor Yellow
}

if ($RemoveToken) {
    Write-Step 'Removing the stored GitHub token'
    & cmdkey /delete:$credentialTarget | Out-Null
    Write-Host 'Token removed from Credential Manager.' -ForegroundColor Green
}

Write-Host ''
Write-Host 'Restart Command Palette to drop the extension from its list.' -ForegroundColor Yellow
