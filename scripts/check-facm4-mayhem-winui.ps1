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
    'CanQuery', 'CanCancel', 'Result', '查询已取消',
    'QueryTimeout = TimeSpan.FromSeconds(13)', 'CancelAfter(QueryTimeout)',
    '_userCancellationRequested', '查询超时，请稍后重试。'
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
    'FACM.League.Mayhem.SaveImage', 'FACM.League.Mayhem.CopyImage', 'FACM.League.Mayhem.ExportCard',
    'FACM.League.Mayhem.Progress', 'FACM.League.Mayhem.Status', 'FACM.League.Mayhem.Results',
    '海斗攻略', '版本修正', '基础 ARAM', 'Mayhem 专属', '两层独立展示，不做数值叠加',
    '这一局怎么选', '强化符文决策榜', '技能与出装', '版本胜率前十',
    'VirtualKey.Enter', 'OnMayhemQueryKeyDown', 'RunMayhemQueryAsync',
    'MayhemExportWidth = 840', 'RenderTargetBitmap', 'FileSavePicker',
    'BitmapEncoder.PngEncoderId', 'RandomAccessStreamReference.CreateFromStream',
    'Clipboard.SetContent', 'Clipboard.Flush', 'EncodeMayhemResultPngAsync',
    'OnMayhemSaveImageClick', 'OnMayhemCopyImageClick', 'DisposeMayhemSurface'
)) {
    if ($ui -notmatch [regex]::Escape($required)) { Fail "Mayhem WinUI surface missing: $required" }
}
foreach ($forbidden in @(
    'HttpClient', 'HttpRequestMessage', 'System\.IO\.File', 'System\.IO\.Directory',
    'LeagueWriteCommand', 'ILeagueWriteGateway', 'Process\.', 'RegisterHotKey',
    'System\.Drawing', 'System\.Windows\.Forms'
)) {
    if ($ui -match $forbidden) { Fail "Mayhem WinUI owns forbidden network/write/legacy-render detail: $forbidden" }
}

if ($ui -notmatch '_mayhemQueryBox\.KeyDown\s*\+=\s*OnMayhemQueryKeyDown' -or
    $ui -notmatch '_mayhemQueryBox\.KeyDown\s*-=\s*OnMayhemQueryKeyDown') {
    Fail 'Mayhem Enter-key handler must be attached and detached with the WinUI surface lifecycle.'
}
if ($ui -notmatch '_mayhemSaveImageButton\.Click\s*\+=\s*OnMayhemSaveImageClick' -or
    $ui -notmatch '_mayhemSaveImageButton\.Click\s*-=\s*OnMayhemSaveImageClick' -or
    $ui -notmatch '_mayhemCopyImageButton\.Click\s*\+=\s*OnMayhemCopyImageClick' -or
    $ui -notmatch '_mayhemCopyImageButton\.Click\s*-=\s*OnMayhemCopyImageClick') {
    Fail 'Mayhem image export handlers must be attached and detached with the WinUI surface lifecycle.'
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

Write-Host 'Mayhem WinUI: user-driven query/cancel + 13s total timeout + Enter-to-query'
Write-Host 'Mayhem presentation: summary / split balance / decisions / augments / build / top-ten'
Write-Host 'Mayhem export: 840px WinUI result-card PNG + SaveFilePicker + Clipboard bitmap'
Write-Host 'Mayhem composition: one product query service + shared League read gateway + runtime cache'
Write-Host 'Mayhem boundary: no direct HTTP, File/Directory IO, League write, process/hotkey or legacy GDI/WinForms rendering'
Write-Host 'FACM 4.0 Mayhem WinUI query/export surface contract: SUCCESS'
