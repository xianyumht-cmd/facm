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

$coreToolPath = Join-Path $Root 'src/FACM.Core/Repair/RepairToolContracts.cs'
$coreLeaguePath = Join-Path $Root 'src/FACM.Core/League/LeagueGameRepair.cs'
$leagueContractsPath = Join-Path $Root 'src/FACM.Core/League/LeagueContracts.cs'
$driverPath = Join-Path $Root 'src/FACM.Platform.Windows/Repair/WindowsRepairToolService.cs'
$gameRepairPath = Join-Path $Root 'src/FACM.Platform.Windows/League/WindowsLeagueGameRepairService.cs'
$toolVmPath = Join-Path $Root 'src/FACM.App/ViewModels/RepairToolsViewModel.cs'
$gameVmPath = Join-Path $Root 'src/FACM.App/ViewModels/LeagueGameRepairViewModel.cs'
$gameUiPath = Join-Path $Root 'src/FACM.App/MainWindow.GameRepair.cs'
$mainXamlPath = Join-Path $Root 'src/FACM.App/MainWindow.xaml'
$appPath = Join-Path $Root 'src/FACM.App/App.xaml.cs'
$platformProjectPath = Join-Path $Root 'src/FACM.Platform.Windows/FACM.Platform.Windows.csproj'
$foundationSmokePath = Join-Path $Root 'src/FACM.FoundationSmoke/RepairParitySmoke.cs'
$windowsSmokePath = Join-Path $Root 'src/FACM.WindowsSmoke/RepairWindowsSmoke.cs'

foreach ($path in @(
    $coreToolPath, $coreLeaguePath, $leagueContractsPath, $driverPath, $gameRepairPath,
    $toolVmPath, $gameVmPath, $gameUiPath, $mainXamlPath, $appPath,
    $platformProjectPath, $foundationSmokePath, $windowsSmokePath
)) {
    if (-not (Test-Path $path)) { Fail "Repair contract file missing: $path" }
}

$core = (Get-Content $coreToolPath -Raw) + "`n" + (Get-Content $coreLeaguePath -Raw)
$leagueContracts = Get-Content $leagueContractsPath -Raw
$driver = Get-Content $driverPath -Raw
$gameRepair = Get-Content $gameRepairPath -Raw
$viewModels = (Get-Content $toolVmPath -Raw) + "`n" + (Get-Content $gameVmPath -Raw)
$gameUi = Get-Content $gameUiPath -Raw
$xaml = Get-Content $mainXamlPath -Raw
$app = Get-Content $appPath -Raw
$platformProject = Get-Content $platformProjectPath -Raw
$foundationSmoke = Get-Content $foundationSmokePath -Raw
$windowsSmoke = Get-Content $windowsSmokePath -Raw

foreach ($forbidden in @('Microsoft\.UI', 'System\.Windows\.Forms', 'System\.Diagnostics\.Process', '\bProcess\.', 'DllImport', 'LibraryImport')) {
    if ($core -match $forbidden) { Fail "Core repair contract crossed platform/UI boundary: $forbidden" }
}
foreach ($required in @(
    'IRepairToolService', 'RepairToolLaunchResult', 'ILeagueGameRepairService',
    'LeagueWindowRepairPlanner', 'LeagueWindowBounds', 'RepairWindowAsync',
    'SetAutoRepairEnabled', 'SkipSettlementAsync', 'RestartClientUxAsync', 'ExitGameAsync'
)) {
    if ($core -notmatch [regex]::Escape($required)) { Fail "Core repair contract missing: $required" }
}

foreach ($required in @(
    'LeagueWriteCapability.PlayAgain', '/lol-lobby/v2/play-again',
    'LeagueWriteCapability.RestartClientUx', '/riotclient/kill-and-restart-ux'
)) {
    if ($leagueContracts -notmatch [regex]::Escape($required)) { Fail "League repair writer allowlist missing: $required" }
}
if ($leagueContracts -match '/lol-lobby/v2/queue|/lol-gameflow/v1/session') {
    Fail 'Repair writer allowlist widened beyond the two frozen 3.5.15 repair endpoints.'
}

foreach ($required in @(
    '4180BAE46BED95661D63DC8D08DD458AE866CC107AB0F00AFC647B9BEB8B4ECA',
    'FACM.Platform.Windows.Resources.DriverCleanup', 'HasExpectedSha256',
    'File.Move', 'Process.Start', 'UseShellExecute = true'
)) {
    if ($driver -notmatch [regex]::Escape($required)) { Fail "Driver cleanup integrity/launch behavior missing: $required" }
}
if ($driver -match 'Fix-LCU-Window|--mode') { Fail '4.0 driver tool service must not resurrect legacy Fix-LCU modes.' }
if ($platformProject -notmatch [regex]::Escape('tools\clean driver.exe')) { Fail 'Driver cleanup payload is not embedded by Platform.Windows.' }
if ($platformProject -match 'Fix-LCU-Window|fix-mode-[1-4]') { Fail 'Platform.Windows must not embed legacy Fix-LCU payloads.' }

foreach ($required in @(
    'WindowsLeagueGameRepairService', 'ILeagueReadGateway', 'ILeagueWriteGateway',
    '/riotclient/zoom-scale', 'SetWinEventHook', 'EventObjectLocationChange',
    'TimeSpan.FromMilliseconds(380)', 'TimeSpan.FromSeconds(2)',
    'RCLIENT', 'CefBrowserWindow', 'LeagueClientUx', 'SetWindowPos',
    'LeagueWriteCapability.PlayAgain', 'LeagueWriteCapability.RestartClientUx',
    'League of Legends(TM)', 'League of Legends'
)) {
    if ($gameRepair -notmatch [regex]::Escape($required)) { Fail "Native League repair behavior missing: $required" }
}
foreach ($forbidden in @('HttpClient', 'LeagueTransportSessionSource', 'WindowsLeagueTransportSessionSource', 'Fix-LCU-Window', '--mode')) {
    if ($gameRepair -match $forbidden) { Fail "Native game repair created forbidden second transport/legacy path: $forbidden" }
}

foreach ($forbidden in @('System\.Diagnostics', '\bProcess\.', 'DllImport', 'LibraryImport', 'HttpClient', '\bFile\.', '\bDirectory\.')) {
    if ($viewModels -match $forbidden) { Fail "Repair ViewModel crossed platform ownership boundary: $forbidden" }
}
foreach ($required in @('IRepairToolService', 'ILeagueGameRepairService', 'LaunchDriverCleanup', 'RepairWindowAsync', 'SkipSettlementAsync', 'RestartClientUxAsync', 'ExitGameAsync')) {
    if ($viewModels -notmatch [regex]::Escape($required)) { Fail "Repair ViewModel behavior missing: $required" }
}

foreach ($id in @(
    'FACM.Repair.Privilege', 'FACM.Repair.DriverCleanup', 'FACM.Repair.WindowNow',
    'FACM.Repair.AutoWindow', 'FACM.Repair.SkipSettlement', 'FACM.Repair.RestartClientUx',
    'FACM.Repair.ExitGame', 'FACM.Repair.GameStatus'
)) {
    if ($xaml -notmatch [regex]::Escape($id)) { Fail "Repair WinUI AutomationId missing: $id" }
}
if ($xaml -match '#[0-9A-Fa-f]{6,8}') { Fail 'Repair WinUI must use semantic design resources, not hard-coded colors.' }
foreach ($required in @('ConfigureGameRepair', 'OnRepairFixWindowClick', 'OnRepairAutoWindowClick', 'OnRepairSkipSettlementClick', 'OnRepairRestartClientUxClick', 'OnRepairExitGameClick')) {
    if ($gameUi -notmatch [regex]::Escape($required)) { Fail "Repair WinUI handler missing: $required" }
}
foreach ($forbidden in @('System\.Diagnostics', '\bProcess\.', 'DllImport', 'LibraryImport', 'HttpClient', 'SetWindowPos', 'SetWinEventHook')) {
    if ($gameUi -match $forbidden) { Fail "Repair WinUI presentation owns platform behavior: $forbidden" }
}

if ((Count-Matches $app 'new\s+LeagueHttpGateway\s*\(') -ne 1) { Fail 'FACM.App must compose exactly one LeagueHttpGateway.' }
if ((Count-Matches $app 'new\s+WindowsLeagueGameRepairService\s*\(') -ne 1) { Fail 'FACM.App must compose exactly one process-wide native game repair service.' }
foreach ($required in @('new WindowsLeagueGameRepairService(_leagueGateway, _leagueGateway)', 'ConfigureGameRepair(gameRepair)', '_leagueGameRepairService?.Dispose()')) {
    if ($app -notmatch [regex]::Escape($required)) { Fail "FACM.App native repair composition missing: $required" }
}

foreach ($required in @(
    'play-again exact target', 'restart-ux exact target', 'play-again arbitrary target rejection',
    'sane window remains unchanged', 'offscreen right clamp', 'remembered sane size', 'fallback aspect'
)) {
    if ($foundationSmoke -notmatch [regex]::Escape($required)) { Fail "Repair foundation smoke missing assertion: $required" }
}
foreach ($required in @(
    'driver cleanup contract hash', 'driver cleanup embedded resource',
    'legacy Fix-LCU must not be embedded', 'driver cleanup embedded SHA-256'
)) {
    if ($windowsSmoke -notmatch [regex]::Escape($required)) { Fail "Repair Windows smoke missing assertion: $required" }
}

Write-Host 'Repair Core intent/planner boundary: OK'
Write-Host 'Repair narrow League write allowlist: OK'
Write-Host 'Driver cleanup fixed-resource/hash boundary: OK'
Write-Host 'Native League window/event-driven repair boundary: OK'
Write-Host 'Repair ViewModel/WinUI ownership boundary: OK'
Write-Host 'Legacy Fix-LCU non-regression boundary: OK'
Write-Host 'FACM 4.0 Repair contract: SUCCESS'
