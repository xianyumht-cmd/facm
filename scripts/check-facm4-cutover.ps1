param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Read-Required([string]$RelativePath) {
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path $path)) { Fail "Gate 13 required file missing: $RelativePath" }
    return Get-Content $path -Raw
}

$matrixPath = Join-Path $Root 'evidence/facm4-release-evidence.json'
if (-not (Test-Path $matrixPath)) { Fail 'Release evidence matrix is missing.' }
try {
    $matrix = Get-Content $matrixPath -Raw | ConvertFrom-Json -Depth 20
} catch {
    Fail "Release evidence matrix is invalid JSON: $($_.Exception.Message)"
}

$items = @($matrix.items)
$required = @($items | Where-Object { [bool]$_.requiredForRelease })
$blocking = @($required | Where-Object { [string]$_.status -ne 'Passed' })
$releaseReady = $blocking.Count -eq 0

$gate13 = @($items | Where-Object { [string]$_.id -eq 'gate13.cutover-guard' })
if ($gate13.Count -ne 1) { Fail 'Release evidence must contain exactly one gate13.cutover-guard item.' }
if (-not [bool]$gate13[0].requiredForRelease) { Fail 'gate13.cutover-guard must be requiredForRelease.' }

$guard = Read-Required 'src/FACM.Core/Release/CutoverGuard.cs'
foreach ($token in @(
    'FACM4ProductionCutover',
    'ReleaseEvidenceEvaluator.Evaluate',
    'if (!release.ReleaseReady)',
    'AuthorizationMissing',
    'AuthorizationScopeMismatch',
    'AuthorizationCandidateMismatch',
    'AuthorizationIssuedInFuture',
    'AuthorizationExpired',
    'AuthorizationStale',
    'AuthorizationWindowTooLong',
    'TimeSpan.FromMinutes(30)'
)) {
    if ($guard -notmatch [regex]::Escape($token)) { Fail "Cutover guard contract missing: $token" }
}
$evidenceIndex = $guard.IndexOf('ReleaseEvidenceEvaluator.Evaluate', [StringComparison]::Ordinal)
$blockedIndex = $guard.IndexOf('if (!release.ReleaseReady)', [StringComparison]::Ordinal)
$authorizationIndex = $guard.IndexOf('if (authorization is null)', [StringComparison]::Ordinal)
if ($evidenceIndex -lt 0 -or $blockedIndex -lt 0 -or $authorizationIndex -lt 0 -or
    $evidenceIndex -gt $blockedIndex -or $blockedIndex -gt $authorizationIndex) {
    Fail 'Cutover guard must evaluate and reject blocked release evidence before considering authorization.'
}

$program = Read-Required 'src/FACM.FoundationSmoke/Program.cs'
$smoke = Read-Required 'src/FACM.FoundationSmoke/Gate13Smoke.cs'
if ($program -notmatch 'Gate13Smoke\.RunAsync') { Fail 'Gate13Smoke is not wired into cumulative FoundationSmoke.' }
foreach ($token in @(
    'ReleaseEvidenceBlocked',
    'AuthorizationMissing',
    'AuthorizationNotGranted',
    'AuthorizationScopeMismatch',
    'AuthorizationCandidateMismatch',
    'AuthorizationIssuedInFuture',
    'AuthorizationExpired',
    'AuthorizationStale',
    'AuthorizationWindowTooLong',
    'CutoverDecisionCode.Allowed'
)) {
    if ($smoke -notmatch [regex]::Escape($token)) { Fail "Gate13Smoke coverage missing: $token" }
}

$embeddedAuthorization = Get-ChildItem (Join-Path $Root 'src') -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '[\\/]FACM\.FoundationSmoke[\\/]' } |
    Select-String -Pattern 'new\s+ProductionCutoverAuthorization\s*\(' -AllMatches
if ($embeddedAuthorization) {
    Fail 'Production cutover authorization must not be embedded/persisted in application source.'
}

if (-not $releaseReady) {
    foreach ($legacyPath in @(
        'FACM.sln',
        'src/FACM/FACM.csproj',
        'src/FACM.Updater/FACM.Updater.csproj',
        'src/FACM.ToolBundle/FACM.ToolBundle.csproj'
    )) {
        if (-not (Test-Path (Join-Path $Root $legacyPath))) {
            Fail "Release is blocked, so legacy rollback asset must remain: $legacyPath"
        }
    }

    Push-Location $Root
    try {
        git rev-parse --verify origin/main *> $null
        if ($LASTEXITCODE -ne 0) { Fail 'origin/main is unavailable for production-control diff verification.' }
        $productionChanges = @(git diff --name-only origin/main...HEAD -- online/version.json release/request.json)
        if ($LASTEXITCODE -ne 0) { Fail 'Unable to compare production release controls against origin/main.' }
        if ($productionChanges.Count -gt 0) {
            Fail ('Release is blocked; production release controls changed: ' + ($productionChanges -join ', '))
        }
    } finally {
        Pop-Location
    }

    Write-Host "Gate 13 evidence required=$($required.Count) passed=$($required.Count - $blocking.Count) blocking=$($blocking.Count)"
    Write-Host 'FACM 4.0 CUTOVER BLOCKED: release evidence is incomplete; production controls and legacy rollback baseline remain frozen.'
} else {
    Write-Host "Gate 13 evidence required=$($required.Count) passed=$($required.Count) blocking=0"
    Write-Host 'FACM 4.0 EVIDENCE READY: fresh scoped production/destructive authorization is still required before cutover.'
}

Write-Host 'FACM 4.0 Cutover Guard contract: SUCCESS'
