param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

$vmPath = Join-Path $Root 'src/FACM.App/ViewModels/MayhemViewModel.cs'
$uiPath = Join-Path $Root 'src/FACM.App/MainWindow.Mayhem.cs'
$runtimePath = Join-Path $Root 'src/FACM.App/MainWindow.LeagueWorkbenchRuntime.cs'
$appPath = Join-Path $Root 'src/FACM.App/App.Mayhem.cs'
$productPath = Join-Path $Root 'src/FACM.Infrastructure/Mayhem/MayhemProductQueryService.cs'

foreach ($path in @($vmPath, $uiPath, $runtimePath, $appPath, $productPath)) {
    if (-not (Test-Path $path)) { Fail "Mayhem WinUI contract file missing: $path" }
}

$vm = Get-Content $vmPath -Raw
$ui = Get-Content $uiPath -Raw
$runtime = Get-Content $runtimePath -Raw
$app = Get-Content $appPath -Raw
$product = Get-Content $productPath -Raw

foreach ($required in @(
    'IMayhemQueryService', 'QueryAsync', 'Cancel', 'CancellationTokenSource', 'IsBusy',
    'CanQuery', 'CanCancel', 'Result', '查询已取消'
)) {
    if ($vm -notmatch [regex]::Escape($required)) { Fail "Mayhem ViewModel missing behavior: $required" }
}
foreach ($forbidden in @(
    'HttpClient', 'HttpRequestMessage', 'System\.IO\.File', 'System\.IO\.Directory',
    'LeagueWriteCommand', 'ILeagueWriteGateway', 'Process\.', 'RegisterHotKey', 'Microsoft\.Win32'
)) {
    if ($vm -match $forbidden) { Fail "Mayhem ViewModel crossed presentation boundary: $forbidden" }
}

foreach ($required in @(
    'FACM.League.Mayhem.Query', 'FACM.League.Mayhem.Search', 'FACM.League.Mayhem.Cancel',
    'FACM.League.Mayhem.Progress', 'FACM.League.Mayhem.Status', 'FACM.League.Mayhem.Results',
    '海斗攻略', '版本修正', '基础 ARAM', 'Mayhem 专属', '两层独立展示，不做数值叠加',
    '这一局怎么选', '强化符文决策榜', '技能与出装', '版本胜率前十',
    'OnMayhemQueryClick', 'OnMayhemCancelClick', 'DisposeMayhemSurface'
)) {
    if ($ui -notmatch [regex]::Escape($required)) { Fail "Mayhem WinUI surface missing: $required" }
}
foreach ($forbidden in @(
    'HttpClient', 'HttpRequestMessage', 'System\.IO\.File', 'System\.IO\.Directory',
    'LeagueWriteCommand', 'ILeagueWriteGateway', 'Process\.', 'RegisterHotKey'
)) {
    if ($ui -match $forbidden) { Fail "Mayhem WinUI owns forbidden network/write/platform detail: $forbidden" }
}

if ($runtime -notmatch 'InitializeMayhemSurface\(\)' -or $runtime -notmatch 'DisposeMayhemSurface\(\)') {
    Fail 'League Workbench must attach and dispose the Mayhem query surface.'
}

foreach ($required in @(
    'MayhemProductQueryService', 'RuntimePathLayout.From', 'WindowsExecutablePathProvider',
    '_leagueGateway', 'CreateMayhemViewModel'
)) {
    if ($app -notmatch [regex]::Escape($required)) { Fail "App Mayhem composition missing: $required" }
}
if ($app -match 'new\s+WindowsLeagueTransportSessionSource') {
    Fail 'Mayhem composition must reuse the process-wide League gateway, not create a second League session owner.'
}

foreach ($required in @(
    'MayhemOfficialPatchQueryService', 'MayhemAugmentEnrichmentService', 'MayhemBuildDetailsService',
    'MayhemBaseAramBalanceService', 'MayhemDecisionLocalizationService', 'Task.WhenAll',
    'ILeagueReadGateway', 'IMayhemQueryService'
)) {
    if ($product -notmatch [regex]::Escape($required)) { Fail "Mayhem product pipeline missing: $required" }
}
if ($product -match 'ILeagueWriteGateway|LeagueWriteCommand|System\.Drawing|System\.Windows\.Forms|Microsoft\.UI') {
    Fail 'Mayhem product pipeline crossed query/presentation/write boundary.'
}

Write-Host 'Mayhem WinUI: user-driven query/cancel/busy surface with stable AutomationIds'
Write-Host 'Mayhem presentation: summary / split balance / decisions / augments / build / top-ten'
Write-Host 'Mayhem composition: one product query service + shared League read gateway + runtime cache'
Write-Host 'Mayhem boundary: App/WinUI has no direct HTTP, file deletion, process, hotkey or League write ownership'
Write-Host 'FACM 4.0 Mayhem WinUI query surface contract: SUCCESS'
