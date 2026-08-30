param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

$coreCatalogPath = Join-Path $Root 'src/FACM.Core/Personalization/PersonalizationCatalog.cs'
$coreContractsPath = Join-Path $Root 'src/FACM.Core/Personalization/PersonalizationContracts.cs'
$preferencePath = Join-Path $Root 'src/FACM.Core/Personalization/DesktopPetPreferenceService.cs'
$settingsPath = Join-Path $Root 'src/FACM.Core/Settings/Settings2.cs'
$viewModelPath = Join-Path $Root 'src/FACM.App/ViewModels/PersonalizationViewModel.cs'
$runtimePath = Join-Path $Root 'src/FACM.App/Personalization/WinUiThemeRuntime.cs'
$surfacePath = Join-Path $Root 'src/FACM.App/MainWindow.Personalization.cs'
$stateSyncPath = Join-Path $Root 'src/FACM.App/MainWindow.PersonalizationStateSync.cs'
$appPersonalizationPath = Join-Path $Root 'src/FACM.App/App.Personalization.cs'
$appProjectPath = Join-Path $Root 'src/FACM.App/FACM.App.csproj'
$controlCenterPath = Join-Path $Root 'src/FACM.App/ViewModels/ControlCenterViewModel.cs'
$petBundleStorePath = Join-Path $Root 'src/FACM.Platform.Windows/Personalization/WindowsPetHostBundleStore.cs'
$flyingBundleStorePath = Join-Path $Root 'src/FACM.Platform.Windows/Personalization/WindowsFlyingHostBundleStore.cs'
$vpetRuntimePath = Join-Path $Root 'src/FACM.Platform.Windows/Personalization/WindowsVPetRuntime.cs'
$flyingRuntimePath = Join-Path $Root 'src/FACM.Platform.Windows/Personalization/WindowsFlyingPetRuntime.cs'
$routerPath = Join-Path $Root 'src/FACM.Platform.Windows/Personalization/WindowsDesktopPetRuntimeRouter.cs'
$jobPath = Join-Path $Root 'src/FACM.Platform.Windows/Personalization/WindowsChildProcessJob.cs'
$petHostProgramPath = Join-Path $Root 'src/FACM.PetHost/Program.cs'
$petHostWindowPath = Join-Path $Root 'src/FACM.PetHost/PetHostWindow.cs'
$flyingHostProgramPath = Join-Path $Root 'src/FACM.FlyingHost/Program.cs'
$flyingHostProjectPath = Join-Path $Root 'src/FACM.FlyingHost/FACM.FlyingHost.csproj'
$flyingProfilesPath = Join-Path $Root 'src/FACM.FlyingHost/FlyingPetProfiles.cs'
$flyingWindowPath = Join-Path $Root 'src/FACM.FlyingHost/FlyingPetHostWindow.cs'
$ipcSmokePath = Join-Path $Root 'src/FACM.WindowsSmoke/DesktopPetIpcLifecycleSmoke.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/PersonalizationSmoke.cs'
$foundationProgramPath = Join-Path $Root 'src/FACM.FoundationSmoke/Program.cs'
$petWindowsSmokePath = Join-Path $Root 'src/FACM.WindowsSmoke/PetHostBundleSmoke.cs'
$flyingWindowsSmokePath = Join-Path $Root 'src/FACM.WindowsSmoke/FlyingHostBundleSmoke.cs'
$windowsSmokeProgramPath = Join-Path $Root 'src/FACM.WindowsSmoke/Program.cs'
$workflowPath = Join-Path $Root '.github/workflows/facm4-foundation.yml'
$solutionPath = Join-Path $Root 'FACM4.sln'

foreach ($path in @(
    $coreCatalogPath, $coreContractsPath, $preferencePath, $settingsPath, $viewModelPath, $runtimePath,
    $surfacePath, $stateSyncPath, $appPersonalizationPath, $appProjectPath, $controlCenterPath,
    $petBundleStorePath, $flyingBundleStorePath, $vpetRuntimePath, $flyingRuntimePath, $routerPath, $jobPath,
    $petHostProgramPath, $petHostWindowPath, $flyingHostProgramPath, $flyingHostProjectPath, $flyingProfilesPath, $flyingWindowPath,
    $smokePath, $foundationProgramPath, $petWindowsSmokePath, $flyingWindowsSmokePath, $ipcSmokePath, $windowsSmokeProgramPath,
    $workflowPath, $solutionPath
)) {
    if (-not (Test-Path $path)) { Fail "Personalization contract file missing: $path" }
}

$core = (Get-Content $coreCatalogPath -Raw) + "`n" + (Get-Content $coreContractsPath -Raw)
$preference = Get-Content $preferencePath -Raw
$settings = Get-Content $settingsPath -Raw
$viewModel = Get-Content $viewModelPath -Raw
$runtime = Get-Content $runtimePath -Raw
$surface = Get-Content $surfacePath -Raw
$stateSync = Get-Content $stateSyncPath -Raw
$appPersonalization = Get-Content $appPersonalizationPath -Raw
$appProject = Get-Content $appProjectPath -Raw
$controlCenter = Get-Content $controlCenterPath -Raw
$petBundleStore = Get-Content $petBundleStorePath -Raw
$flyingBundleStore = Get-Content $flyingBundleStorePath -Raw
$vpetRuntime = Get-Content $vpetRuntimePath -Raw
$flyingRuntime = Get-Content $flyingRuntimePath -Raw
$router = Get-Content $routerPath -Raw
$job = Get-Content $jobPath -Raw
$petHostProgram = Get-Content $petHostProgramPath -Raw
$petHostWindow = Get-Content $petHostWindowPath -Raw
$flyingHostProgram = Get-Content $flyingHostProgramPath -Raw
$flyingHostProject = Get-Content $flyingHostProjectPath -Raw
$flyingProfiles = Get-Content $flyingProfilesPath -Raw
$flyingWindow = Get-Content $flyingWindowPath -Raw
$ipcSmoke = Get-Content $ipcSmokePath -Raw
$smoke = Get-Content $smokePath -Raw
$foundationProgram = Get-Content $foundationProgramPath -Raw
$petWindowsSmoke = Get-Content $petWindowsSmokePath -Raw
$flyingWindowsSmoke = Get-Content $flyingWindowsSmokePath -Raw
$windowsSmokeProgram = Get-Content $windowsSmokeProgramPath -Raw
$workflow = Get-Content $workflowPath -Raw
$solution = Get-Content $solutionPath -Raw

foreach ($forbidden in @('Microsoft\.UI', 'Windows\.UI', 'System\.Windows\.Forms', 'System\.Drawing', 'System\.Diagnostics', 'DllImport', 'LibraryImport')) {
    if ($core -match $forbidden) { Fail "Core personalization contract crossed platform/UI boundary: $forbidden" }
}

$themeIds = @(
    'glass-blue', 'obsidian-gold', 'neon-cyber', 'cloud-light', 'brutalist-grid',
    'holo-spectrum', 'mono-emerald', 'rgb-tactical', 'aurora-night', 'sunset-synthwave'
)
foreach ($id in $themeIds) {
    if ($core -notmatch ('"' + [regex]::Escape($id) + '"')) { Fail "Stable FACM theme missing: $id" }
}
if ($core -notmatch 'DefaultThemeId\s*=\s*"glass-blue"') { Fail 'Theme default must stay glass-blue.' }

$petIds = @('greenfly', 'bee', 'real-bee', 'dragonfly', 'butterfly', 'moth', 'vpet', 'cat', 'dog', 'spider', 'ant', 'greyfly', 'wasp', 'bird')
foreach ($id in $petIds) {
    if ($core -notmatch ('"' + [regex]::Escape($id) + '"')) { Fail "Stable FACM pet compatibility id missing: $id" }
}
if ($core -notmatch 'DefaultPetId\s*=\s*"greenfly"') { Fail 'Pet default must stay greenfly.' }
foreach ($required in @('FacmPetRuntimeKind.FlyingSprite', 'FacmPetRuntimeKind.VPetCore', 'FacmPetRuntimeKind.LegacyCompatibility', 'IDesktopPetRuntime')) {
    if ($core -notmatch [regex]::Escape($required)) { Fail "Desktop pet compatibility boundary missing: $required" }
}

foreach ($required in @('FacmThemeCatalog.Contains', 'FacmPetCatalog.Contains')) {
    if ($settings -notmatch [regex]::Escape($required)) { Fail "Settings 2.0 does not share personalization catalog: $required" }
}
if ($settings -match 'KnownThemeIds|KnownPetIds') { Fail 'Settings 2.0 must not maintain a second theme/pet whitelist.' }

foreach ($forbidden in @('FACM\.Infrastructure', 'FACM\.Platform\.Windows', 'Microsoft\.UI', 'Windows\.UI', 'System\.IO', 'System\.Diagnostics', 'HttpClient', '\bFile\.', '\bDirectory\.')) {
    if ($viewModel -match $forbidden) { Fail "PersonalizationViewModel crossed Core state/intent boundary: $forbidden" }
}
foreach ($required in @(
    'ISettings2Repository', 'IFacmThemeRuntime', 'InitializeForStartup', 'SelectThemeAsync', 'SelectPetAsync',
    'InitializeDesktopPetAsync', 'EnableSelectedPetAsync', 'RestoreDefaultLauncherAsync', 'ResetDesktopPositionAsync',
    'RecoveredLastKnownGood', 'RecoveryDefaults', 'Appearance.ThemeId', 'Pets.StyleId', 'Pets.Enabled', 'UpdateAsync'
)) {
    if ($viewModel -notmatch [regex]::Escape($required)) { Fail "PersonalizationViewModel behavior missing: $required" }
}
if ($viewModel -match '\.SaveAsync\s*\(') { Fail 'PersonalizationViewModel must use narrow Settings 2.0 mutation boundary.' }
if ($viewModel -match 'Pets\.Enabled\s*=\s*true') { Fail 'Pet style selection must not silently enable desktop pet mode.' }

foreach ($required in @('DesktopPetPreferenceService', 'Pets.Enabled = true', 'Pets.Enabled = false', 'ApplyAsync(true', 'ApplyAsync(false', 'RecoveryDefaults', 'ResetPositionAsync', 'UpdateAsync')) {
    if ($preference -notmatch [regex]::Escape($required)) { Fail "Desktop pet settings/runtime coordinator missing: $required" }
}
if ($preference -match '\.SaveAsync\s*\(') { Fail 'DesktopPetPreferenceService must not reintroduce whole-document Settings2 saves.' }

foreach ($required in @('WinUiThemeRuntime', 'AccessibilitySettings', 'HighContrast', 'FacmBackgroundBrush', 'FacmSurfaceBrush', 'FacmTextPrimaryBrush', 'FacmAccentBrush', 'FacmStrokeBrush')) {
    if ($runtime -notmatch [regex]::Escape($required)) { Fail "WinUI theme runtime behavior missing: $required" }
}
if ($runtime -match '\bFile\.|\bDirectory\.|Settings2Repository') { Fail 'WinUI theme runtime must not own settings or filesystem access.' }

foreach ($required in @(
    'FACM.Personalization.ThemePicker', 'FACM.Personalization.PetPicker', 'DisplayMemberPath',
    'SelectThemeAsync', 'SelectPetAsync', 'ConfigurePersonalization', 'OnPersonalizationNavigationChanged',
    'CreatePersonalizationViewModel', 'InitializeDesktopPetAfterLauncherReadyAsync',
    'FACM.Personalization.EnablePet', 'FACM.Personalization.RestoreLauncher', 'FACM.Personalization.ResetDesktopPosition',
    'EnableSelectedPetAsync', 'RestoreDefaultLauncherAsync', 'ResetDesktopPositionAsync'
)) {
    if ($surface -notmatch [regex]::Escape($required)) { Fail "Personalization Shell surface missing: $required" }
}
if ($surface -match 'System\.Diagnostics|HttpClient|\bFile\.|\bDirectory\.') { Fail 'Personalization Shell presentation owns platform/data access.' }
foreach ($required in @('OnPersonalizationViewModelPropertyChanged', 'DispatcherQueue.TryEnqueue', 'ApplyPersonalizationBusyStatus', '正在处理，请稍候')) {
    if ($stateSync -notmatch [regex]::Escape($required)) { Fail "Personalization async state refresh/busy feedback missing: $required" }
}
if ($controlCenter -notmatch 'CreatePersonalization') { Fail 'Existing Settings 2.0 owner must compose the personalization ViewModel.' }

foreach ($required in @(
    'CreatePersonalizationViewModel', 'WinUiThemeRuntime',
    'WindowsPetHostBundleStore', 'WindowsFlyingHostBundleStore',
    'WindowsVPetRuntime', 'WindowsFlyingPetRuntime', 'WindowsDesktopPetRuntimeRouter',
    'GetManifestResourceStream', 'ConfigureDesktopPetService', 'InitializeDesktopPetAfterLauncherReadyAsync',
    'SetDesktopEntryVisible', 'ResetFloatingEntryPositionAsync', 'DisposePersonalizationRuntime',
    'ReadPetHostBundleSha256', 'ReadFlyingHostBundleSha256', 'ParseDesktopPetRuntimeStage', 'rawStage'
)) {
    if ($appPersonalization -notmatch [regex]::Escape($required)) { Fail "App personalization composition missing: $required" }
}
foreach ($required in @('personalization.pet-bundle', 'personalization.flying-bundle', 'personalization.pet-host', 'personalization.flying-host')) {
    if ($appPersonalization -notmatch [regex]::Escape($required)) { Fail "Split desktop-pet diagnostics missing: $required" }
}

foreach ($required in @(
    'FACM.Resources.PetHost.zip', 'FACM.Resources.PetHost.sha256', 'PetHostBundlePath', 'PetHostBundleHashPath', 'RequirePetHostBundle',
    'FACM.Resources.FlyingHost.zip', 'FACM.Resources.FlyingHost.sha256', 'FlyingHostBundlePath', 'FlyingHostBundleHashPath', 'RequireFlyingHostBundle',
    'EmbeddedResource'
)) {
    if ($appProject -notmatch [regex]::Escape($required)) { Fail "FACM.App controlled desktop-pet embedding missing: $required" }
}

foreach ($required in @(
    'WindowsPetHostBundleStore', 'SHA256.HashData', 'ZipArchive', 'pethost-host', 'partial-', 'path traversal',
    'CriticalPayloadFiles', 'PrepareTimeout', '_cachedPreparation', '_expectedBundleSha256', 'NormalizeBundleSha256'
)) {
    if ($petBundleStore -notmatch [regex]::Escape($required)) { Fail "Controlled VPet PetHost bundle store missing: $required" }
}
foreach ($required in @(
    'WindowsFlyingHostBundleStore', 'SHA256.HashData', 'ZipArchive', 'flying-host', 'partial-', 'path traversal',
    'CriticalPayloadFiles', 'PrepareTimeout', '_cachedPreparation', '_expectedBundleSha256', 'NormalizeBundleSha256',
    'FACM.FlyingHost.exe'
)) {
    if ($flyingBundleStore -notmatch [regex]::Escape($required)) { Fail "Controlled FlyingHost bundle store missing: $required" }
}
if ($flyingBundleStore -match 'VPet-Simulator.Core') { Fail 'FlyingHost bundle store must not depend on VPet payload identity.' }

foreach ($required in @(
    'IDesktopPetRuntime', 'NamedPipeClientStream', 'WindowsChildProcessJob.TryAssign', 'activate|', 'event|',
    'ready', 'runtime-failed', 'SetLauncherVisible(false)', 'SetLauncherVisible(true)',
    'VPetCore', '--pet-id', 'runtime-unsupported', 'HostReadyTimeout', 'host-ready-timeout', '_lifetime',
    'SendCommandAsync', 'WriteLineAsync(command.AsMemory(), cancellationToken)', 'FlushAsync(cancellationToken)',
    'transport-poisoned', 'stop-send-skipped-poisoned', 'process-wait-start', 'process-kill-start',
    'transport-dispose-start'
)) {
    if ($vpetRuntime -notmatch [regex]::Escape($required)) { Fail "Controlled VPet runtime missing: $required" }
}
foreach ($required in @(
    'IDesktopPetRuntime', 'WindowsFlyingHostBundleStore', 'NamedPipeClientStream', 'WindowsChildProcessJob.TryAssign',
    'FacmPetRuntimeKind.FlyingSprite', 'FACM.FlyingHost.', '--pet-id', 'flying-payload-preparing',
    'flying-process-start-timeout', 'flying-host-ready-timeout', 'SetLauncherVisible(false)', 'SetLauncherVisible(true)',
    'SendCommandAsync', 'WriteLineAsync(command.AsMemory(), cancellationToken)', 'FlushAsync(cancellationToken)',
    'flying-transport-poisoned', 'flying-stop-send-skipped-poisoned', 'flying-process-wait-start',
    'flying-process-kill-start', 'flying-transport-dispose-start'
)) {
    if ($flyingRuntime -notmatch [regex]::Escape($required)) { Fail "Controlled Flying Sprite runtime missing: $required" }
}
foreach ($required in @(
    'WindowsDesktopPetRuntimeRouter', 'WindowsFlyingPetRuntime', 'WindowsVPetRuntime',
    'FacmPetRuntimeKind.FlyingSprite', 'FacmPetRuntimeKind.VPetCore', 'ApplyAsync(false', 'ResetPositionAsync'
)) {
    if ($router -notmatch [regex]::Escape($required)) { Fail "Desktop pet runtime router missing: $required" }
}

foreach ($required in @('JobObjectLimitKillOnJobClose', 'AssignProcessToJobObject', 'SetInformationJobObject')) {
    if ($job -notmatch [regex]::Escape($required)) { Fail "Desktop pet Job Object containment missing: $required" }
}

foreach ($required in @('PetHostWindow', 'VPetAssetCacheValidator', 'VPet_Simulator.Core', '--pipe', '--parent-pid', '--self-test')) {
    if ($petHostProgram -notmatch [regex]::Escape($required)) { Fail "VPet PetHost entry missing: $required" }
}
if ($petHostProgram -match 'FlyingPetHostWindow|FlyingPetProfiles') { Fail 'VPet PetHost must not own Flying Sprite entry/runtime.' }
if ($petHostProgram -match 'window\.Show\(\)') { Fail 'VPet PetHost must not show before activate.' }
foreach ($required in @('_activated', 'if \(!_activated\) return;', 'HandleCommandOnDispatcherAsync', 'SendEventAsync\("stage"')) {
    if ($petHostWindow -notmatch $required) { Fail "VPet PetHost activation lifecycle missing: $required" }
}
foreach ($required in @('FlyingPetHostWindow', '--pet-id', '--pipe', '--parent-pid', '--self-test', 'FlyingHostSelfTest')) {
    if ($flyingHostProgram -notmatch [regex]::Escape($required)) { Fail "FlyingHost entry missing: $required" }
}
if ($flyingHostProgram -match 'VPetAssetCacheValidator|VPet_Simulator') { Fail 'FlyingHost entry must not own VPet runtime/cache.' }
if ($flyingHostProgram -match 'window\.Show\(\)') { Fail 'FlyingHost must not show before activate.' }
foreach ($required in @('_activated', 'if \(!_activated\) return;', 'HandleCommandOnDispatcherAsync', 'SendEventAsync\("stage"')) {
    if ($flyingWindow -notmatch $required) { Fail "FlyingHost activation lifecycle missing: $required" }
}
foreach ($required in @('<AssemblyName>FACM.FlyingHost</AssemblyName>', '<UseWPF>true</UseWPF>', '<UseWindowsForms>true</UseWindowsForms>')) {
    if ($flyingHostProject -notmatch [regex]::Escape($required)) { Fail "FlyingHost project contract missing: $required" }
}
if ($flyingHostProject -match 'VPet-Simulator|VPet_Simulator') { Fail 'FACM.FlyingHost project must not reference VPet.' }

foreach ($id in @('greenfly', 'bee', 'real-bee', 'dragonfly', 'butterfly', 'moth')) {
    if ($flyingProfiles -notmatch ('"' + [regex]::Escape($id) + '"')) { Fail "FlyingHost profile missing: $id" }
}
foreach ($required in @('82, 140', '7.5, 10.5', '48, 82', '120, 205', '18, 38', '36, 68')) {
    if ($flyingProfiles -notmatch [regex]::Escape($required)) { Fail "Frozen 3.5 flying behavior baseline missing: $required" }
}
foreach ($required in @(
    'DispatcherTimer', 'Interval = TimeSpan.FromMilliseconds(16)', 'Math.Abs(dx) + Math.Abs(dy) > 4',
    'JitterXFrequency', 'JitterYFrequency', 'roams freely', 'Mouse.Capture',
    'SendEventAsync("ready"', 'SendEventAsync("click"', 'SendEventAsync("right-click"',
    'case "reset"', 'AllowsTransparency = true', 'WsExToolWindow', 'WsExNoActivate'
)) {
    if ($flyingWindow -notmatch [regex]::Escape($required)) { Fail "FlyingHost behavior missing: $required" }
}

foreach ($required in @(
    'VerifyActivateHandshakeOrderAsync', 'VerifyCancellationAwareCommandWriteAsync',
    'VerifyStopSendFailureIsFailSoftAsync',
    'VerifySequentialHostSessionsAsync', 'event|stage|show', 'event|stage|loaded',
    'timed-out IPC command write left a pending task', 'maximum active host count'
)) {
    if ($ipcSmoke -notmatch [regex]::Escape($required)) { Fail "Desktop-pet IPC lifecycle smoke missing: $required" }
}
if ($windowsSmokeProgram -notmatch 'DesktopPetIpcLifecycleSmoke\.RunAsync') { Fail 'Windows smoke runner does not execute desktop-pet IPC lifecycle smoke.' }

foreach ($required in @(
    'Prepare controlled FlyingHost payload', 'FACM.FlyingHost/FACM.FlyingHost.csproj', 'FACM.FlyingHost.exe',
    'FlyingHostBundle.zip', 'FlyingHostBundle.sha256', 'VPet-Simulator.Core.dll', 'RequireFlyingHostBundle=true',
    'Prepare controlled VPet PetHost payload', 'FACM.PetHost/FACM.PetHost.csproj', 'FACM.PetHost.exe',
    'PetHostBundle.zip', 'PetHostBundle.sha256', 'RequirePetHostBundle=true', 'Compress-Archive'
)) {
    if ($workflow -notmatch [regex]::Escape($required)) { Fail "Foundation workflow split desktop-pet packaging missing: $required" }
}

foreach ($required in @(
    'WindowsPetHostBundleStore', 'CacheHit', 'RejectsPathTraversalAsync', 'BundleSha256',
    'ReusesDiskCacheAcrossProcessBoundaryWithoutOpeningBundleAsync',
    'cross-process cache hit must not reopen the embedded bundle'
)) {
    if ($petWindowsSmoke -notmatch [regex]::Escape($required)) { Fail "Windows VPet PetHost smoke missing: $required" }
}
foreach ($required in @(
    'WindowsFlyingHostBundleStore', 'WindowsFlyingPetRuntime', 'CacheHit', 'RejectsPathTraversalAsync', 'BundleSha256',
    'ReusesDiskCacheAcrossProcessBoundaryWithoutOpeningBundleAsync',
    'cross-process FlyingHost cache hit must not reopen the embedded bundle',
    'flying-process-start-timeout'
)) {
    if ($flyingWindowsSmoke -notmatch [regex]::Escape($required)) { Fail "Windows FlyingHost smoke missing: $required" }
}
foreach ($required in @('FlyingHostBundleSmoke.RunAsync()', 'PetHostBundleSmoke.RunAsync()')) {
    if ($windowsSmokeProgram -notmatch [regex]::Escape($required)) { Fail "Windows smoke runner missing: $required" }
}
if ($solution -notmatch [regex]::Escape('FACM.FlyingHost')) { Fail 'FACM4.sln does not include FACM.FlyingHost.' }

foreach ($required in @(
    'stable theme count', 'unknown theme fallback', 'unique theme ids',
    'stable default pet id', 'unknown pet fallback', 'visible VPet Core route',
    'legacy pet id compatibility', 'new installs must not auto-enable desktop pet',
    'unsupported theme rejection', 'unsupported pet rejection'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) { Fail "Personalization smoke missing assertion: $required" }
}
if ($foundationProgram -notmatch 'PersonalizationSmoke\.Run\(\)') { Fail 'Foundation smoke runner does not execute personalization smoke.' }

Write-Host 'Personalization stable theme/pet catalogs: OK'
Write-Host 'Settings 2.0 shared catalog + atomic mutation ownership: OK'
Write-Host 'Theme/pet selection recovery and persistence boundary: OK'
Write-Host 'WinUI theme High Contrast fail-safe: OK'
Write-Host 'App-owned split desktop-pet composition and diagnostics: OK'
Write-Host 'Flying Sprite -> independent FlyingHost runtime: OK'
Write-Host 'VPetCore -> VPet PetHost runtime: OK'
Write-Host 'Separate FlyingHost/PetHost bundle identity + cache + timeout boundaries: OK'
Write-Host 'Frozen 3.5 flying movement profiles preserved outside VPet PetHost: OK'
Write-Host 'Foundation split bundle packaging/self-test contract: OK'
Write-Host 'Personalization deterministic catalog and Windows split-runtime smoke: OK'
Write-Host 'FACM 4.0 Personalization foundation contract: SUCCESS'
