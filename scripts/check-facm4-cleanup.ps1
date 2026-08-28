param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

$coreContractsPath = Join-Path $Root 'src/FACM.Core/Cleanup/CleanupContracts.cs'
$coreProfilePath = Join-Path $Root 'src/FACM.Core/Cleanup/CleanupProfileContract.cs'
$environmentPath = Join-Path $Root 'src/FACM.Platform.Windows/Cleanup/WindowsCleanupEnvironment.cs'
$enginePath = Join-Path $Root 'src/FACM.Platform.Windows/Cleanup/WindowsCleanupEngine.cs'
$windowsSmokePath = Join-Path $Root 'src/FACM.WindowsSmoke/CleanupSmoke.cs'

foreach ($path in @($coreContractsPath, $coreProfilePath, $environmentPath, $enginePath, $windowsSmokePath)) {
    if (-not (Test-Path $path)) { Fail "Cleanup contract file missing: $path" }
}

$core = (Get-Content $coreContractsPath -Raw) + "`n" + (Get-Content $coreProfilePath -Raw)
$environment = Get-Content $environmentPath -Raw
$engine = Get-Content $enginePath -Raw
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
    'GetCursorPos' # sentinel replaced below to keep regex arrays uniform
)) { }

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
    'RevalidateTarget', 'EnsureNotReparsePoint', 'FileAttributes.ReparsePoint',
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

foreach ($required in @(
    'unconfirmed cleanup must not delete data',
    'process guard must reject before deletion',
    'execution-time allowlist must reject forged target',
    'preserved DATA directory must survive execution',
    'non-log sibling must survive cleanup',
    'unrelated file must survive valid cleanup'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) { Fail "Cleanup Windows smoke missing assertion: $required" }
}

Write-Host 'Cleanup Core profile/confirmation boundary: OK'
Write-Host 'Cleanup Windows path/process/elevation boundary: OK'
Write-Host 'Cleanup allowlist/reparse/execution-time revalidation: OK'
Write-Host 'Cleanup deterministic no-unrelated-delete smoke: OK'
Write-Host 'FACM 4.0 Cleanup contract: SUCCESS'
