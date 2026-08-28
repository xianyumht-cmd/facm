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

$coreContractsPath = Join-Path $Root 'src/FACM.Core/League/LeagueContracts.cs'
$coreGameflowPath = Join-Path $Root 'src/FACM.Core/League/LeagueGameflow.cs'
$coreWorkbenchPath = Join-Path $Root 'src/FACM.Core/League/LeagueWorkbench.cs'
$coreWorkbenchDataPath = Join-Path $Root 'src/FACM.Core/League/LeagueWorkbenchData.cs'
$coreBuildAdvisorPath = Join-Path $Root 'src/FACM.Core/League/LeagueBuildAdvisor.cs'
$coreItemSetPath = Join-Path $Root 'src/FACM.Core/League/LeagueItemSet.cs'
$monitorPath = Join-Path $Root 'src/FACM.Infrastructure/League/LeagueGameflowMonitor.cs'
$dataSourcePath = Join-Path $Root 'src/FACM.Infrastructure/League/LeagueWorkbenchDataSource.cs'
$advisorPath = Join-Path $Root 'src/FACM.Infrastructure/League/LeagueBuildAdvisorService.cs'
$itemSetPath = Join-Path $Root 'src/FACM.Infrastructure/League/LeagueItemSetService.cs'
$matchmakingPath = Join-Path $Root 'src/FACM.Infrastructure/League/LeagueMatchmakingAutomationService.cs'
$viewModelPath = Join-Path $Root 'src/FACM.App/ViewModels/LeagueWorkbenchViewModel.cs'
$appPath = Join-Path $Root 'src/FACM.App/App.xaml.cs'
$productCompositionPath = Join-Path $Root 'src/FACM.App/App.LeagueWorkbenchProductization.cs'
$mainXamlPath = Join-Path $Root 'src/FACM.App/MainWindow.xaml'
$mainCodePath = Join-Path $Root 'src/FACM.App/MainWindow.xaml.cs'
$runtimeUiPath = Join-Path $Root 'src/FACM.App/MainWindow.LeagueWorkbenchRuntime.cs'
$productActionsPath = Join-Path $Root 'src/FACM.App/MainWindow.LeagueWorkbenchActions.cs'
$textPath = Join-Path $Root 'src/FACM.Core/Text/UiTextContracts.cs'

foreach ($path in @(
    $coreContractsPath, $coreGameflowPath, $coreWorkbenchPath, $coreWorkbenchDataPath,
    $coreBuildAdvisorPath, $coreItemSetPath, $monitorPath, $dataSourcePath, $advisorPath,
    $itemSetPath, $matchmakingPath, $viewModelPath, $appPath, $productCompositionPath,
    $mainXamlPath, $mainCodePath, $runtimeUiPath, $productActionsPath, $textPath
)) {
    if (-not (Test-Path $path)) { Fail "League Workbench contract file missing: $path" }
}

$coreContracts = Get-Content $coreContractsPath -Raw
$coreGameflow = Get-Content $coreGameflowPath -Raw
$coreWorkbench = Get-Content $coreWorkbenchPath -Raw
$coreWorkbenchData = Get-Content $coreWorkbenchDataPath -Raw
$coreBuildAdvisor = Get-Content $coreBuildAdvisorPath -Raw
$coreItemSet = Get-Content $coreItemSetPath -Raw
$monitor = Get-Content $monitorPath -Raw
$dataSource = Get-Content $dataSourcePath -Raw
$advisor = Get-Content $advisorPath -Raw
$itemSet = Get-Content $itemSetPath -Raw
$matchmaking = Get-Content $matchmakingPath -Raw
$viewModel = Get-Content $viewModelPath -Raw
$app = Get-Content $appPath -Raw
$productComposition = Get-Content $productCompositionPath -Raw
$mainXaml = Get-Content $mainXamlPath -Raw
$mainCode = Get-Content $mainCodePath -Raw
$runtimeUi = Get-Content $runtimeUiPath -Raw
$productActions = Get-Content $productActionsPath -Raw
$text = Get-Content $textPath -Raw

foreach ($required in @(
    'LeagueWriteCapability.StartMatchmaking', 'LeagueWriteCapability.AcceptReadyCheck',
    '/lol-lobby/v2/lobby/matchmaking/search', '/lol-matchmaking/v1/ready-check/accept'
)) {
    if ($coreContracts -notmatch [regex]::Escape($required)) {
        Fail "League narrow write contract is missing: $required"
    }
}

foreach ($state in @('NotRunning', 'Connecting', 'Lobby', 'Matchmaking', 'ReadyCheck', 'ChampSelect', 'InGame', 'PostGame', 'ClientError')) {
    if ($coreGameflow -notmatch ('LeagueProductState\.' + [regex]::Escape($state))) {
        Fail "Gameflow mapper is missing Product State: $state"
    }
}
foreach ($phase in @('Matchmaking', 'ReadyCheck', 'ChampSelect', 'InProgress', 'WatchInProgress', 'Reconnect', 'GameStart', 'WaitingForStats', 'PreEndOfGame', 'EndOfGame')) {
    if ($coreGameflow -notmatch ('"' + [regex]::Escape($phase) + '"')) {
        Fail "Gameflow mapper is missing legacy phase coverage: $phase"
    }
}
foreach ($required in @('ILeagueGameflowObservationSource', 'event EventHandler<LeagueGameflowChangedEventArgs>? Observed')) {
    if ($coreGameflow -notmatch [regex]::Escape($required)) {
        Fail "Gameflow shared observation contract is missing: $required"
    }
}

foreach ($id in @('Match', 'Strategy', 'Automation')) {
    if ((Count-Matches $coreWorkbench ('new\(' + [regex]::Escape($id) + ',')) -ne 1) {
        Fail "League Workbench must contain exactly one $id section."
    }
}
if ((Count-Matches $coreWorkbench 'new\((Match|Strategy|Automation),') -ne 3) {
    Fail 'League Workbench must expose exactly three user-facing sections.'
}

foreach ($required in @(
    'ILeagueWorkbenchDataSource', 'LoadDashboardAsync', 'LoadCurrentPlayerAsync', 'LoadLiveAsync',
    'LeagueWorkbenchDashboardSnapshot', 'LeagueWorkbenchPlayerSnapshot', 'LeagueWorkbenchLiveSnapshot'
)) {
    if ($coreWorkbenchData -notmatch [regex]::Escape($required)) {
        Fail "League Workbench Core read contract is missing: $required"
    }
}
foreach ($required in @(
    'ILeagueBuildAdvisorService', 'LeagueBuildAdvisorSnapshot', 'LeagueBuildRecommendation',
    'LeagueBuildAdvisorState', 'InGameCache', 'InGameNoCache'
)) {
    if ($coreBuildAdvisor -notmatch [regex]::Escape($required)) {
        Fail "Build Advisor Core contract is missing: $required"
    }
}
foreach ($required in @(
    'ILeagueItemSetService', 'PrepareAsync', 'ApplyAsync', 'LeagueItemSetPlan',
    'LeagueItemSetApplyResult', 'LeagueItemSetApplyState'
)) {
    if ($coreItemSet -notmatch [regex]::Escape($required)) {
        Fail "Item Set Core contract is missing: $required"
    }
}

foreach ($required in @(
    'ILeagueReadGateway', 'ILeagueSessionAccessor', 'IProductStateWriter', 'PerformanceBudgetProvider',
    'LeagueGameflowPhaseMapper.Map', 'LeagueGameflowCadence.Resolve',
    'ILeagueGameflowObservationSource', 'Observed', 'observedHandler'
)) {
    if ($monitor -notmatch [regex]::Escape($required)) { Fail "Gameflow owner is missing required shared contract: $required" }
}
foreach ($forbidden in @('new\s+HttpClient', 'new\s+WindowsLeagueTransportSessionSource', 'ProcessLockfileLeagueSessionDiscovery', 'ILeagueWriteGateway', 'LeagueWriteCommand')) {
    if ($monitor -match $forbidden) { Fail "Gameflow monitor crossed its read/state ownership boundary: $forbidden" }
}

foreach ($required in @(
    'ILeagueReadGateway', 'ILeagueGameflowReader', '_gameflow?.Current',
    '/lol-summoner/v1/current-summoner', '/lol-ranked/v1/current-ranked-stats',
    '/lol-match-history/v1/products/lol/', '/lol-champ-select/v1/session', '/lol-gameflow/v1/session'
)) {
    if ($dataSource -notmatch [regex]::Escape($required)) {
        Fail "League Workbench data source is missing shared read behavior: $required"
    }
}
foreach ($forbidden in @(
    'new\s+HttpClient', 'new\s+WindowsLeagueTransportSessionSource',
    'new\s+LeagueGameflowMonitor', 'ProcessLockfileLeagueSessionDiscovery',
    'Task\.Delay', 'ILeagueWriteGateway', 'LeagueWriteCommand'
)) {
    if ($dataSource -match $forbidden) {
        Fail "League Workbench data source created a second transport/poll/write owner: $forbidden"
    }
}

foreach ($required in @(
    'ILeagueWorkbenchDataSource', 'ILeagueReadGateway', 'IOpggBuildSource', 'OpggBuildHttpSource',
    'BuildCacheDuration', 'CatalogCacheDuration', 'VersionCacheDuration', 'RankedPositionCacheDuration',
    'LeagueBuildAdvisorState.InGameCache', 'LeagueBuildAdvisorState.InGameNoCache',
    'ResolveOpggMode', 'ResolveOpggPosition', 'BuildPath'
)) {
    if ($advisor -notmatch [regex]::Escape($required)) {
        Fail "Build Advisor service is missing required product behavior: $required"
    }
}
foreach ($forbidden in @(
    'new\s+WindowsLeagueTransportSessionSource', 'new\s+LeagueGameflowMonitor',
    'ProcessLockfileLeagueSessionDiscovery', 'Task\.Delay', 'ILeagueWriteGateway', 'LeagueWriteCommand'
)) {
    if ($advisor -match $forbidden) {
        Fail "Build Advisor created a second League runtime/write owner: $forbidden"
    }
}

foreach ($required in @(
    'ILeagueWorkbenchDataSource', 'ILeagueReadGateway', 'LoadLiveAsync',
    'FilePrefix = "facm4-"', 'InstallDirPath = "/data-store/v1/install-dir"',
    'Config', 'Global', 'Recommended', 'champ-select-required', 'champion-changed', 'queue-changed',
    'CommitOwnedFile', 'VerifyItemSetJson', 'CleanupOldOwnedFiles', 'TryResolveTargetDirectory'
)) {
    if ($itemSet -notmatch [regex]::Escape($required)) {
        Fail "Item Set service is missing safe write behavior: $required"
    }
}
foreach ($forbidden in @(
    'FilePrefix\s*=\s*"facm1-', 'new\s+WindowsLeagueTransportSessionSource',
    'new\s+LeagueGameflowMonitor', 'ProcessLockfileLeagueSessionDiscovery',
    'Task\.Delay', 'ILeagueWriteGateway', 'LeagueWriteCommand'
)) {
    if ($itemSet -match $forbidden) {
        Fail "Item Set service crossed its ownership/runtime boundary: $forbidden"
    }
}

foreach ($required in @(
    'ILeagueReadGateway', 'ILeagueWriteGateway', 'ILeagueGameflowObservationSource',
    '_gameflow.Observed += OnGameflowObserved', 'EvaluateObservationAsync',
    'LobbyPath = "/lol-lobby/v2/lobby"', 'SearchStatePath = "/lol-matchmaking/v1/search"',
    'LeagueWriteCapability.StartMatchmaking', 'LeagueWriteCapability.AcceptReadyCheck',
    'canStartActivity', 'isLeader', 'isBot', 'isSpectator', 'Fingerprint',
    'Accepted', 'Declined', '_acceptAttemptedThisReadyCheck'
)) {
    if ($matchmaking -notmatch [regex]::Escape($required)) {
        Fail "Matchmaking automation is missing required shared-heartbeat behavior: $required"
    }
}
foreach ($forbidden in @(
    'Task\.Delay', 'new\s+HttpClient', 'new\s+WindowsLeagueTransportSessionSource',
    'new\s+LeagueGameflowMonitor', 'ProcessLockfileLeagueSessionDiscovery'
)) {
    if ($matchmaking -match $forbidden) {
        Fail "Matchmaking automation created a second polling/transport owner: $forbidden"
    }
}

if ((Count-Matches $app 'new\s+WindowsLeagueTransportSessionSource\s*\(') -ne 1) {
    Fail 'App composition must create exactly one League session owner.'
}
if ((Count-Matches $app 'new\s+LeagueGameflowMonitor\s*\(') -ne 1) {
    Fail 'App composition must create exactly one Gameflow monitor.'
}
if ((Count-Matches $app 'new\s+LeagueMatchmakingAutomationService\s*\(') -ne 1) {
    Fail 'App composition must create exactly one matchmaking automation consumer.'
}
if ((Count-Matches $app 'new\s+PerformanceBudgetProvider\s*\(') -ne 1) {
    Fail 'App composition must create exactly one PerformanceBudgetProvider.'
}
if ((Count-Matches $app 'new\s+LeagueWorkbenchDataSource\s*\(') -ne 1) {
    Fail 'App composition must create exactly one Workbench data source over the shared League runtime.'
}
if ((Count-Matches $app '\.Start\s*\(\s*\)') -lt 1 -or $app -notmatch '_gameflow\.Start\s*\(\s*\)') {
    Fail 'App composition must start the one Gameflow monitor.'
}
foreach ($required in @(
    'ConfigureLeagueAutomationFromSettings', 'AutoMatchmakingEnabled', 'AutoAcceptEnabled',
    '_matchmakingAutomation?.Dispose()'
)) {
    if ($app -notmatch [regex]::Escape($required)) {
        Fail "App composition is missing matchmaking settings/lifetime wiring: $required"
    }
}

foreach ($required in @(
    'viewModel.DataSource', '_leagueGateway', '_performance',
    'new LeagueBuildAdvisorService', 'new LeagueItemSetService', 'ConfigureProductServices'
)) {
    if ($productComposition -notmatch [regex]::Escape($required)) {
        Fail "League product composition is missing shared-runtime wiring: $required"
    }
}
foreach ($forbidden in @(
    'new\s+LeagueWorkbenchDataSource', 'new\s+WindowsLeagueTransportSessionSource',
    'new\s+LeagueGameflowMonitor', 'ProcessLockfileLeagueSessionDiscovery'
)) {
    if ($productComposition -match $forbidden) {
        Fail "League product composition created a duplicate runtime owner: $forbidden"
    }
}

$uiBoundary = $viewModel + "`n" + $mainCode + "`n" + $runtimeUi + "`n" + $productActions
foreach ($forbidden in @(
    'FACM\.Infrastructure', 'FACM\.Platform\.Windows', 'System\.Net\.Http', 'HttpClient',
    'WindowsLeagueTransportSessionSource', 'LeagueGameflowMonitor', 'Task\.Delay',
    '/lol-', 'LeagueWriteCommand', 'ILeagueWriteGateway',
    'Directory\.GetFiles', 'File\.WriteAllText', 'File\.Delete'
)) {
    if ($uiBoundary -match $forbidden) { Fail "League Workbench UI crossed its state/intent boundary: $forbidden" }
}
foreach ($required in @(
    'ILeagueWorkbenchDataSource', 'RefreshAsync', 'Dashboard', 'Player', 'Live',
    'LeagueMatchDescription.Text', 'LeagueStrategyDescription.Text', 'LeagueAutomationDescription.Text',
    'ILeagueBuildAdvisorService', 'ILeagueItemSetService', 'RefreshBuildAdvisorAsync',
    'PrepareItemSetAsync', 'ApplyItemSetAsync', 'ContentDialog',
    'FACM.League.RefreshBuildAdvisor', 'FACM.League.ApplyItemSet'
)) {
    if ($uiBoundary -notmatch [regex]::Escape($required)) {
        Fail "League Workbench real UI/product intent binding is missing: $required"
    }
}
if ($productActions -notmatch 'ContentDialogResult\.Primary') {
    Fail 'Item Set UI must require explicit primary confirmation before ApplyItemSetAsync.'
}
if ($runtimeUi -notmatch 'ConfigureLeagueWorkbenchProductization' -or $runtimeUi -notmatch 'InitializeLeagueWorkbenchProductActions') {
    Fail 'League runtime surface must initialize product services and product actions.'
}

foreach ($name in @(
    'LeagueWorkbenchPanel', 'LeagueMatchTitle', 'LeagueStrategyTitle', 'LeagueAutomationTitle',
    'LeagueStateValue', 'LeagueBudgetValue'
)) {
    if ((Count-Matches $mainXaml ('x:Name="' + [regex]::Escape($name) + '"')) -ne 1) {
        Fail "League Workbench XAML surface is missing or duplicated: $name"
    }
}

foreach ($constant in @(
    'LeagueWorkbenchMatch', 'LeagueWorkbenchMatchDescription',
    'LeagueWorkbenchStrategy', 'LeagueWorkbenchStrategyDescription',
    'LeagueWorkbenchAutomation', 'LeagueWorkbenchAutomationDescription',
    'LeagueWorkbenchStateLabel', 'LeagueWorkbenchBudgetLabel',
    'LeagueStateNotRunning', 'LeagueStateConnecting', 'LeagueStateLobby',
    'LeagueStateMatchmaking', 'LeagueStateReadyCheck', 'LeagueStateChampSelect',
    'LeagueStateInGame', 'LeagueStatePostGame', 'LeagueStateClientError'
)) {
    if ($text -notmatch ('public const string\s+' + [regex]::Escape($constant) + '\s*=')) {
        Fail "League Workbench UI Text key missing: $constant"
    }
    if ($text -notmatch ('\[UiTextKeys\.' + [regex]::Escape($constant) + '\]\s*=')) {
        Fail "League Workbench UI Text default missing: $constant"
    }
}

Write-Host 'League Gameflow owner: exactly one composition instance + shared observation heartbeat'
Write-Host 'League Workbench data source: shared read gateway + shared gameflow owner'
Write-Host 'League Workbench real surface: dashboard / player / live snapshots'
Write-Host 'League Build Advisor: user-driven OP.GG + in-game cache-only'
Write-Host 'League Item Sets: explicit-confirmation + FACM4-owned atomic write'
Write-Host 'League Matchmaking: shared heartbeat + leader/member eligibility + one-shot ReadyCheck accept'
Write-Host 'FACM 4.0 League Workbench contract: SUCCESS'
