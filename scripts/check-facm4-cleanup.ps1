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

$coreContractsPath = Join-Path $Root 'src/FACM.Core/Cleanup/CleanupContracts.cs'
$coreProfilePath = Join-Path $Root 'src/FACM.Core/Cleanup/CleanupProfileContract.cs'
$environmentPath = Join-Path $Root 'src/FACM.Platform.Windows/Cleanup/WindowsCleanupEnvironment.cs'
$enginePath = Join-Path $Root 'src/FACM.Platform.Windows/Cleanup/WindowsCleanupEngine.cs'
$viewModelPath = Join-Path $Root 'src/FACM.App/ViewModels/CleanupViewModel.cs'
$mainXamlPath = Join-Path $Root 'src/FACM.App/MainWindow.xaml'
$mainCodePath = Join-Path $Root 'src/FACM.App/MainWindow.xaml.cs'
$appPath = Join-Path $Root 'src/FACM.App/App.xaml.cs'
$windowsSmokePath = Join-Path $Root 'src/FACM.WindowsSmoke/CleanupSmoke.cs'

foreach ($path in @(
    $coreContractsPath, $coreProfilePath, $environmentPath, $enginePath,
    $viewModelPath, $mainXamlPath, $mainCodePath, $appPath, $windowsSmokePath
)) {
    if (-not (Test-Path $path)) { Fail "Cleanup contract file missing: $path" }
}

$core = (Get-Content $coreContractsPath -Raw) + "`n" + (Get-Content $coreProfilePath -Raw)
$environment = Get-Content $environmentPath -Raw
$engine = Get-Content $enginePath -Raw
$viewModel = Get-Content $viewModelPath -Raw
$mainXaml = Get-Content $mainXamlPath -Raw
$mainCode = Get-Content $mainCodePath -Raw
$app = Get-Content $appPath -Raw
$smoke = Get-Content $windowsSmokePath -Raw

foreach ($forbidden in @('Microsoft\.UI', 'System\.Windows\.Forms', 'Microsoft\.Win32', 'Process\.GetProcesses', 'Environment\.SpecialFolder', 'DllImport', 'LibraryImport')) {
    if ($core -match $forbidden) { Fail "Core cleanup contract crossed platform/UI boundary: $forbidden" }
}

foreach ($required in @(
    'CleanupApplicationService', 'ExecuteConfirmedAsync', 'explicit confirmation',
    'CleanupProfileSnapshot', 'CleanupProfileContract', 'ICleanupEnvironment',
    'AntiCheatExpert', 'PreservedChildFolderName', 'DATA', 'LogSearchPattern',
    'LeagueClientUx', 'RiotClientServices'
)) {
    if ($core -notmatch [regex]::Escape($required)) { Fail "Core cleanup contract missing: $required" }
}

foreach ($required in @(
    'WindowsCleanupEnvironment', 'FindGameRootAsync', 'ResolveGameRootAsync',
    'MaxVisitedDirectories', 'SearchTimeBudget', 'RegistryHive', 'RegistryView',
    'Process.GetProcesses', 'IsAdministrator', 'RestartElevatedForCleanup', 'Verb = "runas"'
)) {
    if ($environment -notmatch [regex]::Escape($required)) { Fail "Windows cleanup environment missing: $required" }
}
if ($environment -match 'System\.Windows\.Forms|Microsoft\.UI\.Xaml') {
    Fail 'Windows cleanup environment must not own UI controls.'
}

foreach ($required in @(
    'WindowsCleanupEngine', 'CreatePlanAsync', 'ExecuteAsync', 'CollapseNestedTargets',
    'RevalidateTarget', 'EnsureNotReparsePoint', 'EnsureNoReparsePointInPath', 'FileAttributes.ReparsePoint',
    'CombineInsideRoot', 'PreservedChildFolderName', 'CleanupRuleKind.ProgramFilesDirectory',
    'CleanupRuleKind.ProgramDataDirectory', 'CleanupRuleKind.ContainerChild',
    'CleanupRuleKind.ExtraDirectory', 'CleanupRuleKind.LogFile',
    'GetRunningRelatedProcesses', 'IsAdministrator', 'MaxScannedEntriesPerTarget',
    'MaxScanTimePerTarget', 'cancellationToken.ThrowIfCancellationRequested'
)) {
    if ($engine -notmatch [regex]::Escape($required)) { Fail "Windows cleanup engine missing safety behavior: $required" }
}
foreach ($forbidden in @(
    'Directory\.Delete\([^\)]*,\s*true\s*\)',
    'SearchOption\.AllDirectories',
    'Process\.Kill\(',
    'taskkill', 'sc\.exe', 'schtasks', 'cmd\.exe', 'powershell\.exe'
)) {
    if ($engine -match $forbidden) { Fail "Cleanup engine contains forbidden broad/destructive behavior: $forbidden" }
}

foreach ($forbidden in @(
    'System\.Windows\.Forms', 'Microsoft\.Win32', '\bFile\.', '\bDirectory\.',
    'Process\.GetProcesses', 'Environment\.SpecialFolder', 'DllImport', 'LibraryImport',
    'WindowsCleanupEngine', 'WindowsCleanupEnvironment'
)) {
    if ($viewModel -match $forbidden) { Fail "CleanupViewModel crossed UI/platform ownership boundary: $forbidden" }
}
foreach ($required in @(
    'CleanupViewModel', 'ISettings2Repository', 'CleanupApplicationService', 'ICleanupEnvironment',
    'InitializeAsync', 'DetectAsync', 'SetSelectedPathAsync', 'PreviewAsync',
    'ExecuteConfirmedAsync', 'GetRunningRelatedProcesses', 'RequiresElevation',
    'Environment.GamePath', 'RecoveredLastKnownGood', 'RecoveryDefaults', 'UpdateAsync'
)) {
    if ($viewModel -notmatch [regex]::Escape($required)) { Fail "CleanupViewModel contract missing: $required" }
}
if ($viewModel -match '\.SaveAsync\s*\(') {
    Fail 'CleanupViewModel must use the atomic narrow Settings 2.0 mutation boundary, not whole-document SaveAsync.'
}

foreach ($id in @(
    'FACM.Cleanup.GamePath', 'FACM.Cleanup.Detect', 'FACM.Cleanup.Select',
    'FACM.Cleanup.Preview', 'FACM.Cleanup.Progress'
)) {
    if ($mainXaml -notmatch [regex]::Escape($id)) { Fail "Cleanup WinUI AutomationId missing: $id" }
}
if ($mainXaml -notmatch 'x:Name="CleanupPanel"') { Fail 'Main Shell cleanup panel is missing.' }
if ($mainXaml -match '#[0-9A-Fa-f]{6,8}') { Fail 'Cleanup WinUI must use semantic design resources, not hard-coded colors.' }

foreach ($forbidden in @(
    'Microsoft\.Win32', '\bFile\.', '\bDirectory\.', 'Process\.GetProcesses',
    'WindowsCleanupEngine', 'WindowsCleanupEnvironment', 'Directory\.Delete', 'File\.Delete'
)) {
    if ($mainCode -match $forbidden) { Fail "MainWindow cleanup presentation owns forbidden platform/delete behavior: $forbidden" }
}
foreach ($required in @(
    'CleanupViewModel', 'FolderPicker', 'ContentDialog', 'ShowCleanupReviewAsync',
    'CleanupConfirmTitle', 'CleanupConfirmPrimary', 'CleanupCancel',
    'CurrentPlan', 'DeletableTargets', 'BuildCleanupSummary', 'Summary', 'BlockedCount',
    'FormatCleanupTarget', 'IsBlocked', 'BlockedReason', 'RequiresElevation',
    'RestartElevatedForCleanup', 'Application.Current.Exit()', 'ExecuteConfirmedAsync(confirmed: true',
    'Progress<CleanupProgress>', 'CleanupPathText', 'CleanupOperationStatus'
)) {
    if ($mainCode -notmatch [regex]::Escape($required)) { Fail "MainWindow cleanup presentation missing: $required" }
}
if ($mainCode -notmatch '(?s)var\s+started\s*=\s*_cleanupCenter\.RestartElevatedForCleanup\(\);.*if\s*\(!started\).*return;.*Application\.Current\.Exit\(\)') {
    Fail 'Original non-elevated instance must exit only after elevated cleanup relaunch succeeds.'
}
if ($mainCode -match '(?s)OnCleanupPreviewClick.*File\.Delete|(?s)OnCleanupPreviewClick.*Directory\.Delete') {
    Fail 'Cleanup preview button must never directly delete filesystem entries.'
}

if ((Count-Matches $app 'new\s+WindowsCleanupEnvironment\s*\(') -ne 1) {
    Fail 'FACM.App must compose exactly one Windows cleanup environment.'
}
if ((Count-Matches $app 'new\s+WindowsCleanupEngine\s*\(') -ne 1) {
    Fail 'FACM.App must compose exactly one Windows cleanup engine.'
}
if ((Count-Matches $app 'new\s+CleanupViewModel\s*\(') -ne 1) {
    Fail 'FACM.App must compose exactly one CleanupViewModel.'
}
foreach ($required in @(
    'FeatureGatedCleanupExecutor', 'CleanupApplicationService', 'CleanupViewModel',
    'cleanupCenter', 'new MainWindow(controlCenter, cleanupCenter', '--cleanup'
)) {
    if ($app -notmatch [regex]::Escape($required)) { Fail "FACM.App cleanup composition missing: $required" }
}

foreach ($required in @(
    'unconfirmed cleanup must not delete data',
    'process guard must reject before deletion',
    'execution-time allowlist must reject forged target',
    'parent reparse guard must block preview through junction',
    'execution-time parent reparse guard must reject post-preview junction swap',
    'parent reparse guard must protect external data',
    'preserved DATA directory must survive execution',
    'non-log sibling must survive cleanup',
    'unrelated file must survive valid cleanup'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) { Fail "Cleanup Windows smoke missing assertion: $required" }
}

Write-Host 'Cleanup Core profile/confirmation boundary: OK'
Write-Host 'Cleanup Windows path/process/elevation boundary: OK'
Write-Host 'Cleanup allowlist/reparse/ancestor-chain execution-time revalidation: OK'
Write-Host 'Cleanup UAC handoff lifecycle: OK'
Write-Host 'Cleanup ViewModel Settings 2.0/recovery boundary: OK'
Write-Host 'Cleanup WinUI preview/confirm/progress presentation boundary: OK'
Write-Host 'Cleanup deterministic no-unrelated-delete smoke: OK'
Write-Host 'FACM 4.0 Cleanup contract: SUCCESS'
