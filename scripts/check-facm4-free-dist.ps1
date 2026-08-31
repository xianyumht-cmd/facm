[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require-File([string]$RelativePath) {
    $path = Join-Path $RepoRoot ($RelativePath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required FREE-DIST file: $RelativePath" }
    return Get-Content -Raw -LiteralPath $path
}

$bootstrapper = Require-File 'src/FACM.Bootstrapper/main.cpp'
$prep = Require-File 'tools/release/Prepare-FacmFreeDistCandidate.ps1'
$test = Require-File 'tools/release/Test-FacmFreeDistProxyTransport.ps1'
$docs = Require-File 'docs/FREE-DIST-1.md'

foreach ($marker in @('ghfast.top', 'gh-proxy.com', 'gh.llkk.cc', 'github-direct', 'release-assets.githubusercontent.com', 'objects.githubusercontent.com', 'WINHTTP_QUERY_CONTENT_RANGE')) {
    if ($bootstrapper -notmatch [regex]::Escape($marker)) { throw "Bootstrapper is missing FREE-DIST marker: $marker" }
}
foreach ($marker in @('canonical', 'manifestMirrors', 'production-r1', 'free-dist-release')) {
    if ($prep -notmatch [regex]::Escape($marker)) { throw "Preparation tool is missing FREE-DIST marker: $marker" }
}
foreach ($marker in @('CanonicalGithubUrlsAndProxySeparation', 'LiveGithubTransportProbe', 'UnsafeTransportUrlRejected', 'Boot3CHttpsRegressionEvidence')) {
    if ($test -notmatch [regex]::Escape($marker)) { throw "FREE-DIST test is missing assertion: $marker" }
}
foreach ($marker in @('canonical GitHub Release URLs', 'Resume and verification behavior', 'direct GitHub', 'not an SLA')) {
    if ($docs -notmatch [regex]::Escape($marker)) { throw "FREE-DIST documentation is missing section/evidence: $marker" }
}

if ($bootstrapper -match '(?i)NODE_TLS_REJECT_UNAUTHORIZED|curl\s+-k|verify\s*=?\s*false') { throw 'FREE-DIST code contains an insecure TLS bypass.' }
if ($prep -match '-----BEGIN (RSA )?PRIVATE KEY-----|PRIVATE KEY-----') { throw 'Preparation tool contains private-key material.' }
if ($docs -match '(?i)password|BEGIN .*PRIVATE KEY|token=|sig=') { throw 'FREE-DIST documentation contains secret-like material.' }

foreach ($relative in @('tools/release/Prepare-FacmFreeDistCandidate.ps1', 'tools/release/Test-FacmFreeDistProxyTransport.ps1', 'scripts/check-facm4-free-dist.ps1')) {
    $path = Join-Path $RepoRoot ($relative -replace '/', '\')
    $tokens = $null; $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) { throw "PowerShell syntax invalid: $relative" }
}

Write-Host 'FACM FREE-DIST-1 static contract gate: PASS'
