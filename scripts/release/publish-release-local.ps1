[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$LegacyReleaseRoot = 'D:\project2\facm-release-3.5.17-selfsigned',
    [string]$Facm4ReleaseRoot = 'D:\project2\facm-release-4.0.0-selfsigned\release',
    [string]$TargetCommit = '',
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-FullPath([string]$Path) { return [IO.Path]::GetFullPath($Path) }
function Assert-DProject2Path([string]$Path, [string]$Label) {
    $full = Get-FullPath $Path
    if (-not $full.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase) -or $full -eq 'D:\project2') {
        throw "$Label must be a specific path under D:\project2: $full"
    }
    return $full
}
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-Sha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant() }
function Read-Json([string]$Path) { return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json }

$RepoRoot = (Resolve-Path $RepoRoot).Path
$LegacyReleaseRoot = Assert-DProject2Path $LegacyReleaseRoot 'LegacyReleaseRoot'
$Facm4ReleaseRoot = Assert-DProject2Path $Facm4ReleaseRoot 'Facm4ReleaseRoot'
Require (Test-Path -LiteralPath (Join-Path $LegacyReleaseRoot 'FACM.exe') -PathType Leaf) '3.5.17 FACM.exe is missing.'
Require (Test-Path -LiteralPath (Join-Path $Facm4ReleaseRoot 'FACM.exe') -PathType Leaf) '4.0 FACM.exe is missing.'
Require (Test-Path -LiteralPath (Join-Path $Facm4ReleaseRoot 'manifest.json.sig') -PathType Leaf) '4.0 application signature is missing.'

$legacyExe = Join-Path $LegacyReleaseRoot 'FACM.exe'
$legacyInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($legacyExe)
Require ($legacyInfo.FileVersion -eq '3.5.17.0') "Legacy artifact version is not 3.5.17: $($legacyInfo.FileVersion)"
$facm4Info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Facm4ReleaseRoot 'FACM.exe'))
Require ($facm4Info.FileVersion -eq '4.0.0.0') "4.0 bootstrapper version is not 4.0.0: $($facm4Info.FileVersion)"

$validator = Join-Path $RepoRoot 'tools\release\Test-FacmReleaseBundle.ps1'
& (Get-Command pwsh -ErrorAction Stop).Source -NoLogo -NoProfile -ExecutionPolicy Bypass -File $validator `
    -BundleRoot $Facm4ReleaseRoot -Bootstrapper (Join-Path $Facm4ReleaseRoot 'FACM.exe')
if ($LASTEXITCODE -ne 0) { throw "4.0 bundle validation failed: $LASTEXITCODE" }

$versionManifest = Read-Json (Join-Path $RepoRoot 'online\version.json')
Require ([string]$versionManifest.version -ceq '3.5.17') 'online/version.json is not staged for 3.5.17.'
Require ([string]$versionManifest.download_url -ceq 'https://github.com/xianyumht-cmd/facm/releases/download/v3.5.17/FACM.exe') 'Legacy online URL is not v3.5.17.'
Require ([string]$versionManifest.sha256 -ieq (Get-Sha256 $legacyExe)) 'Legacy online SHA-256 does not match the staged artifact.'
Require ([string]$versionManifest.migration.manifest_url -ceq 'https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0/manifest.json') 'Migration manifest URL is not v4.0.0.'
Require ([string]$versionManifest.migration.bootstrapper_sha256 -ieq (Get-Sha256 (Join-Path $Facm4ReleaseRoot 'FACM.exe'))) 'Migration bootstrapper SHA-256 does not match the staged artifact.'

$target = if ([string]::IsNullOrWhiteSpace($TargetCommit)) { (& git -C $RepoRoot rev-parse HEAD).Trim() } else { $TargetCommit.Trim() }
Require ($target -match '^[0-9a-f]{40}$') "TargetCommit is not a full commit SHA: $target"

Write-Host "Local release preview passed. Target commit: $target"
Write-Host "3.5.17 SHA-256: $(Get-Sha256 $legacyExe)"
Write-Host "4.0.0 bootstrapper SHA-256: $(Get-Sha256 (Join-Path $Facm4ReleaseRoot 'FACM.exe'))"
Write-Host "4.0.0 manifest SHA-256: $(Get-Sha256 (Join-Path $Facm4ReleaseRoot 'manifest.json'))"

if (-not $Publish) {
    Write-Host 'Preview only. Re-run with -Publish after the target commit is pushed.'
    exit 0
}

Require ((Get-Command gh -ErrorAction SilentlyContinue) -ne $null) 'GitHub CLI is required for local publication.'
& gh release create v3.5.17 `
    (Join-Path $LegacyReleaseRoot 'FACM.exe') `
    (Join-Path $LegacyReleaseRoot 'SHA256.txt') `
    --repo xianyumht-cmd/facm --target $target --title 'FACM 3.5.17 Bridge' `
    --notes 'FACM 3.5.17 旧版自动更新过渡版。安装后会自动迁移到 FACM 4.0。此版本使用 FACM 自签名证书。'
if ($LASTEXITCODE -ne 0) { throw 'Creating v3.5.17 GitHub Release failed.' }

$facm4Assets = @(Get-ChildItem -LiteralPath $Facm4ReleaseRoot -File | Where-Object {
    $_.Name -notin @('self-signed-release-evidence.json')
} | ForEach-Object { $_.FullName })
& gh release create v4.0.0 @facm4Assets `
    --repo xianyumht-cmd/facm --target $target --title 'FACM 4.0.0' `
    --notes 'FACM 4.0.0 自签名组件发布。旧版用户请先通过 3.5.17 自动迁移；新用户可下载 FACM.exe 与 bootstrap.json。'
if ($LASTEXITCODE -ne 0) { throw 'Creating v4.0.0 GitHub Release failed.' }

Write-Host 'GitHub Releases v3.5.17 and v4.0.0 created successfully.'
