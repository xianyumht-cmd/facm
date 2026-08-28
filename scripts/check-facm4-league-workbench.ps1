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

$coreGameflowPath = Join-Path $Root 'src/FACM.Core/League/LeagueGameflow.cs'
$coreWorkbenchPath = Join-Path $Root 'src/FACM.Core/League/LeagueWorkbench.cs'
$coreWorkbenchDataPath = Join-Path $Root 'src/FACM.Core/League/LeagueWorkbenchData.cs'
$monitorPath = Join-Path $Root 'src/FACM.Infrastructure/League/LeagueGameflowMonitor.cs'
$dataSourcePath = Join-Path $Root 'src/FACM.Infrastructure/League/LeagueWorkbenchDataSource.cs'
$viewModelPath = Join-Path $Root 'src/FACM.App/ViewModels/LeagueWorkbenchViewModel.cs'
$appPath = Join-Path $Root 'src/FACM.App/App.xaml.cs'
$mainXamlPath = Join-Path $Root 'src/FACM.App/MainWindow.xaml'
$mainCodePath = Join-Path $Root 'src/FACM.App/MainWindow.xaml.cs'
$runtimeUiPath = Join-Path $Root 'src/FACM.App/MainWindow.LeagueWorkbenchRuntime.cs'
$textPath = Join-Path $Root 'src/FACM.Core/Text/UiTextContracts.cs'

foreach ($path in @(
    $coreGameflowPath, $coreWorkbenchPath, $coreWorkbenchDataPath, $monitorPath, $dataSourcePath,
    $viewModelPath, $appPath, $mainXamlPath, $mainCodePath, $runtimeUiPath, $textPath
)) {
    if (-not (Test-Path $path)) { Fail "League Workbench contract file missing: $path" }
}

$coreGameflow = Get-Content $coreGameflowPath -Raw
$coreWorkbench = Get-Content $coreWorkbenchPath -Raw
$coreWorkbenchData = Get-Content $coreWorkbenchDataPath -Raw
$monitor = Get-Content $monitorPath -Raw
$dataSource = Get-Content $dataSourcePath -Raw
$viewModel = Get-Content $viewModelPath -Raw
$app = Get-Content $appPath -Raw
$mainXaml = Get-Content $mainXamlPath -Raw
$mainCode = Get-Content $mainCodePath -Raw
$runtimeUi = Get-Content $runtimeUiPath -Raw
$text = Get-Content $textPath -Raw

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

foreach ($required in @('ILeagueReadGateway', 'ILeagueSessionAccessor', 'IProductStateWriter', 'PerformanceBudgetProvider', 'LeagueGameflowPhaseMapper.Map', 'LeagueGameflowCadence.Resolve')) {
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

if ((Count-Matches $app 'new\s+WindowsLeagueTransportSessionSource\s*\(') -ne 1) {
    Fail 'App composition must create exactly one League session owner.'
}
if ((Count-Matches $app 'new\s+LeagueGameflowMonitor\s*\(') -ne 1) {
    Fail 'App composition must create exactly one Gameflow monitor.'
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

$uiBoundary = $viewModel + "`n" + $mainCode + "`n" + $runtimeUi
foreach ($forbidden in @(
    'FACM\.Infrastructure', 'FACM\.Platform\.Windows', 'System\.Net\.Http', 'HttpClient',
    'WindowsLeagueTransportSessionSource', 'LeagueGameflowMonitor', 'Task\.Delay',
    '/lol-', 'LeagueWriteCommand', 'ILeagueWriteGateway'
)) {
    if ($uiBoundary -match $forbidden) { Fail "League Workbench UI crossed its state/intent boundary: $forbidden" }
}
foreach ($required in @(
    'ILeagueWorkbenchDataSource', 'RefreshAsync', 'Dashboard', 'Player', 'Live',
    'LeagueMatchDescription.Text', 'LeagueStrategyDescription.Text', 'LeagueAutomationDescription.Text'
)) {
    if ($uiBoundary -notmatch [regex]::Escape($required)) {
        Fail "League Workbench real UI data binding is missing: $required"
    }
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

Write-Host 'League Gameflow owner: exactly one composition instance'
Write-Host 'League Workbench data source: shared read gateway + shared gameflow owner'
Write-Host 'League Workbench real surface: dashboard / player / live snapshots'
Write-Host 'FACM 4.0 League Workbench contract: SUCCESS'
