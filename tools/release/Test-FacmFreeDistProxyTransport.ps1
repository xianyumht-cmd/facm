[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$BundleRoot = 'D:\project2\facm-free-dist-release-20260831\bundle',
    [string]$LauncherRoot = 'D:\project2\facm4-free-dist-review-20260831',
    [string]$Bootstrapper = 'D:\project2\facm-boot3c-native-build-20260831\FACM.exe',
    [string]$ProbeRoot = 'D:\project2\facm-free-dist-probe-20260831',
    [string]$ProbeUrl = 'https://github.com/cli/cli/releases/download/v2.62.0/gh_2.62.0_checksums.txt',
    [string]$HttpsResults = 'D:\project2\facm-boot3c-https-tests-20260831\results.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-FullPath([string]$Path) { return [IO.Path]::GetFullPath($Path) }
function Assert-DProject2Path([string]$Path, [string]$Label) {
    $full = Get-FullPath $Path
    if (-not $full.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase) -or $full -eq 'D:\project2') { throw "$Label must be under D:\project2: $full" }
    return $full
}
function Fail([string]$Message) { throw $Message }
function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { Fail $Message } }
function Get-Sha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-Utf8NoBom([string]$Path, [string]$Text) { [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }

$BundleRoot = Assert-DProject2Path $BundleRoot 'BundleRoot'
$LauncherRoot = Assert-DProject2Path $LauncherRoot 'LauncherRoot'
$Bootstrapper = Assert-DProject2Path $Bootstrapper 'Bootstrapper'
$ProbeRoot = Assert-DProject2Path $ProbeRoot 'ProbeRoot'
$RepoRoot = (Resolve-Path $RepoRoot).Path
Assert-True (Test-Path -LiteralPath $BundleRoot -PathType Container) "Bundle missing: $BundleRoot"
Assert-True (Test-Path -LiteralPath $LauncherRoot -PathType Container) "Launcher missing: $LauncherRoot"
Assert-True (Test-Path -LiteralPath $Bootstrapper -PathType Leaf) "Bootstrapper missing: $Bootstrapper"

$manifest = Get-Content -Raw -LiteralPath (Join-Path $BundleRoot 'manifest.json') | ConvertFrom-Json
$releaseIndex = Get-Content -Raw -LiteralPath (Join-Path $BundleRoot 'release-index.json') | ConvertFrom-Json
$canonicalPattern = '^https://github\.com/xianyumht-cmd/facm/releases/download/[A-Za-z0-9._-]+/[A-Za-z0-9._/-]+$'
$allUrls = [System.Collections.Generic.List[string]]::new()
foreach ($url in @($manifest.manifestMirrors)) { if ($url) { [void]$allUrls.Add([string]$url) } }
foreach ($component in @($manifest.components)) {
    [void]$allUrls.Add([string]$component.primaryUrl)
    foreach ($url in @($component.mirrors)) { if ($url) { [void]$allUrls.Add([string]$url) } }
    [void]$allUrls.Add([string]$component.componentManifestUrl)
    foreach ($url in @($component.componentManifestMirrors)) { if ($url) { [void]$allUrls.Add([string]$url) } }
}
$allUrls = @($allUrls | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
Assert-True ($allUrls.Count -gt 0) 'Signed metadata did not contain any source URLs.'
foreach ($url in $allUrls) {
    Assert-True ($url -match $canonicalPattern) "Non-canonical signed URL: $url"
    Assert-True ($url -notmatch 'ghfast|gh-proxy|gh\.llkk|ghproxy|github\.tk') "Proxy hostname leaked into signed metadata: $url"
}
Write-Host 'CanonicalGithubUrlsAndProxySeparation: PASS'

$assetFiles = @(Get-ChildItem -LiteralPath $BundleRoot -Recurse -File | ForEach-Object {
    [IO.Path]::GetRelativePath($BundleRoot, $_.FullName).Replace('\', '/')
})
Assert-True (@($assetFiles | Where-Object { $_ -match '/' }).Count -eq 0) 'GitHub Release assets must be flat files without directory paths.'
Assert-True (@($assetFiles | Group-Object { [IO.Path]::GetFileName($_) } | Where-Object { $_.Count -gt 1 }).Count -eq 0) 'GitHub Release asset basenames must be unique.'
Assert-True (@($releaseIndex.components | Where-Object { ([string]$_.manifestPath + [string]$_.signaturePath + [string]$_.packagePath) -match '/' }).Count -eq 0) 'Release index contains a nested asset path.'
Write-Host 'ReleaseAssetLayout: PASS'

$signatureFiles = @(Get-ChildItem -LiteralPath $BundleRoot -Recurse -Filter '*.sig' -File)
Assert-True ($signatureFiles.Count -eq 4) "Expected four detached signatures, found $($signatureFiles.Count)."
Write-Host 'SignedTrustSurfacePreserved: PASS'

$bootstrap = Get-Content -Raw -LiteralPath (Join-Path $LauncherRoot 'bootstrap.json') | ConvertFrom-Json
Assert-True ([string]$bootstrap.manifestUrl -match '^https://github\.com/xianyumht-cmd/facm/releases/download/[A-Za-z0-9._-]+/manifest\.json$') 'Launcher manifest URL is not canonical GitHub Release HTTPS.'
Assert-True (@($bootstrap.manifestMirrors).Count -eq 0) 'Launcher unexpectedly contains manifest mirrors.'
Assert-True (-not [bool]$bootstrap.allowUnsignedLocal -and -not [bool]$bootstrap.allowInsecureLocal) 'Launcher trust flags are not production-safe.'
$launcherFiles = @(Get-ChildItem -LiteralPath $LauncherRoot -Recurse -File)
Assert-True (($launcherFiles.Name | Sort-Object) -join ',' -eq 'bootstrap.json,FACM.exe') 'Launcher-only directory contains unexpected files.'
Assert-True (-not (Get-ChildItem -LiteralPath $LauncherRoot -Recurse -Force | Where-Object { $_.FullName -match '\\.facm([\\]|$)' })) 'Launcher-only directory contains runtime state.'
Write-Host 'LauncherOnlyShape: PASS'

if (Test-Path -LiteralPath $ProbeRoot) { Remove-Item -LiteralPath $ProbeRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $ProbeRoot | Out-Null
$probeExe = Join-Path $ProbeRoot 'FACM.exe'
Copy-Item -LiteralPath $Bootstrapper -Destination $probeExe -Force
$probeArguments = @('--no-ui', "--probe-github-transport=$ProbeUrl")
$probeProcess = Start-Process -FilePath $probeExe -ArgumentList $probeArguments -Wait -PassThru
Assert-True ($probeProcess.ExitCode -eq 0) "GitHub transport probe did not find a working transport (exit $($probeProcess.ExitCode))."
$probeLogPath = Join-Path $ProbeRoot '.facm\logs\bootstrapper.jsonl'
Assert-True (Test-Path -LiteralPath $probeLogPath -PathType Leaf) 'Transport probe did not write its diagnostic log.'
$probeEvents = @(Get-Content -LiteralPath $probeLogPath | ForEach-Object { $_ | ConvertFrom-Json })
$probeTransportEvents = @($probeEvents | Where-Object { $_.event -in @('free-dist-transport-probe-pass', 'free-dist-transport-probe-fail') })
$probeIds = @($probeTransportEvents | Select-Object -ExpandProperty detail -Unique)
Assert-True ($probeIds.Count -eq 4) "Expected all four transport candidates to be probed, found $($probeIds -join ',')."
Assert-True (@($probeTransportEvents | Where-Object { $_.event -eq 'free-dist-transport-probe-pass' }).Count -ge 1) 'No GitHub transport candidate passed the live probe.'
Write-Host "LiveGithubTransportProbe: PASS ($($probeIds -join ', '))"

$invalidArguments = @('--no-ui', '--probe-github-transport=https://example.com/not-a-release')
$invalidProcess = Start-Process -FilePath $probeExe -ArgumentList $invalidArguments -Wait -PassThru
Assert-True ($invalidProcess.ExitCode -eq 16) "Invalid transport probe unexpectedly returned $($invalidProcess.ExitCode)."
$probeEvents = @(Get-Content -LiteralPath $probeLogPath | ForEach-Object { $_ | ConvertFrom-Json })
Assert-True (@($probeEvents | Where-Object { $_.event -eq 'free-dist-transport-probe-rejected' }).Count -ge 1) 'Invalid GitHub transport URL was not rejected.'
Write-Host 'UnsafeTransportUrlRejected: PASS'

if (Test-Path -LiteralPath $HttpsResults -PathType Leaf) {
    $https = Get-Content -Raw -LiteralPath $HttpsResults | ConvertFrom-Json
    $httpsScenarios = @($https.scenarios)
    Assert-True ($httpsScenarios.Count -ge 8 -and @($httpsScenarios | Where-Object { $_.status -ne 'PASS' }).Count -eq 0) 'Existing BOOT3-C HTTPS regression evidence has fewer than 8 scenarios or contains a failure.'
    Write-Host 'Boot3CHttpsRegressionEvidence: PASS'
}

$result = [ordered]@{
    schemaVersion = 1
    task = 'FACM 4.0 FREE-DIST-1'
    canonicalUrls = $allUrls.Count
    detachedSignatures = $signatureFiles.Count
    liveProbeUrl = $ProbeUrl
    liveProbeCandidates = $probeIds
    liveProbePassed = @($probeEvents | Where-Object { $_.event -eq 'free-dist-transport-probe-pass' } | Select-Object -ExpandProperty detail -Unique)
    invalidUrlRejected = $true
    boot3cHttpsRegression = if (Test-Path -LiteralPath $HttpsResults -PathType Leaf) { "$($httpsScenarios.Count)/$($httpsScenarios.Count) PASS" } else { 'not-provided' }
}
Write-Utf8NoBom (Join-Path $ProbeRoot 'free-dist-test-results.json') (($result | ConvertTo-Json -Depth 10) + "`n")
Write-Host "FREE-DIST proxy transport and signed-bundle tests: SUCCESS"
