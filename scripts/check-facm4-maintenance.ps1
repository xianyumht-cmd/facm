param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Require-Text([string]$Text, [string]$Needle, [string]$Message) {
    if ($Text -notmatch [regex]::Escape($Needle)) { Fail $Message }
}

$coreAnnouncementPath = Join-Path $Root 'src/FACM.Core/Online/AnnouncementContracts.cs'
$coreMaintenancePath = Join-Path $Root 'src/FACM.Core/Online/MaintenanceApplicationService.cs'
$corePlatformPath = Join-Path $Root 'src/FACM.Core/Maintenance/MaintenancePlatformContracts.cs'
$manifestPath = Join-Path $Root 'src/FACM.Infrastructure/Online/HttpUpdateManifestSource.cs'
$announcementPath = Join-Path $Root 'src/FACM.Infrastructure/Online/HttpAnnouncementSource.cs'
$singleInstancePath = Join-Path $Root 'src/FACM.Platform.Windows/Runtime/WindowsSingleInstanceGate.cs'
$logOpenerPath = Join-Path $Root 'src/FACM.Platform.Windows/Runtime/WindowsLogFileOpener.cs'
$viewModelPath = Join-Path $Root 'src/FACM.App/ViewModels/MaintenanceViewModel.cs'
$controlPath = Join-Path $Root 'src/FACM.App/MaintenanceSettingsControl.xaml.cs'
$windowPath = Join-Path $Root 'src/FACM.App/MainWindow.Maintenance.cs'
$settingsPath = Join-Path $Root 'src/FACM.Core/Settings/Settings2.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MaintenanceSmoke.cs'
$smokeProgramPath = Join-Path $Root 'src/FACM.FoundationSmoke/Program.cs'
$windowsSmokePath = Join-Path $Root 'src/FACM.WindowsSmoke/MaintenanceWindowsSmoke.cs'
$windowsSmokeProgramPath = Join-Path $Root 'src/FACM.WindowsSmoke/Program.cs'

foreach ($path in @(
    $coreAnnouncementPath, $coreMaintenancePath, $corePlatformPath, $manifestPath, $announcementPath,
    $singleInstancePath, $logOpenerPath, $viewModelPath, $controlPath, $windowPath, $settingsPath,
    $smokePath, $smokeProgramPath, $windowsSmokePath, $windowsSmokeProgramPath
)) {
    if (-not (Test-Path $path)) { Fail "P6 maintenance contract file missing: $path" }
}

$coreAnnouncement = Get-Content $coreAnnouncementPath -Raw
$coreMaintenance = Get-Content $coreMaintenancePath -Raw
$corePlatform = Get-Content $corePlatformPath -Raw
$manifest = Get-Content $manifestPath -Raw
$announcement = Get-Content $announcementPath -Raw
$singleInstance = Get-Content $singleInstancePath -Raw
$logOpener = Get-Content $logOpenerPath -Raw
$viewModel = Get-Content $viewModelPath -Raw
$control = Get-Content $controlPath -Raw
$window = Get-Content $windowPath -Raw
$settings = Get-Content $settingsPath -Raw
$smoke = Get-Content $smokePath -Raw
$smokeProgram = Get-Content $smokeProgramPath -Raw
$windowsSmoke = Get-Content $windowsSmokePath -Raw
$windowsSmokeProgram = Get-Content $windowsSmokeProgramPath -Raw

foreach ($required in @(
    'AnnouncementSnapshot', 'IAnnouncementSource', 'OnlineUriPolicy',
    'NormalizeAbsoluteHttps', 'Uri.UriSchemeHttps', '!uri.IsLoopback'
)) {
    Require-Text $coreAnnouncement $required "Announcement Core contract is missing: $required"
}
foreach ($forbidden in @('HttpClient', 'HttpRequestMessage', 'Process\.', 'Microsoft\.UI', 'System\.Drawing')) {
    if ($coreAnnouncement -match $forbidden) { Fail "Announcement Core leaked transport/UI detail: $forbidden" }
}

foreach ($required in @(
    'MaintenancePreferences', 'MaintenanceCheckResult', 'MaintenanceApplicationService',
    'LoadPreferencesAsync', 'SetAutoUpdateEnabledAsync', 'CheckNowAsync',
    'GetAnnouncementAsync', 'MarkAnnouncementSeenAsync',
    'SettingsLoadOrigin.RecoveredLastKnownGood', 'SettingsLoadOrigin.RecoveryDefaults',
    '_updates.GetAsync', 'UpdateDecisionService.Evaluate'
)) {
    Require-Text $coreMaintenance $required "Maintenance Core intent is missing: $required"
}
if ($coreMaintenance -match 'CheckNowAsync[\s\S]{0,1200}AutoUpdateEnabled') {
    Fail 'Manual CheckNowAsync must not be gated by AutoUpdateEnabled.'
}
foreach ($forbidden in @('HttpClient', 'HttpRequestMessage', 'Process\.', 'Microsoft\.UI', 'File\.', 'Directory\.')) {
    if ($coreMaintenance -match $forbidden) { Fail "Maintenance Core crossed platform boundary: $forbidden" }
}

foreach ($required in @('ILogFileOpener', 'LogOpenResult', 'ISingleInstanceGate', 'SingleInstanceDisposition', 'EnterNormal')) {
    Require-Text $corePlatform $required "Maintenance platform Core contract is missing: $required"
}
foreach ($forbidden in @('System\.Threading\.Mutex', 'EventWaitHandle', 'Process\.Start', 'File\.', 'Directory\.', 'Microsoft\.UI')) {
    if ($corePlatform -match $forbidden) { Fail "Maintenance Core platform contract leaked Windows implementation: $forbidden" }
}

foreach ($required in @(
    'ProductionAnnouncementUri',
    'https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/announcement.json',
    'DefaultMaxMetadataBytes = 128 * 1024', 'DefaultTimeout = TimeSpan.FromSeconds(8)',
    'HttpCompletionOption.ResponseHeadersRead', 'OnlineUriPolicy.NormalizeAbsoluteHttpsString'
)) {
    Require-Text $announcement $required "Announcement fixed-origin adapter is missing: $required"
}
if ($announcement -match 'public\s+HttpAnnouncementSource\s*\([^)]*Uri') {
    Fail 'Announcement public constructor must not accept an arbitrary URI.'
}

foreach ($required in @(
    'ProductionManifestUri', 'DefaultMaxMetadataBytes = 128 * 1024',
    'Uri.UriSchemeHttps', 'github.com', '/xianyumht-cmd/facm/releases/download/v',
    'manifest.Sha256.Length != 64', 'TimeSpan.FromSeconds(7)'
)) {
    Require-Text $manifest $required "Existing update manifest security contract regressed: $required"
}

foreach ($required in @(
    'DefaultMutexName = @"Local\FACM-2C429A53-6710-48BC-A57C-32BEA688B25D"',
    'DefaultActivationEventName = @"Local\FACM-Activate-2C429A53-6710-48BC-A57C-32BEA688B25D"',
    'DefaultSignalTimeout = TimeSpan.FromMilliseconds(1600)',
    'new Mutex(initiallyOwned: true', 'EventResetMode.AutoReset', 'ThreadPool.RegisterWaitForSingleObject',
    'TrySignalExisting', 'WaitHandleCannotBeOpenedException', 'ExistingUnresponsive'
)) {
    Require-Text $singleInstance $required "Windows single-instance parity is missing: $required"
}
foreach ($forbidden in @('Process\.GetProcesses', 'Process\.Kill', 'MainWindowTitle', 'FindWindow', 'GetProcessesByName')) {
    if ($singleInstance -match $forbidden) { Fail "Single-instance implementation used forbidden takeover/window/process discovery: $forbidden" }
}

foreach ($required in @(
    'WindowsLogFileOpener', 'LogFileName = "facm4-events.jsonl"', 'layout.LogsDirectory',
    'Directory.CreateDirectory', 'FileMode.CreateNew', 'UseShellExecute = true', 'LogOpenResult'
)) {
    Require-Text $logOpener $required "Controlled Windows log opener is missing: $required"
}
if ($logOpener -match 'public\s+WindowsLogFileOpener\s*\([^)]*string') {
    Fail 'Log opener public surface must not accept an arbitrary path.'
}

foreach ($required in @(
    'MaintenanceApplicationService', 'InitializeAsync', 'SetAutoUpdateEnabledAsync',
    'ManualCheckAsync', 'RefreshAnnouncementAsync', 'MarkAnnouncementSeenAsync',
    'LoadedFromRecovery', 'UpdateAvailable', 'ForceUpdateRequired',
    'Status = "initialization-failed"', 'ReferenceEquals(_downloadCancellation, downloadCancellation)',
    'EnterInstallerOperation()', 'ExitInstallerOperation()', 'DisposeInstallerOnce()',
    'Volatile.Read(ref _activeInstallerOperations)'
)) {
    Require-Text $viewModel $required "Maintenance ViewModel intent/lifecycle surface is missing: $required"
}
if ($viewModel -notmatch '(?s)InitializeAsync\(.*?ApplyPreferences\(await _service\.LoadPreferencesAsync.*?_initialized\s*=\s*true;') {
    Fail 'Maintenance initialization may mark success only after preferences load succeeds.'
}
if ($viewModel -match '(?s)InitializeAsync\(.*?finally\s*\{[^}]*_initialized\s*=\s*true') {
    Fail 'Maintenance initialization failure/cancellation must not permanently latch IsInitialized=true.'
}
if ($viewModel -notmatch '(?s)PrepareUpdateAsync\(.*?_downloadCancellation\s*=\s*downloadCancellation.*?finally.*?downloadCancellation\.Dispose\(\)') {
    Fail 'Maintenance download operation must own and dispose its CTS only after the installer await unwinds.'
}
if ($viewModel -notmatch '(?s)Dispose\(\).*?downloadCancellation\?\.Cancel\(\).*?Volatile\.Read\(ref _activeInstallerOperations\)') {
    Fail 'Maintenance teardown must cancel active download work and defer installer disposal until operations unwind.'
}
foreach ($forbidden in @(
    'HttpClient', 'HttpRequestMessage', 'Process\.Start', 'Registry',
    'File\.', 'Directory\.', 'Microsoft\.Win32', 'runas'
)) {
    if ($viewModel -match $forbidden) { Fail "Maintenance ViewModel owns forbidden platform/network behavior: $forbidden" }
}

foreach ($required in @(
    'RetryInitialization()', '_initializationInFlight', 'InitializeCoreAsync',
    'if (viewModel.IsInitialized)', 'catch (OperationCanceledException)'
)) {
    Require-Text $control $required "Maintenance WinUI retry/async containment missing: $required"
}
foreach ($handler in @(
    'OnAutoUpdateToggled', 'OnCheckNowClick', 'OnDownloadClick', 'OnInstallClick',
    'OnAnnouncementDetailClick', 'OnOpenLogClick'
)) {
    if ($control -notmatch ('(?s)' + [regex]::Escape($handler) + '.*?catch')) {
        Fail "Maintenance async-void handler is missing failure containment: $handler"
    }
}
Require-Text $window '_maintenanceControl?.RetryInitialization()' 'Entering More Settings must retry a transiently failed maintenance initialization.'

foreach ($forbidden in @('StartupEnabled', 'RunRegistry', 'StartupFolder', 'StartWithWindows', 'LaunchAtStartup')) {
    if ($settings -match $forbidden -or $viewModel -match $forbidden -or $coreMaintenance -match $forbidden) {
        Fail "P6 invented a startup-setting that does not exist in the 3.5.15 parity baseline: $forbidden"
    }
}
Require-Text $settings 'public bool AutoUpdateEnabled { get; set; } = true' 'AutoUpdate default true was lost.'
Require-Text $settings 'public string LastAnnouncementId { get; set; } = string.Empty' 'LastAnnouncementId ownership was lost.'

foreach ($required in @(
    'ValidateRecoveryLoadDoesNotSaveAsync', 'SaveCalls == 0',
    'ValidateExplicitToggleRepairsPrimaryAsync', 'SaveCalls == 1',
    'ValidateManualCheckIgnoresAutoToggleAsync', 'updates.Calls == 1',
    'ValidateAnnouncementHttpsPolicyAsync', 'http://example.com/details', 'https://localhost/details',
    'HttpAnnouncementSource.ProductionAnnouncementUri'
)) {
    Require-Text $smoke $required "P6 maintenance deterministic smoke is missing: $required"
}
if (@([regex]::Matches($smokeProgram, 'MaintenanceSmoke\.RunAsync')).Count -ne 1) {
    Fail 'Foundation smoke must register MaintenanceSmoke exactly once.'
}

foreach ($required in @(
    'ValidateSingleInstanceActivation', 'ExistingSignaled', 'callbackCount) == 1',
    'ValidateMissingActivationListenerIsBounded', 'ExistingUnresponsive',
    'ValidateControlledLogOpenAsync', 'WindowsLogFileOpener.LogFileName', 'shellCalls == 1'
)) {
    Require-Text $windowsSmoke $required "P6 Windows maintenance smoke is missing: $required"
}
if (@([regex]::Matches($windowsSmokeProgram, 'MaintenanceWindowsSmoke\.RunAsync')).Count -ne 1) {
    Fail 'Windows smoke must register MaintenanceWindowsSmoke exactly once.'
}

Write-Host 'P6 Settings: AutoUpdateEnabled/LastAnnouncementId reuse Settings 2.0 ownership'
Write-Host 'P6 Manual check: explicit user check bypasses automatic-startup toggle'
Write-Host 'P6 Announcement: fixed GitHub raw origin, 128 KiB cap, 8s timeout, HTTPS detail only'
Write-Host 'P6 Recovery: load is read-only; explicit user changes may rebuild primary settings'
Write-Host 'P6 Single instance: legacy mutex/event names + 1600ms bounded signal + AutoReset callback'
Write-Host 'P6 Log opener: controlled runtime/logs/facm4-events.jsonl + separable Windows Shell launch'
Write-Host 'P7 Maintenance hardening: retryable initialization + async-void containment + deferred installer/CTS teardown'
Write-Host 'P6 App boundary: no direct network/file/process/registry maintenance behavior in ViewModel'
Write-Host 'FACM 4.0 P6 maintenance foundation contract: SUCCESS'
