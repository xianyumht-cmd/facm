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
$manifestPath = Join-Path $Root 'src/FACM.Infrastructure/Online/HttpUpdateManifestSource.cs'
$announcementPath = Join-Path $Root 'src/FACM.Infrastructure/Online/HttpAnnouncementSource.cs'
$viewModelPath = Join-Path $Root 'src/FACM.App/ViewModels/MaintenanceViewModel.cs'
$settingsPath = Join-Path $Root 'src/FACM.Core/Settings/Settings2.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MaintenanceSmoke.cs'
$smokeProgramPath = Join-Path $Root 'src/FACM.FoundationSmoke/Program.cs'

foreach ($path in @(
    $coreAnnouncementPath, $coreMaintenancePath, $manifestPath, $announcementPath,
    $viewModelPath, $settingsPath, $smokePath, $smokeProgramPath
)) {
    if (-not (Test-Path $path)) { Fail "P6 maintenance contract file missing: $path" }
}

$coreAnnouncement = Get-Content $coreAnnouncementPath -Raw
$coreMaintenance = Get-Content $coreMaintenancePath -Raw
$manifest = Get-Content $manifestPath -Raw
$announcement = Get-Content $announcementPath -Raw
$viewModel = Get-Content $viewModelPath -Raw
$settings = Get-Content $settingsPath -Raw
$smoke = Get-Content $smokePath -Raw
$smokeProgram = Get-Content $smokeProgramPath -Raw

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
    'MaintenanceApplicationService', 'InitializeAsync', 'SetAutoUpdateEnabledAsync',
    'ManualCheckAsync', 'RefreshAnnouncementAsync', 'MarkAnnouncementSeenAsync',
    'LoadedFromRecovery', 'UpdateAvailable', 'ForceUpdateRequired'
)) {
    Require-Text $viewModel $required "Maintenance ViewModel intent surface is missing: $required"
}
foreach ($forbidden in @(
    'HttpClient', 'HttpRequestMessage', 'Process\.Start', 'Registry',
    'File\.', 'Directory\.', 'Microsoft\.Win32', 'runas'
)) {
    if ($viewModel -match $forbidden) { Fail "Maintenance ViewModel owns forbidden platform/network behavior: $forbidden" }
}

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

Write-Host 'P6 Settings: AutoUpdateEnabled/LastAnnouncementId reuse Settings 2.0 ownership'
Write-Host 'P6 Manual check: explicit user check bypasses automatic-startup toggle'
Write-Host 'P6 Announcement: fixed GitHub raw origin, 128 KiB cap, 8s timeout, HTTPS detail only'
Write-Host 'P6 Recovery: load is read-only; explicit user changes may rebuild primary settings'
Write-Host 'P6 App boundary: no direct network/file/process/registry maintenance behavior in ViewModel'
Write-Host 'FACM 4.0 P6 maintenance foundation contract: SUCCESS'
