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

$loadoutContractPath = Join-Path $Root 'src/FACM.Core/League/LeagueBuildLoadout.cs'
$autoContractPath = Join-Path $Root 'src/FACM.Core/League/LeagueRecommendedAutoApply.cs'
$loadoutServicePath = Join-Path $Root 'src/FACM.Infrastructure/League/LeagueBuildLoadoutService.cs'
$autoServicePath = Join-Path $Root 'src/FACM.Infrastructure/League/LeagueRecommendedAutoApplyService.cs'
$settingsVmPath = Join-Path $Root 'src/FACM.App/ViewModels/LeagueRecommendedAutoApplySettingsViewModel.cs'
$compositionPath = Join-Path $Root 'src/FACM.App/App.LeagueWorkbenchProductization.cs'
$actionsPath = Join-Path $Root 'src/FACM.App/MainWindow.LeagueWorkbenchActions.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/LeagueRecommendedAutoApplySmoke.cs'
$smokeProgramPath = Join-Path $Root 'src/FACM.FoundationSmoke/Program.cs'

foreach ($path in @(
    $loadoutContractPath, $autoContractPath, $loadoutServicePath, $autoServicePath,
    $settingsVmPath, $compositionPath, $actionsPath, $smokePath, $smokeProgramPath
)) {
    if (-not (Test-Path $path)) { Fail "Recommended setup contract file missing: $path" }
}

$loadoutContract = Get-Content $loadoutContractPath -Raw
$autoContract = Get-Content $autoContractPath -Raw
$loadoutService = Get-Content $loadoutServicePath -Raw
$autoService = Get-Content $autoServicePath -Raw
$settingsVm = Get-Content $settingsVmPath -Raw
$composition = Get-Content $compositionPath -Raw
$actions = Get-Content $actionsPath -Raw
$smoke = Get-Content $smokePath -Raw
$smokeProgram = Get-Content $smokeProgramPath -Raw

foreach ($required in @(
    'LeagueBuildLoadoutPlan', 'HasSpells', 'HasRunes', 'SelectedPerkIds',
    'LeagueBuildLoadoutApplyResult', 'AnyApplied', 'ILeagueBuildLoadoutService',
    'PrepareAsync', 'ApplyAsync'
)) {
    if ($loadoutContract -notmatch [regex]::Escape($required)) {
        Fail "Recommended loadout Core contract is missing: $required"
    }
}
foreach ($forbidden in @('FACM\.Infrastructure', 'FACM\.Platform\.Windows', 'HttpClient', '/lol-')) {
    if ($loadoutContract -match $forbidden) {
        Fail "Recommended loadout Core contract leaked implementation detail: $forbidden"
    }
}

foreach ($required in @(
    'ILeagueRecommendedAutoApplyService', 'LeagueRecommendedAutoApplyStatus',
    'Enabled', 'LastStatus', 'StatusChanged', 'Configure'
)) {
    if ($autoContract -notmatch [regex]::Escape($required)) {
        Fail "Recommended auto-apply Core contract is missing: $required"
    }
}
foreach ($forbidden in @('FACM\.Infrastructure', 'FACM\.Platform\.Windows', 'HttpClient', '/lol-')) {
    if ($autoContract -match $forbidden) {
        Fail "Recommended auto-apply Core contract leaked implementation detail: $forbidden"
    }
}

foreach ($required in @(
    'ILeagueWorkbenchDataSource', 'ILeagueReadGateway', 'ILeagueWriteGateway',
    'OwnedRunePagePrefix', 'PrepareAsync', 'ApplyAsync', 'PreserveFlashSlot',
    'champ-select-required', 'champion-changed', 'queue-changed',
    'LeagueWriteCapability.CreatePerkPage', 'LeagueWriteCapability.UpdatePerkPage',
    'LeagueWriteCapability.ApplyMySelection'
)) {
    if ($loadoutService -notmatch [regex]::Escape($required)) {
        Fail "Recommended loadout service is missing revalidated narrow-write behavior: $required"
    }
}
foreach ($forbidden in @(
    'new\s+WindowsLeagueTransportSessionSource', 'new\s+LeagueGameflowMonitor',
    'ProcessLockfileLeagueSessionDiscovery', 'new\s+HttpClient'
)) {
    if ($loadoutService -match $forbidden) {
        Fail "Recommended loadout service created a duplicate League runtime owner: $forbidden"
    }
}

foreach ($required in @(
    'ILeagueRecommendedAutoApplyService', 'ILeagueBuildAdvisorService',
    'ILeagueBuildLoadoutService', 'ILeagueItemSetService', 'ILeagueGameflowObservationSource',
    '_gameflow.Observed += OnGameflowObserved', 'StabilityWindow', 'BuildFingerprint',
    '_attemptedFingerprint', 'stable-context', 'already-attempted',
    'PrepareAsync', 'ApplyAsync', 'champ-select-ended'
)) {
    if ($autoService -notmatch [regex]::Escape($required)) {
        Fail "Recommended auto-apply service is missing shared-heartbeat safety behavior: $required"
    }
}
foreach ($forbidden in @(
    'Task\.Delay', 'new\s+HttpClient', 'new\s+WindowsLeagueTransportSessionSource',
    'new\s+LeagueGameflowMonitor', 'ProcessLockfileLeagueSessionDiscovery',
    '/lol-gameflow/v1/gameflow-phase'
)) {
    if ($autoService -match $forbidden) {
        Fail "Recommended auto-apply created a second polling/transport owner: $forbidden"
    }
}

foreach ($required in @(
    'ISettings2Repository', 'ILeagueRecommendedAutoApplyService', 'SetEnabledAsync',
    'AutoApplyRecommended', 'SettingsLoadOrigin.RecoveredLastKnownGood',
    'SettingsLoadOrigin.RecoveryDefaults', '_automation.Configure'
)) {
    if ($settingsVm -notmatch [regex]::Escape($required)) {
        Fail "Recommended auto-apply settings presenter is missing persistence/recovery behavior: $required"
    }
}
foreach ($forbidden in @(
    'FACM\.Infrastructure', 'FACM\.Platform\.Windows', 'HttpClient', '/lol-',
    'LeagueWriteCommand', 'ILeagueWriteGateway'
)) {
    if ($settingsVm -match $forbidden) {
        Fail "Recommended auto-apply settings presenter crossed its intent boundary: $forbidden"
    }
}

foreach ($required in @(
    'EnsureLeagueRecommendedAutoApply', 'ILeagueReadGateway readGateway',
    'ILeagueWriteGateway writeGateway', 'new LeagueBuildAdvisorService',
    'new LeagueBuildLoadoutService(dataSource, readGateway, writeGateway)',
    'new LeagueItemSetService', 'new LeagueRecommendedAutoApplyService',
    'AutoApplyRecommended', 'ProcessExit'
)) {
    if ($composition -notmatch [regex]::Escape($required)) {
        Fail "Recommended setup composition is missing shared read/write gateway wiring: $required"
    }
}
if ((Count-Matches $composition 'new\s+LeagueRecommendedAutoApplyService\s*\(') -ne 1) {
    Fail 'Recommended auto-apply must have exactly one process-wide construction site.'
}
foreach ($forbidden in @(
    'new\s+WindowsLeagueTransportSessionSource', 'new\s+LeagueGameflowMonitor',
    'new\s+LeagueWorkbenchDataSource', 'ProcessLockfileLeagueSessionDiscovery'
)) {
    if ($composition -match $forbidden) {
        Fail "Recommended setup composition created a duplicate runtime owner: $forbidden"
    }
}

foreach ($required in @(
    'CreateLeagueBuildLoadoutService', 'CreateLeagueRecommendedAutoApplySettingsViewModel',
    'OnLeagueLoadoutApplyClicked', 'OnLeagueRecommendedAutoApplyToggled',
    'service.PrepareAsync(advisor)', 'service.ApplyAsync(plan)',
    'ContentDialogResult.Primary', 'FACM.League.ApplyRecommendedLoadout',
    'FACM.League.AutoApplyRecommended', 'FACM.League.AutoApplyRecommendedStatus'
)) {
    if ($actions -notmatch [regex]::Escape($required)) {
        Fail "Recommended setup WinUI intent surface is missing: $required"
    }
}
if ($actions -notmatch 'ShowAsync\(\)\s*!=\s*ContentDialogResult\.Primary') {
    Fail 'Manual recommended loadout apply must require explicit primary confirmation.'
}
foreach ($forbidden in @(
    'FACM\.Infrastructure', 'FACM\.Platform\.Windows', 'HttpClient',
    'WindowsLeagueTransportSessionSource', 'LeagueGameflowMonitor',
    '/lol-', 'LeagueWriteCommand', 'ILeagueWriteGateway'
)) {
    if ($actions -match $forbidden) {
        Fail "Recommended setup WinUI crossed its intent boundary: $forbidden"
    }
}

foreach ($required in @(
    'ValidateFlashSlotPreservation', 'ValidateFingerprintStability',
    'ValidateDisabledDoesNoWorkAsync', 'ValidateStableContextAppliesAtMostOnceAsync',
    'ValidateBlockedLoadoutStopsItemSetWriteAsync', 'already-attempted',
    'Item-set disk write continued after the League loadout context was blocked'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) {
        Fail "Recommended setup deterministic smoke is missing: $required"
    }
}
if ((Count-Matches $smokeProgram 'LeagueRecommendedAutoApplySmoke\.RunAsync') -ne 1) {
    Fail 'Foundation smoke must register recommended auto-apply exactly once.'
}

Write-Host 'League recommended loadout: explicit-confirmation + context revalidation + owned rune page boundary'
Write-Host 'League recommended auto-apply: one shared heartbeat + stable fingerprint + at-most-once transaction'
Write-Host 'League recommended settings: Settings 2.0 persisted toggle + recovery read-only behavior'
Write-Host 'League recommended deterministic smoke: disabled / stable-once / blocked-write scenarios'
Write-Host 'FACM 4.0 League Recommended Setup contract: SUCCESS'
