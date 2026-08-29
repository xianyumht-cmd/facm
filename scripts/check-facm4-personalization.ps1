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
$appPersonalizationPath = Join-Path $Root 'src/FACM.App/App.Personalization.cs'
$appProjectPath = Join-Path $Root 'src/FACM.App/FACM.App.csproj'
$controlCenterPath = Join-Path $Root 'src/FACM.App/ViewModels/ControlCenterViewModel.cs'
$bundleStorePath = Join-Path $Root 'src/FACM.Platform.Windows/Personalization/WindowsPetHostBundleStore.cs'
$vpetRuntimePath = Join-Path $Root 'src/FACM.Platform.Windows/Personalization/WindowsVPetRuntime.cs'
$jobPath = Join-Path $Root 'src/FACM.Platform.Windows/Personalization/WindowsChildProcessJob.cs'
$petHostProgramPath = Join-Path $Root 'src/FACM.PetHost/Program.cs'
$flyingProfilesPath = Join-Path $Root 'src/FACM.PetHost/FlyingPetProfiles.cs'
$flyingWindowPath = Join-Path $Root 'src/FACM.PetHost/FlyingPetHostWindow.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/PersonalizationSmoke.cs'
$foundationProgramPath = Join-Path $Root 'src/FACM.FoundationSmoke/Program.cs'
$windowsSmokePath = Join-Path $Root 'src/FACM.WindowsSmoke/PetHostBundleSmoke.cs'
$workflowPath = Join-Path $Root '.github/workflows/facm4-foundation.yml'

foreach ($path in @(
    $coreCatalogPath, $coreContractsPath, $preferencePath, $settingsPath, $viewModelPath, $runtimePath,
    $surfacePath, $appPersonalizationPath, $appProjectPath, $controlCenterPath, $bundleStorePath,
    $vpetRuntimePath, $jobPath, $petHostProgramPath, $flyingProfilesPath, $flyingWindowPath,
    $smokePath, $foundationProgramPath, $windowsSmokePath, $workflowPath
)) {
    if (-not (Test-Path $path)) { Fail "Personalization contract file missing: $path" }
}

$core = (Get-Content $coreCatalogPath -Raw) + "`n" + (Get-Content $coreContractsPath -Raw)
$preference = Get-Content $preferencePath -Raw
$settings = Get-Content $settingsPath -Raw
$viewModel = Get-Content $viewModelPath -Raw
$runtime = Get-Content $runtimePath -Raw
$surface = Get-Content $surfacePath -Raw
$appPersonalization = Get-Content $appPersonalizationPath -Raw
$appProject = Get-Content $appProjectPath -Raw
$controlCenter = Get-Content $controlCenterPath -Raw
$bundleStore = Get-Content $bundleStorePath -Raw
$vpetRuntime = Get-Content $vpetRuntimePath -Raw
$job = Get-Content $jobPath -Raw
$petHostProgram = Get-Content $petHostProgramPath -Raw
$flyingProfiles = Get-Content $flyingProfilesPath -Raw
$flyingWindow = Get-Content $flyingWindowPath -Raw
$smoke = Get-Content $smokePath -Raw
$foundationProgram = Get-Content $foundationProgramPath -Raw
$windowsSmoke = Get-Content $windowsSmokePath -Raw
$workflow = Get-Content $workflowPath -Raw

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
if ($viewModel -match '\.SaveAsync\s*\(') {
    Fail 'PersonalizationViewModel must use the atomic narrow Settings 2.0 mutation boundary, not whole-document SaveAsync.'
}
if ($viewModel -match 'Pets\.Enabled\s*=\s*true') {
    Fail 'Pet style selection must not silently enable desktop pet mode before an explicit runtime intent.'
}

foreach ($required in @('DesktopPetPreferenceService', 'Pets.Enabled = true', 'Pets.Enabled = false', 'ApplyAsync(true', 'ApplyAsync(false', 'RecoveryDefaults', 'ResetPositionAsync', 'UpdateAsync')) {
    if ($preference -notmatch [regex]::Escape($required)) { Fail "Desktop pet settings/runtime coordinator missing: $required" }
}
if ($preference -match '\.SaveAsync\s*\(') {
    Fail 'DesktopPetPreferenceService must not reintroduce stale whole-document Settings2 saves.'
}

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
if ($controlCenter -notmatch 'CreatePersonalization') { Fail 'Existing Settings 2.0 owner must compose the personalization ViewModel.' }

foreach ($required in @(
    'CreatePersonalizationViewModel', 'WinUiThemeRuntime', 'WindowsPetHostBundleStore', 'WindowsVPetRuntime',
    'GetManifestResourceStream', 'ConfigureDesktopPetService', 'InitializeDesktopPetAfterLauncherReadyAsync',
    'SetDesktopEntryVisible', 'ResetFloatingEntryPositionAsync', 'DisposePersonalizationRuntime'
)) {
    if ($appPersonalization -notmatch [regex]::Escape($required)) { Fail "App personalization composition missing: $required" }
}

foreach ($required in @('FACM.Resources.PetHost.zip', 'PetHostBundlePath', 'RequirePetHostBundle', 'EmbeddedResource')) {
    if ($appProject -notmatch [regex]::Escape($required)) { Fail "FACM.App controlled PetHost embedding missing: $required" }
}

foreach ($required in @('WindowsPetHostBundleStore', 'SHA256.HashData', 'ZipArchive', 'pethost-host', 'partial-', 'path traversal', 'CriticalPayloadFiles', 'PrepareTimeout', '_cachedPreparation')) {
    if ($bundleStore -notmatch [regex]::Escape($required)) { Fail "Controlled PetHost bundle store missing: $required" }
}
foreach ($required in @(
    'IDesktopPetRuntime', 'NamedPipeClientStream', 'WindowsChildProcessJob.TryAssign', 'activate|', 'event|',
    'ready', 'runtime-failed', 'SetLauncherVisible(false)', 'SetLauncherVisible(true)',
    'FacmPetRuntimeKind.FlyingSprite', '--runtime', '--pet-id', 'runtime-unsupported',
    'HostReadyTimeout', 'host-ready-timeout', '_lifetime'
)) {
    if ($vpetRuntime -notmatch [regex]::Escape($required)) { Fail "Controlled desktop PetHost runtime missing: $required" }
}
foreach ($required in @('JobObjectLimitKillOnJobClose', 'AssignProcessToJobObject', 'SetInformationJobObject')) {
    if ($job -notmatch [regex]::Escape($required)) { Fail "PetHost Job Object containment missing: $required" }
}

foreach ($required in @('FlyingPetHostWindow', '--runtime', '--pet-id', 'flying', 'VPetAssetCacheValidator')) {
    if ($petHostProgram -notmatch [regex]::Escape($required)) { Fail "PetHost runtime routing missing: $required" }
}
foreach ($id in @('greenfly', 'bee', 'real-bee', 'dragonfly', 'butterfly', 'moth')) {
    if ($flyingProfiles -notmatch ('"' + [regex]::Escape($id) + '"')) { Fail "Flying PetHost profile missing: $id" }
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
    if ($flyingWindow -notmatch [regex]::Escape($required)) { Fail "Flying PetHost behavior missing: $required" }
}

foreach ($required in @('Prepare controlled PetHost payload', 'FACM.PetHost/FACM.PetHost.csproj', '--self-test', 'Compress-Archive', 'PetHostBundle.zip', 'RequirePetHostBundle=true')) {
    if ($workflow -notmatch [regex]::Escape($required)) { Fail "Foundation workflow PetHost packaging missing: $required" }
}
foreach ($required in @('WindowsPetHostBundleStore', 'CacheHit', 'RejectsPathTraversalAsync', 'BundleSha256', 'second prepare must not reopen the embedded bundle')) {
    if ($windowsSmoke -notmatch [regex]::Escape($required)) { Fail "Windows controlled PetHost smoke missing: $required" }
}

foreach ($required in @(
    'stable theme count', 'unknown theme fallback', 'unique theme ids',
    'stable default pet id', 'unknown pet fallback', 'visible VPet Core route',
    'legacy pet id compatibility', 'new installs must not auto-enable desktop pet',
    'unsupported theme rejection', 'unsupported pet rejection'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) { Fail "Personalization smoke missing assertion: $required" }
}
if ($foundationProgram -notmatch 'PersonalizationSmoke\.Run\(\)') { Fail 'Foundation smoke runner does not execute personalization smoke.' }

Write-Host 'Personalization stable theme catalog: OK'
Write-Host 'Personalization stable pet compatibility catalog: OK'
Write-Host 'Settings 2.0 shared catalog + atomic mutation ownership: OK'
Write-Host 'Theme/pet selection recovery and persistence boundary: OK'
Write-Host 'WinUI theme High Contrast fail-safe: OK'
Write-Host 'App-owned theme and desktop pet composition: OK'
Write-Host 'Explicit enable / restore F / reset-position controls: OK'
Write-Host 'Controlled PetHost bundle SHA/extraction/cache/timeout boundary: OK'
Write-Host 'VPet + Flying Sprite named-pipe ready timeout and Job Object runtime boundary: OK'
Write-Host 'Frozen 3.5 flying movement profiles and modern PetHost window: OK'
Write-Host 'Foundation PetHost packaging/self-test contract: OK'
Write-Host 'Personalization deterministic catalog and Windows bundle smoke: OK'
Write-Host 'FACM 4.0 Personalization foundation contract: SUCCESS'
