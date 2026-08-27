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
    if (-not (Test-Path $path)) { Fail "Gate 12 required file missing: $RelativePath" }
    return Get-Content $path -Raw
}

$matrixPath = Join-Path $Root 'evidence/facm4-release-evidence.json'
if (-not (Test-Path $matrixPath)) { Fail 'Release evidence matrix is missing.' }
try {
    $matrix = Get-Content $matrixPath -Raw | ConvertFrom-Json -Depth 20
} catch {
    Fail "Release evidence matrix is invalid JSON: $($_.Exception.Message)"
}

if ($matrix.schemaVersion -ne 1) { Fail "Unsupported release evidence schema: $($matrix.schemaVersion)" }
if ([string]::IsNullOrWhiteSpace([string]$matrix.candidate.headSha) -or [string]$matrix.candidate.headSha -notmatch '^[0-9a-fA-F]{40}$') {
    Fail 'Candidate headSha must be a full 40-character Git SHA.'
}
if ([long]$matrix.candidate.artifactId -le 0) { Fail 'Candidate artifactId must be positive.' }
if ([long]$matrix.candidate.artifactSizeBytes -le 0) { Fail 'Candidate artifactSizeBytes must be positive.' }
if ([string]$matrix.candidate.artifactDigest -notmatch '^sha256:[0-9a-fA-F]{64}$') {
    Fail 'Candidate artifactDigest must be sha256:<64 hex>.'
}

$items = @($matrix.items)
if ($items.Count -eq 0) { Fail 'Release evidence matrix must contain items.' }
$allowedStatuses = @('Passed', 'Blocked', 'NotRun', 'Failed')
$ids = @{}
foreach ($item in $items) {
    $id = [string]$item.id
    if ([string]::IsNullOrWhiteSpace($id)) { Fail 'Release evidence item id is required.' }
    if ($ids.ContainsKey($id)) { Fail "Duplicate release evidence id: $id" }
    $ids[$id] = $item
    if ([string]::IsNullOrWhiteSpace([string]$item.category)) { Fail "Evidence category is missing: $id" }
    if ($allowedStatuses -notcontains [string]$item.status) { Fail "Invalid evidence status for ${id}: $($item.status)" }
    if ([string]$item.status -eq 'Passed' -and [string]::IsNullOrWhiteSpace([string]$item.evidence)) {
        Fail "Passed evidence must cite proof: $id"
    }
    if ([bool]$item.requiredForRelease -and [string]$item.status -ne 'Passed' -and [string]::IsNullOrWhiteSpace([string]$item.notes)) {
        Fail "Required non-passed evidence must explain the blocker: $id"
    }
}

$mandatoryIds = @(
    'engineering.facm4-foundation',
    'engineering.legacy-build',
    'engineering.ui-text',
    'performance.budget-contract',
    'performance.gameflow-cadence',
    'architecture.runtime-ownership',
    'recovery.monotonic-flags',
    'recovery.settings-lkg',
    'diagnostics.redaction',
    'deployment.single-file',
    'gate12.latest-head-ci',
    'compat.non-admin-uac-cancel',
    'security.defender-smartscreen',
    'compat.windows-10-1809',
    'compat.windows-10-22h2',
    'compat.windows-11',
    'display.real-mixed-dpi-multimonitor',
    'accessibility.real-machine',
    'migration.settings-3.5.15-to-4.0',
    'update.interrupted-replacement-rollback',
    'release.final-signature-package'
)
foreach ($id in $mandatoryIds) {
    if (-not $ids.ContainsKey($id)) { Fail "Mandatory release evidence item missing: $id" }
    if (-not [bool]$ids[$id].requiredForRelease) { Fail "Mandatory release evidence must be requiredForRelease: $id" }
}

$app = Read-Required 'src/FACM.App/App.xaml.cs'
$owners = @{
    'WindowsLeagueTransportSessionSource' = 'new\s+WindowsLeagueTransportSessionSource\s*\('
    'LeagueHttpGateway' = 'new\s+LeagueHttpGateway\s*\('
    'LeagueGameflowMonitor' = 'new\s+LeagueGameflowMonitor\s*\('
    'PerformanceBudgetProvider' = 'new\s+PerformanceBudgetProvider\s*\('
    'ProductStateStore' = 'new\s+ProductStateStore\s*\('
}
foreach ($name in $owners.Keys) {
    $count = ([regex]::Matches($app, $owners[$name])).Count
    if ($count -ne 1) { Fail "Process-wide runtime owner must be constructed exactly once: $name actual=$count" }
}

$performance = Read-Required 'src/FACM.Core/Performance/PerformanceBudget.cs'
foreach ($budget in @('Desktop', 'Client', 'Queueing', 'ChampSelect', 'InGame', 'Background')) {
    if ($performance -notmatch ('PerformanceBudget\s+' + $budget + '\s*=')) { Fail "Performance budget disappeared: $budget" }
}
$gameflow = Read-Required 'src/FACM.Core/League/LeagueGameflow.cs'
foreach ($seconds in @('FromSeconds\(2\)', 'FromSeconds\(3\)', 'FromSeconds\(10\)', 'FromSeconds\(5\)')) {
    if ($gameflow -notmatch $seconds) { Fail "Gameflow cadence contract missing: $seconds" }
}

$program = Read-Required 'src/FACM.FoundationSmoke/Program.cs'
$smoke = Read-Required 'src/FACM.FoundationSmoke/Gate12Smoke.cs'
if ($program -notmatch 'Gate12Smoke\.RunAsync') { Fail 'Gate12Smoke is not wired into cumulative FoundationSmoke.' }
foreach ($token in @('PerformancePolicy.Desktop', 'PerformancePolicy.Client', 'PerformancePolicy.Queueing', 'PerformancePolicy.ChampSelect', 'PerformancePolicy.InGame', 'PerformancePolicy.Background', 'ReleaseEvidenceEvaluator.Evaluate')) {
    if ($smoke -notmatch [regex]::Escape($token)) { Fail "Gate12Smoke regression coverage missing: $token" }
}

$required = @($items | Where-Object { [bool]$_.requiredForRelease })
$blocking = @($required | Where-Object { [string]$_.status -ne 'Passed' })
$releaseReady = $blocking.Count -eq 0
Write-Host "Release evidence required=$($required.Count) passed=$($required.Count - $blocking.Count) blocking=$($blocking.Count)"
if ($releaseReady) {
    Write-Host 'FACM 4.0 release evidence evaluator: RELEASE READY'
} else {
    Write-Host 'FACM 4.0 release evidence evaluator: RELEASE BLOCKED'
    Write-Host ('Blocking ids: ' + (($blocking | ForEach-Object { $_.id }) -join ', '))
}
Write-Host 'FACM 4.0 release evidence contract: SUCCESS'
