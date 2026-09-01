[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$OutputRoot = 'D:\project2\facm-free-dist-release-20260831',
    [string]$LauncherRoot = 'D:\project2\facm4-free-dist-review-20260831',
    [string]$Version = '4.0.0-free-dist-1',
    [string]$ReleaseTag = 'v4.0.0-free-dist-1',
    [string]$Bootstrapper = 'D:\project2\facm-boot3c-native-build-20260831\FACM.exe',
    [string]$Boot2MirrorSource = 'D:\project2\facm-boot3c-boot3b-regression-20260831\release-a\boot2-mirror',
    [string]$Boot2BuildRoot = 'D:\project2\facm-boot3c-boot3b-regression-20260831\release-a\boot2-build',
    [string]$LocalValidationKeyPath = 'D:\project2\facm-boot3a-signing\production-r1\production-r1.pk8.pem'
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

function Remove-Scope([string]$Path, [string]$Label) {
    $full = Assert-DProject2Path $Path $Label
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

function New-CleanDirectory([string]$Path, [string]$Label) {
    Remove-Scope $Path $Label
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Write-Utf8NoBom([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Write-ExactJson([string]$Path, [object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 30
    Write-Utf8NoBom $Path ($json + "`n")
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Sha256Bytes([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-RelativeUnix([string]$Root, [string]$Path) {
    return ([IO.Path]::GetRelativePath($Root, $Path)).Replace('\', '/')
}

function Get-GitHead([string]$Root) {
    $head = (& git -C $Root rev-parse HEAD 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') { throw 'Unable to record the repository commit.' }
    return $head
}

function Get-SafeRelativePath([string]$Relative) {
    $normalized = $Relative.Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or [IO.Path]::IsPathRooted($normalized) -or $normalized.Split('/') -contains '..') {
        throw "Unsafe relative path: $Relative"
    }
    return $normalized.Replace('/', '\')
}

function Get-ReleaseAssetName([string]$Relative) {
    $normalized = $Relative.Replace('\', '/')
    if ($normalized -eq 'manifest.json' -or $normalized -eq 'manifest.json.sig' -or
        $normalized -eq 'release-index.json' -or $normalized -eq 'ownership-report.json') {
        return $normalized
    }
    if ($normalized -match '^components/([^/]+)/[^/]+/component\.manifest\.json$') {
        return "$($Matches[1])-component-manifest.json"
    }
    if ($normalized -match '^components/([^/]+)/[^/]+/component\.manifest\.json\.sig$') {
        return "$($Matches[1])-component-manifest.json.sig"
    }
    if ($normalized -match '^components/([^/]+)/[^/]+/([^/]+\.cab)$') {
        return $Matches[2]
    }
    throw "No stable GitHub Release asset name for: $Relative"
}

function Write-ReleaseJson([string]$Path, [object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 30
    Write-Utf8NoBom $Path ($json + "`n")
}

function Flatten-ReleaseBundle([string]$BundleRoot, [string]$ReleaseBase, [string]$RequestPath) {
    $flatRoot = Join-Path (Split-Path -Parent $BundleRoot) 'bundle-flat'
    New-CleanDirectory $flatRoot 'FlatBundleRoot'
    foreach ($file in @(Get-ChildItem -LiteralPath $BundleRoot -File)) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $flatRoot $file.Name) -Force
    }

    $application = Get-Content -Raw -LiteralPath (Join-Path $BundleRoot 'manifest.json') | ConvertFrom-Json
    $index = Get-Content -Raw -LiteralPath (Join-Path $BundleRoot 'release-index.json') | ConvertFrom-Json
    foreach ($appComponent in @($application.components)) {
        $id = [string]$appComponent.componentId
        $version = [string]$appComponent.version
        $sourceDirectory = Join-Path $BundleRoot "components\$id\$version"
        $sourceManifest = Join-Path $sourceDirectory 'component.manifest.json'
        $sourcePackage = Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cab' -File | Select-Object -First 1
        if (-not (Test-Path -LiteralPath $sourceManifest -PathType Leaf) -or $null -eq $sourcePackage) {
            throw "Component artifact missing while flattening: $id"
        }
        $manifestName = Get-ReleaseAssetName "components/$id/$version/component.manifest.json"
        $packageName = Get-ReleaseAssetName "components/$id/$version/$($sourcePackage.Name)"
        $component = Get-Content -Raw -LiteralPath $sourceManifest | ConvertFrom-Json
        $appComponent.primaryUrl = "$ReleaseBase/$packageName"
        $appComponent.mirrors = @()
        $appComponent.componentManifestUrl = "$ReleaseBase/$manifestName"
        $appComponent.componentManifestMirrors = @()
        $component.primaryUrl = $appComponent.primaryUrl
        $component.mirrors = @()
        $component.componentManifestMirrors = @()
        Write-ReleaseJson (Join-Path $flatRoot $manifestName) $component
        Copy-Item -LiteralPath $sourcePackage.FullName -Destination (Join-Path $flatRoot $packageName) -Force
        $appComponent.componentManifestSha256 = Get-Sha256 (Join-Path $flatRoot $manifestName)

        $indexComponent = @($index.components | Where-Object { [string]$_.componentId -ceq $id }) | Select-Object -First 1
        if ($null -eq $indexComponent) { throw "Release index component missing while flattening: $id" }
        $indexComponent.manifestPath = $manifestName
        $indexComponent.signaturePath = "$manifestName.sig"
        $indexComponent.packagePath = $packageName
        $indexComponent.manifestSha256 = Get-Sha256 (Join-Path $flatRoot $manifestName)
        $indexComponent.manifestBytes = (Get-Item -LiteralPath (Join-Path $flatRoot $manifestName)).Length
    }
    $application.components = @($application.components)
    Write-ReleaseJson (Join-Path $flatRoot 'manifest.json') $application
    $index.application.manifestPath = 'manifest.json'
    $index.application.signaturePath = 'manifest.json.sig'
    $index.application.manifestSha256 = Get-Sha256 (Join-Path $flatRoot 'manifest.json')
    $index.application.manifestBytes = (Get-Item -LiteralPath (Join-Path $flatRoot 'manifest.json')).Length
    Write-ReleaseJson (Join-Path $flatRoot 'release-index.json') $index

    $request = Get-Content -Raw -LiteralPath $RequestPath | ConvertFrom-Json
    $request.releaseIndexSha256 = Get-Sha256 (Join-Path $flatRoot 'release-index.json')
    $request.releaseIndexBytes = (Get-Item -LiteralPath (Join-Path $flatRoot 'release-index.json')).Length
    foreach ($item in @($request.requests)) {
        $item.payloadPath = Get-ReleaseAssetName ([string]$item.payloadPath)
        $item.signaturePath = "$($item.payloadPath).sig"
        $payloadPath = Join-Path $flatRoot $item.payloadPath
        $item.payloadSha256 = Get-Sha256 $payloadPath
        $item.payloadBytes = (Get-Item -LiteralPath $payloadPath).Length
    }
    Write-ReleaseJson $RequestPath $request

    Remove-Scope $BundleRoot 'NestedBundleRoot'
    Move-Item -LiteralPath $flatRoot -Destination $BundleRoot
}

function Invoke-Tool([string]$Script, [string[]]$Arguments) {
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    & $pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $Script @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Tool failed ($LASTEXITCODE): $Script" }
}

function Open-Rsa([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Local validation key missing: $Path" }
    $rsa = [Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem([IO.File]::ReadAllText($Path))
    return $rsa
}

function Create-SignerResponses([string]$RequestPath, [string]$BundleRoot, [string]$ResponseRoot, [Security.Cryptography.RSA]$Rsa) {
    $request = Get-Content -Raw -LiteralPath $RequestPath | ConvertFrom-Json
    New-Item -ItemType Directory -Force -Path $ResponseRoot | Out-Null
    foreach ($item in @($request.requests)) {
        $payloadRelative = Get-SafeRelativePath ([string]$item.payloadPath)
        $payloadPath = Join-Path $BundleRoot $payloadRelative
        $bytes = [IO.File]::ReadAllBytes($payloadPath)
        $digest = Get-Sha256Bytes $bytes
        if ($digest -ne [string]$item.payloadSha256.ToLowerInvariant() -or $bytes.Length -ne [int64]$item.payloadBytes) {
            throw "Signing request payload changed: $($item.payloadPath)"
        }
        $signature = $Rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $responsePath = Join-Path $ResponseRoot (Get-SafeRelativePath ([string]$item.signaturePath))
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $responsePath) | Out-Null
        Write-Utf8NoBom $responsePath ([Convert]::ToBase64String($signature) + "`n")
    }
}

$RepoRoot = (Resolve-Path $RepoRoot).Path
$OutputRoot = Assert-DProject2Path $OutputRoot 'OutputRoot'
$LauncherRoot = Assert-DProject2Path $LauncherRoot 'LauncherRoot'
$Boot2MirrorSource = Assert-DProject2Path $Boot2MirrorSource 'Boot2MirrorSource'
$Boot2BuildRoot = Assert-DProject2Path $Boot2BuildRoot 'Boot2BuildRoot'
$Bootstrapper = Assert-DProject2Path $Bootstrapper 'Bootstrapper'
$LocalValidationKeyPath = Assert-DProject2Path $LocalValidationKeyPath 'LocalValidationKeyPath'
if ($Version -notmatch '^[A-Za-z0-9._-]+$' -or $ReleaseTag -notmatch '^[A-Za-z0-9._-]+$') { throw 'Version and ReleaseTag contain unsafe characters.' }
if (-not (Test-Path -LiteralPath $Bootstrapper -PathType Leaf)) { throw "Bootstrapper missing: $Bootstrapper" }
if (-not (Test-Path -LiteralPath $Boot2MirrorSource -PathType Container)) { throw "BOOT-2 mirror missing: $Boot2MirrorSource" }
if (-not (Test-Path -LiteralPath $Boot2BuildRoot -PathType Container)) { throw "BOOT-2 build root missing: $Boot2BuildRoot" }

$stagedMirror = 'D:\project2\facm-free-dist-boot2-mirror-20260831'
New-CleanDirectory $OutputRoot 'OutputRoot'
New-CleanDirectory $LauncherRoot 'LauncherRoot'
New-CleanDirectory $stagedMirror 'StagedMirror'

$componentIds = @('facm-app-win-x64', 'facm-dotnet-runtime-win-x64', 'facm-windows-runtime-win-x64')
$previousVersion = '4.0.0-boot3b'
foreach ($componentId in $componentIds) {
    $sourceDirectory = Join-Path $Boot2MirrorSource "components\$componentId\$previousVersion"
    $sourcePackage = Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cab' -File | Select-Object -First 1
    if (-not $sourcePackage) { throw "BOOT-2 CAB package missing: $componentId" }
    $targetDirectory = Join-Path $stagedMirror "components\$componentId\$Version"
    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    Copy-Item -LiteralPath $sourcePackage.FullName -Destination (Join-Path $targetDirectory "$componentId-$Version.cab") -Force
}

$githubBase = "https://github.com/xianyumht-cmd/facm/releases/download/$ReleaseTag"
$builder = Join-Path $RepoRoot 'tools\release\Build-FacmBoot3BRelease.ps1'
Invoke-Tool $builder @(
    '-RepoRoot', $RepoRoot,
    '-OutputRoot', $OutputRoot,
    '-Version', $Version,
    '-ManifestBaseUrl', $githubBase,
    '-SkipBoot2Build',
    '-Boot2MirrorRoot', $stagedMirror,
    '-Boot2BuildRoot', $Boot2BuildRoot
)

$bundleRoot = Join-Path $OutputRoot 'bundle'
$requestPath = Join-Path $OutputRoot 'signing-request.json'
$releaseBase = "https://github.com/xianyumht-cmd/facm/releases/download/$ReleaseTag"
Flatten-ReleaseBundle $bundleRoot $releaseBase $requestPath
$responseRoot = Join-Path $OutputRoot 'signer-responses-local-validation-only'
$rsa = Open-Rsa $LocalValidationKeyPath
try {
    Create-SignerResponses $requestPath $bundleRoot $responseRoot $rsa
} finally {
    $rsa.Dispose()
}
$apply = Join-Path $RepoRoot 'tools\release\Apply-FacmSigningResponses.ps1'
Invoke-Tool $apply @('-RequestPath', $requestPath, '-SignatureRoot', $responseRoot, '-BundleRoot', $bundleRoot)

$bootstrap = [ordered]@{
    schemaVersion = 1
    manifestUrl = "$githubBase/manifest.json"
    manifestMirrors = @()
    allowUnsignedLocal = $false
    allowInsecureLocal = $false
}
Write-ExactJson (Join-Path $LauncherRoot 'bootstrap.json') $bootstrap
Copy-Item -LiteralPath $Bootstrapper -Destination (Join-Path $LauncherRoot 'FACM.exe') -Force

$bundleFiles = @(Get-ChildItem -LiteralPath $bundleRoot -Recurse -File | ForEach-Object {
    [ordered]@{ path=(Get-RelativeUnix $bundleRoot $_.FullName); size=[uint64]$_.Length; sha256=(Get-Sha256 $_.FullName) }
}) | Sort-Object { $_['path'] }
$bundleTotalBytes = [uint64](($bundleFiles | ForEach-Object { [uint64]$_['size'] } | Measure-Object -Sum).Sum)
$packageFiles = @($bundleFiles | Where-Object { $_['path'] -like '*.cab' })
$packageTotalBytes = [uint64](($packageFiles | ForEach-Object { [uint64]$_['size'] } | Measure-Object -Sum).Sum)
$launcherFiles = @(Get-ChildItem -LiteralPath $LauncherRoot -File | ForEach-Object {
    [ordered]@{ path=$_.Name; size=[uint64]$_.Length; sha256=(Get-Sha256 $_.FullName) }
}) | Sort-Object { $_['path'] }
$launcherTotalBytes = [uint64](($launcherFiles | ForEach-Object { [uint64]$_['size'] } | Measure-Object -Sum).Sum)
$evidence = [ordered]@{
    schemaVersion = 1
    task = 'FACM 4.0 FREE-DIST-1'
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    sourceCommit = Get-GitHead $RepoRoot
    productionVersionUnchanged = '3.5.15'
    releasePublicationPerformed = $false
    canonicalOrigin = [ordered]@{
        owner = 'xianyumht-cmd'; repository = 'facm'; releaseTag = $ReleaseTag
        baseUrl = $githubBase; manifestUrl = "$githubBase/manifest.json"; manifestMirrors = @()
    }
    selectedProxyCandidates = @('ghfast.top', 'gh-proxy.com', 'gh.llkk.cc')
    transportOrder = @('ghfast.top', 'gh-proxy.com', 'gh.llkk.cc', 'github-direct')
    bundle = [ordered]@{ root=$bundleRoot; totalBytes=$bundleTotalBytes; packageBytes=$packageTotalBytes; files=$bundleFiles }
    launcherOnly = [ordered]@{ root=$LauncherRoot; totalBytes=$launcherTotalBytes; files=$launcherFiles }
    signing = [ordered]@{ keyId='facm-production-r1'; method='local validation key outside repository'; privateKeyCopied=$false }
}
Write-ExactJson (Join-Path $OutputRoot 'free-dist-evidence.json') $evidence
Write-Host "FREE-DIST candidate bundle: $bundleRoot"
Write-Host "FREE-DIST launcher-only review: $LauncherRoot"
Write-Host "FREE-DIST evidence: $(Join-Path $OutputRoot 'free-dist-evidence.json')"
