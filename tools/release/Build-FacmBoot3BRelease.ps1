[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$OutputRoot = 'D:\project2\facm-boot3b-release-20260831',
    [string]$Version = '4.0.0-boot3b',
    [string]$ManifestBaseUrl = 'https://updates.facm.example/facm/4.0.0-boot3b',
    [string[]]$MirrorBaseUrls = @(),
    [switch]$SkipBoot2Build,
    [string]$Boot2MirrorRoot = '',
    [string]$Boot2BuildRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-FullPath([string]$Path) { return [IO.Path]::GetFullPath($Path) }

function Assert-DProject2Path([string]$Path, [string]$Label) {
    $full = Get-FullPath $Path
    if (-not $full.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase) -or
        $full -eq 'D:\project2') {
        throw "$Label must be a specific path under D:\project2: $full"
    }
    return $full
}

function Remove-Scope([string]$Path) {
    $full = Assert-DProject2Path $Path 'Output path'
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

function New-CleanDirectory([string]$Path) {
    Remove-Scope $Path
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Convert-ToUnixRelative([string]$Path) { return $Path.Replace('\', '/') }

function Assert-Https([string]$Url, [string]$Label) {
    $uri = [Uri]$Url
    if ($uri.Scheme -ne 'https' -or [string]::IsNullOrWhiteSpace($uri.Host)) {
        throw "$Label must be an HTTPS URL: $Url"
    }
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-DirectoryDigest([string]$Root) {
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File)) {
        [void]$paths.Add((Convert-ToUnixRelative ([IO.Path]::GetRelativePath($Root, $file.FullName))))
    }
    $paths.Sort([StringComparer]::Ordinal)
    $descriptor = [Text.StringBuilder]::new()
    foreach ($relative in $paths) {
        $file = Get-Item -LiteralPath (Join-Path $Root ($relative -replace '/', '\'))
        [void]$descriptor.Append($relative).Append("`n").Append((Get-Sha256 $file.FullName)).Append("`n")
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($descriptor.ToString())
        return ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
}

function Write-ExactJson([string]$Path, [object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllBytes($Path, [Text.UTF8Encoding]::new($false).GetBytes($json + "`n"))
}

function Invoke-Boot2Build([string]$Output, [string]$Mirror, [string]$Build, [string]$NuGet) {
    $script = Join-Path $RepoRoot 'tools\boot1\Build-Boot2Candidate.ps1'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    & $pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $script `
        -RepoRoot $RepoRoot -ReviewRoot (Join-Path $Output 'boot2-review') `
        -MirrorRoot $Mirror -BuildRoot $Build -NuGetPackages $NuGet `
        -Version $Version -MirrorPort 18089
    if ($LASTEXITCODE -ne 0) { throw "BOOT-2 package/build stage failed ($LASTEXITCODE)." }
}

function Get-GitHead {
    $head = (& git -C $RepoRoot rev-parse HEAD 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') { throw 'Unable to record the repository commit for release evidence.' }
    return $head
}

function Get-ComponentFiles([string]$Stage) {
    return @(Get-ChildItem -LiteralPath $Stage -Recurse -File | ForEach-Object {
        $relative = Convert-ToUnixRelative ([IO.Path]::GetRelativePath($Stage, $_.FullName))
        [ordered]@{ path=$relative; size=[uint64]$_.Length; sha256=(Get-Sha256 $_.FullName) }
    } | Sort-Object { $_['path'] })
}

$RepoRoot = (Resolve-Path $RepoRoot).Path
$OutputRoot = Assert-DProject2Path $OutputRoot 'OutputRoot'
if ($Version -notmatch '^[A-Za-z0-9._-]+$') { throw "Invalid release version: $Version" }
Assert-Https $ManifestBaseUrl 'ManifestBaseUrl'
$ManifestBaseUrl = $ManifestBaseUrl.TrimEnd('/')
foreach ($mirror in $MirrorBaseUrls) { Assert-Https $mirror 'MirrorBaseUrl' }
$MirrorBaseUrls = @($MirrorBaseUrls | ForEach-Object { $_.TrimEnd('/') })

New-CleanDirectory $OutputRoot
$bundleRoot = Join-Path $OutputRoot 'bundle'
$componentRoot = Join-Path $bundleRoot 'components'
New-Item -ItemType Directory -Force -Path $componentRoot | Out-Null

$nugetRoot = Join-Path $OutputRoot 'boot2-nuget'
if (-not $SkipBoot2Build) {
    $Boot2MirrorRoot = Join-Path $OutputRoot 'boot2-mirror'
    $Boot2BuildRoot = Join-Path $OutputRoot 'boot2-build'
    Invoke-Boot2Build $OutputRoot $Boot2MirrorRoot $Boot2BuildRoot $nugetRoot
} else {
    $Boot2MirrorRoot = Assert-DProject2Path $Boot2MirrorRoot 'Boot2MirrorRoot'
    $Boot2BuildRoot = Assert-DProject2Path $Boot2BuildRoot 'Boot2BuildRoot'
}

$componentIds = @('facm-app-win-x64', 'facm-dotnet-runtime-win-x64', 'facm-windows-runtime-win-x64')
$appRecords = [System.Collections.Generic.List[object]]::new()
$indexRecords = [System.Collections.Generic.List[object]]::new()
$ownershipRecords = [System.Collections.Generic.List[object]]::new()
$allOwnedPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($componentId in $componentIds) {
    $stage = Join-Path $Boot2BuildRoot "component-stages\$componentId"
    $package = Join-Path $Boot2MirrorRoot "components\$componentId\$Version\$componentId-$Version.cab"
    if (-not (Test-Path -LiteralPath $stage -PathType Container)) { throw "BOOT-2 component stage missing: $componentId" }
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "BOOT-2 CAB package missing: $package" }

    $files = @(Get-ComponentFiles $stage)
    if ($files.Count -eq 0) { throw "Component stage is empty: $componentId" }
    foreach ($file in $files) {
        if (-not $allOwnedPaths.Add([string]$file['path'])) { throw "Overlapping component ownership path: $($file['path'])" }
    }
    $installedSize = [uint64](($files | ForEach-Object { [uint64]$_.size } | Measure-Object -Sum).Sum)
    $packageInfo = Get-Item -LiteralPath $package
    $packageSha = Get-Sha256 $package
    $contentDigest = Get-DirectoryDigest $stage
    $packageRelative = "components/$componentId/$Version/$componentId-$Version.cab"
    $manifestRelative = "components/$componentId/$Version/component.manifest.json"
    $componentUrl = "$ManifestBaseUrl/$manifestRelative"
    $packageUrl = "$ManifestBaseUrl/$packageRelative"
    $mirrors = @($MirrorBaseUrls | ForEach-Object { "$_/$packageRelative" })
    $dependencies = if ($componentId -eq 'facm-app-win-x64') {
        @('facm-dotnet-runtime-win-x64', 'facm-windows-runtime-win-x64')
    } else { @() }
    $entryPoint = if ($componentId -eq 'facm-app-win-x64') { 'FACM.App.exe' } else { '' }
    $component = [ordered]@{
        schemaVersion=3; componentId=$componentId; version=$Version; architecture='win-x64'
        keyId='facm-production-r1'; required=$true; packageSize=[uint64]$packageInfo.Length
        installedSize=$installedSize; sha256=$packageSha; contentDigest=$contentDigest
        fileCount=[uint64]$files.Count; packageFormat='cab'; entryPoint=$entryPoint
        primaryUrl=$packageUrl; mirrors=$mirrors; dependencies=$dependencies
    }

    $componentDirectory = Join-Path $bundleRoot ($manifestRelative -replace '/', '\' | Split-Path -Parent)
    New-Item -ItemType Directory -Force -Path $componentDirectory | Out-Null
    Copy-Item -LiteralPath $package -Destination (Join-Path $bundleRoot ($packageRelative -replace '/', '\')) -Force
    $componentManifestPath = Join-Path $bundleRoot ($manifestRelative -replace '/', '\')
    Write-ExactJson $componentManifestPath $component
    $componentManifestSha = Get-Sha256 $componentManifestPath

    $appRecord = [ordered]@{}
    foreach ($property in $component.GetEnumerator()) { $appRecord[$property.Key] = $property.Value }
    $appRecord['componentManifestUrl'] = $componentUrl
    $appRecord['componentManifestSha256'] = $componentManifestSha
    [void]$appRecords.Add($appRecord)

    [void]$indexRecords.Add([ordered]@{
        componentId=$componentId; version=$Version; manifestPath=$manifestRelative
        manifestSha256=$componentManifestSha; manifestBytes=[uint64](Get-Item -LiteralPath $componentManifestPath).Length
        signaturePath="$manifestRelative.sig"; packagePath=$packageRelative; packageSha256=$packageSha
        packageBytes=[uint64]$packageInfo.Length; installedSize=$installedSize; fileCount=[uint64]$files.Count
        contentDigest=$contentDigest
    })
    [void]$ownershipRecords.Add([ordered]@{
        componentId=$componentId; fileCount=[uint64]$files.Count; installedSize=$installedSize; files=$files
    })
}

$application = [ordered]@{
    schemaVersion=3; applicationId='FACM'; applicationVersion=$Version; architecture='win-x64'
    trustMode='production'; keyId='facm-production-r1'; components=$appRecords
}
$applicationPath = Join-Path $bundleRoot 'manifest.json'
Write-ExactJson $applicationPath $application
$applicationRelative = 'manifest.json'
$applicationSha = Get-Sha256 $applicationPath

$sourceCommit = Get-GitHead
$ownership = [ordered]@{
    schemaVersion=1; releaseVersion=$Version; sourceCommit=$sourceCommit; architecture='win-x64'
    componentOwnership=$ownershipRecords
}
Write-ExactJson (Join-Path $bundleRoot 'ownership-report.json') $ownership

$index = [ordered]@{
    schemaVersion=1; releaseVersion=$Version; architecture='win-x64'; trustMode='production'
    keyId='facm-production-r1'; sourceCommit=$sourceCommit; packageFormat='cab'
    defaultComposition=$componentIds; application=[ordered]@{
        manifestPath=$applicationRelative; manifestSha256=$applicationSha
        manifestBytes=[uint64](Get-Item -LiteralPath $applicationPath).Length; signaturePath='manifest.json.sig'
    }
    components=$indexRecords; signing=[ordered]@{
        algorithm='RSA-2048-PKCS1-SHA256'; signatureEncoding='base64'
        requestPath='../signing-request.json'; signaturesRequired=[uint64]($componentIds.Count + 1)
        status='external-signer-required'
    }
}
$indexPath = Join-Path $bundleRoot 'release-index.json'
Write-ExactJson $indexPath $index

$requests = [System.Collections.Generic.List[object]]::new()
[void]$requests.Add([ordered]@{
    logicalName='application-manifest'; keyId='facm-production-r1'; algorithm='RSA-2048-PKCS1-SHA256'
    payloadPath='manifest.json'; payloadSha256=$applicationSha
    payloadBytes=[uint64](Get-Item -LiteralPath $applicationPath).Length; signaturePath='manifest.json.sig'
})
foreach ($record in $indexRecords) {
    [void]$requests.Add([ordered]@{
        logicalName=("component-manifest/{0}" -f $record['componentId']); keyId='facm-production-r1'
        algorithm='RSA-2048-PKCS1-SHA256'; payloadPath=$record['manifestPath']
        payloadSha256=$record['manifestSha256']; payloadBytes=$record['manifestBytes']
        signaturePath=$record['signaturePath']
    })
}
$signingRequest = [ordered]@{
    schemaVersion=1; requestStatus='unsigned-external-signer-required'; releaseVersion=$Version
    architecture='win-x64'; keyId='facm-production-r1'; algorithm='RSA-2048-PKCS1-SHA256'
    signatureEncoding='base64'; bundlePath='bundle'; releaseIndexPath='release-index.json'
    releaseIndexSha256=(Get-Sha256 $indexPath); releaseIndexBytes=[uint64](Get-Item -LiteralPath $indexPath).Length
    requests=$requests
}
$requestPath = Join-Path $OutputRoot 'signing-request.json'
Write-ExactJson $requestPath $signingRequest

Write-Host "BOOT3-B release bundle prepared: $bundleRoot"
Write-Host "BOOT3-B signing request: $requestPath"
Write-Host "BOOT3-B source commit: $sourceCommit"
Write-Host "BOOT3-B component count: $($componentIds.Count)"
Write-Host 'BOOT3-B output is unsigned until an external signer response is applied.'
