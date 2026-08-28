param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Count-Matches([string]$Text, [string]$Pattern) {
    return @([regex]::Matches($Text, $Pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)).Count
}

$corePath = Join-Path $Root 'src/FACM.Core/League/LeagueBenchQuickPick.cs'
$writeContractPath = Join-Path $Root 'src/FACM.Core/League/LeagueContracts.cs'
$servicePath = Join-Path $Root 'src/FACM.Infrastructure/League/LeagueBenchQuickPickService.cs'
$compositionPath = Join-Path $Root 'src/FACM.App/App.LeagueBenchQuickPick.cs'
$uiPath = Join-Path $Root 'src/FACM.App/MainWindow.LeagueBenchQuickPick.cs'
$runtimeUiPath = Join-Path $Root 'src/FACM.App/MainWindow.LeagueWorkbenchRuntime.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/LeagueBenchQuickPickSmoke.cs'
$smokeProgramPath = Join-Path $Root 'src/FACM.FoundationSmoke/Program.cs'

foreach ($path in @($corePath, $writeContractPath, $servicePath, $compositionPath, $uiPath, $runtimeUiPath, $smokePath, $smokeProgramPath)) {
    if (-not (Test-Path $path)) { Fail "League bench quick-pick contract file missing: $path" }
}

$core = Get-Content $corePath -Raw
$writeContract = Get-Content $writeContractPath -Raw
$service = Get-Content $servicePath -Raw
$composition = Get-Content $compositionPath -Raw
$ui = Get-Content $uiPath -Raw
$runtimeUi = Get-Content $runtimeUiPath -Raw
$smoke = Get-Content $smokePath -Raw
$smokeProgram = Get-Content $smokeProgramPath -Raw

foreach ($required in @(
    'LeagueBenchSwapRoute', 'Legacy', 'TeamBuilder', 'LeagueBenchSwapStatus',
    'LeagueBenchQuickPickState', 'LeagueBenchSwapResult', 'ILeagueBenchQuickPickService',
    'RefreshAsync', 'LoadChampionIconAsync', 'TrySwapAsync', 'LeagueBenchQuickPickPolling'
)) {
    if ($core -notmatch [regex]::Escape($required)) {
        Fail "League bench Core contract is missing: $required"
    }
}
foreach ($forbidden in @('System\.Net\.Http', 'System\.Diagnostics', 'FACM\.Infrastructure', 'FACM\.Platform', 'Microsoft\.UI')) {
    if ($core -match $forbidden) {
        Fail "League bench Core contract leaked implementation/UI detail: $forbidden"
    }
}

foreach ($required in @(
    'SwapBenchChampionLegacy', 'SwapBenchChampionTeamBuilder',
    '/lol-champ-select/v1/session/bench/swap/',
    '/lol-lobby-team-builder/champ-select/v1/session/bench/swap/',
    'command.ResourceId is > 0'
)) {
    if ($writeContract -notmatch [regex]::Escape($required)) {
        Fail "League write allowlist is missing bench safety rule: $required"
    }
}

foreach ($required in @(
    'ChampSelectSessionPath', 'TeamBuilderChampSelectSessionPath', 'ChampionIconPathPrefix',
    'SemaphoreSlim', '_writer.ExecuteAsync', 'SwapBenchChampionTeamBuilder', 'SwapBenchChampionLegacy',
    'TimeSpan.FromMilliseconds(35)', 'TimeSpan.FromMilliseconds(70)', 'TimeSpan.FromMilliseconds(140)',
    'VerificationFailed', 'TargetUnavailable', 'response.StatusCode is 404 or 409',
    'ParseBenchState', 'isLegacyChampSelect', 'benchChampions', 'benchChampionIds',
    'MaxChampionIconBytes'
)) {
    if ($service -notmatch [regex]::Escape($required)) {
        Fail "League bench service is missing migrated 3.5 behavior: $required"
    }
}
if ((Count-Matches $service '_writer\.ExecuteAsync\s*\(') -ne 1) {
    Fail 'League bench service must contain exactly one write call site; verification is read-only.'
}
if ($service -match '/session/bench/swap/') {
    Fail 'League bench service must select Core capabilities instead of constructing write URLs.'
}
foreach ($forbidden in @('HttpClient', 'HttpRequestMessage', 'FACM\.Platform', 'Microsoft\.UI', 'Process\.')) {
    if ($service -match $forbidden) {
        Fail "League bench service crossed its shared-gateway boundary: $forbidden"
    }
}

foreach ($required in @('CreateLeagueBenchQuickPickService', 'new LeagueBenchQuickPickService(gateway, gateway)')) {
    if ($composition -notmatch [regex]::Escape($required)) {
        Fail "App composition is missing shared-gateway bench service: $required"
    }
}

foreach ($required in @(
    'FACM.League.BenchState', 'FACM.League.BenchStatus', 'FACM.League.Bench.',
    'ILeagueBenchQuickPickService', 'CreateLeagueBenchQuickPickService',
    'LeagueBenchQuickPickPolling.ResolveDelay', 'TrySwapAsync',
    'SetLeagueBenchButtonsEnabled(false)', 'RefreshLeagueBenchOnceAsync'
)) {
    if ($ui -notmatch [regex]::Escape($required)) {
        Fail "League bench WinUI surface is missing behavior: $required"
    }
}
foreach ($forbidden in @('ILeagueWriteGateway', 'LeagueHttpGateway', 'HttpClient', 'HttpRequestMessage', 'FACM\.Platform', 'bench/swap/')) {
    if ($ui -match $forbidden) {
        Fail "League bench WinUI crossed the intent boundary: $forbidden"
    }
}
foreach ($required in @('InitializeLeagueBenchQuickPickSurface()', 'DisposeLeagueBenchQuickPickSurface()')) {
    if ($runtimeUi -notmatch [regex]::Escape($required)) {
        Fail "League Workbench lifecycle is missing bench surface hook: $required"
    }
}

foreach ($required in @(
    'ValidateWriteAllowlist', 'ValidatePollingPolicy', 'ValidateLegacyParser',
    'ValidateTeamBuilderFallbackAndSingleWriteAsync', 'ValidateVerificationFailureNeverRetriesWriteAsync',
    'ValidateTargetUnavailableSkipsReadbackAsync', 'gateway.Commands.Count == 1', 'reads == 4'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) {
        Fail "League bench deterministic smoke is missing: $required"
    }
}
if ((Count-Matches $smokeProgram 'LeagueBenchQuickPickSmoke\.RunAsync') -ne 1) {
    Fail 'Foundation smoke must register League bench quick-pick exactly once.'
}

Write-Host 'League bench Core: platform-neutral state/result + legacy polling cadence'
Write-Host 'League bench write boundary: two exact POST capability paths with positive champion id'
Write-Host 'League bench service: shared gateway + Team Builder fallback + one POST + bounded read-back'
Write-Host 'League bench WinUI: visible-only quick-pick controls with busy serialization'
Write-Host 'League bench deterministic smoke: routing / single-write / verification / stale target'
Write-Host 'FACM 4.0 League Bench Quick-Pick contract: SUCCESS'
