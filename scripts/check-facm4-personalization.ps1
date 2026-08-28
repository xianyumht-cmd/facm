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
$settingsPath = Join-Path $Root 'src/FACM.Core/Settings/Settings2.cs'
$viewModelPath = Join-Path $Root 'src/FACM.App/ViewModels/PersonalizationViewModel.cs'
$runtimePath = Join-Path $Root 'src/FACM.App/Personalization/WinUiThemeRuntime.cs'
$surfacePath = Join-Path $Root 'src/FACM.App/MainWindow.Personalization.cs'
$controlCenterPath = Join-Path $Root 'src/FACM.App/ViewModels/ControlCenterViewModel.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/PersonalizationSmoke.cs'
$foundationProgramPath = Join-Path $Root 'src/FACM.FoundationSmoke/Program.cs'

foreach ($path in @($coreCatalogPath, $coreContractsPath, $settingsPath, $viewModelPath, $runtimePath, $surfacePath, $controlCenterPath, $smokePath, $foundationProgramPath)) {
    if (-not (Test-Path $path)) { Fail "Personalization contract file missing: $path" }
}

$core = (Get-Content $coreCatalogPath -Raw) + "`n" + (Get-Content $coreContractsPath -Raw)
$settings = Get-Content $settingsPath -Raw
$viewModel = Get-Content $viewModelPath -Raw
$runtime = Get-Content $runtimePath -Raw
$surface = Get-Content $surfacePath -Raw
$controlCenter = Get-Content $controlCenterPath -Raw
$smoke = Get-Content $smokePath -Raw
$foundationProgram = Get-Content $foundationProgramPath -Raw

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
    'RecoveredLastKnownGood', 'RecoveryDefaults', 'Appearance.ThemeId', 'Pets.StyleId', 'Pets.Enabled', 'SaveAsync'
)) {
    if ($viewModel -notmatch [regex]::Escape($required)) { Fail "PersonalizationViewModel behavior missing: $required" }
}
if ($viewModel -match 'Pets\.Enabled\s*=\s*true') {
    Fail 'Pet style selection must not silently enable desktop pet mode before a real desktop-pet runtime succeeds.'
}

foreach ($required in @('WinUiThemeRuntime', 'AccessibilitySettings', 'HighContrast', 'FacmBackgroundBrush', 'FacmSurfaceBrush', 'FacmTextPrimaryBrush', 'FacmAccentBrush', 'FacmStrokeBrush')) {
    if ($runtime -notmatch [regex]::Escape($required)) { Fail "WinUI theme runtime behavior missing: $required" }
}
if ($runtime -match '\bFile\.|\bDirectory\.|Settings2Repository') { Fail 'WinUI theme runtime must not own settings or filesystem access.' }

foreach ($required in @(
    'FACM.Personalization.ThemePicker', 'FACM.Personalization.PetPicker', 'DisplayMemberPath',
    'SelectThemeAsync', 'SelectPetAsync', 'ConfigurePersonalization', 'OnPersonalizationNavigationChanged'
)) {
    if ($surface -notmatch [regex]::Escape($required)) { Fail "Personalization Shell surface missing: $required" }
}
if ($surface -match 'System\.Diagnostics|HttpClient|\bFile\.|\bDirectory\.') { Fail 'Personalization Shell presentation owns platform/data access.' }
if ($controlCenter -notmatch 'CreatePersonalization') { Fail 'Existing Settings 2.0 owner must compose the personalization ViewModel.' }

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
Write-Host 'Settings 2.0 shared catalog ownership: OK'
Write-Host 'Theme/pet selection recovery and persistence boundary: OK'
Write-Host 'WinUI theme High Contrast fail-safe: OK'
Write-Host 'Personalization Shell theme and pet pickers: OK'
Write-Host 'Personalization deterministic catalog smoke: OK'
Write-Host 'FACM 4.0 Personalization foundation contract: SUCCESS'