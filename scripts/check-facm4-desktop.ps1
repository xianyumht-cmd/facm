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

$corePath = Join-Path $Root 'src/FACM.Core/Desktop/AnchorPlacement.cs'
$dragCorePath = Join-Path $Root 'src/FACM.Core/Desktop/FloatingSurfaceDrag.cs'
$platformPath = Join-Path $Root 'src/FACM.Platform.Windows/Desktop/WindowsDesktopWorkAreaProvider.cs'
$floatingPlatformPath = Join-Path $Root 'src/FACM.Platform.Windows/Desktop/WindowsFloatingSurfacePlatform.cs'
$floatingXamlPath = Join-Path $Root 'src/FACM.App/FloatingWindow.xaml'
$floatingCodePath = Join-Path $Root 'src/FACM.App/FloatingWindow.xaml.cs'
$compactXamlPath = Join-Path $Root 'src/FACM.App/CompactLauncherWindow.xaml'
$compactCodePath = Join-Path $Root 'src/FACM.App/CompactLauncherWindow.xaml.cs'
$appCodePath = Join-Path $Root 'src/FACM.App/App.xaml.cs'
$textPath = Join-Path $Root 'src/FACM.Core/Text/UiTextContracts.cs'

foreach ($path in @(
    $corePath, $dragCorePath, $platformPath, $floatingPlatformPath,
    $floatingXamlPath, $floatingCodePath, $compactXamlPath, $compactCodePath,
    $appCodePath, $textPath
)) {
    if (-not (Test-Path $path)) { Fail "Desktop contract file missing: $path" }
}

$core = Get-Content $corePath -Raw
$dragCore = Get-Content $dragCorePath -Raw
$platform = Get-Content $platformPath -Raw
$floatingPlatform = Get-Content $floatingPlatformPath -Raw
$floatingXaml = Get-Content $floatingXamlPath -Raw
$floatingCode = Get-Content $floatingCodePath -Raw
$compactXaml = Get-Content $compactXamlPath -Raw
$compactCode = Get-Content $compactCodePath -Raw
$appCode = Get-Content $appCodePath -Raw
$text = Get-Content $textPath -Raw

$coreDesktop = $core + "`n" + $dragCore
foreach ($forbidden in @('Microsoft\.UI', 'System\.Windows\.Forms', 'DllImport', 'LibraryImport', 'user32', 'gdi32', 'shcore')) {
    if ($coreDesktop -match $forbidden) { Fail "Core desktop placement crossed the platform boundary: $forbidden" }
}
foreach ($required in @(
    'DesktopPoint', 'DesktopSize', 'DesktopRect', 'DesktopWorkArea', 'DesktopAnchor',
    'AnchorPlacementService', 'IDesktopWorkAreaProvider', 'FloatingSurfaceDragService',
    'HasExceededThreshold', 'ClampTopLeft', 'HasExceededLegacyBallThreshold',
    'ClampLegacyBallTopLeft', 'DefaultLegacyBallTopLeft'
)) {
    if ($coreDesktop -notmatch ('\b' + [regex]::Escape($required) + '\b')) { Fail "Core desktop contract missing: $required" }
}

foreach ($required in @('EnumDisplayMonitors', 'GetMonitorInfo', 'GetDpiForMonitor')) {
    if ($platform -notmatch ('\b' + [regex]::Escape($required) + '\b')) { Fail "Windows desktop adapter missing API: $required" }
}
if ($platform -match 'Microsoft\.UI\.Xaml') { Fail 'Windows work-area adapter must not own WinUI controls.' }

foreach ($required in @(
    'WindowsFloatingSurfacePlatform', 'CreateEllipticRgn', 'SetWindowRgn', 'DeleteObject',
    'GetWindowRect', 'GetClientRect', 'ClientToScreen', 'TryGetClientBoundsInWindow',
    'GetCursorPos', 'TryGetCursorPosition'
)) {
    if ($floatingPlatform -notmatch ('\b' + [regex]::Escape($required) + '\b')) {
        Fail "Windows floating-surface adapter missing native behavior: $required"
    }
}
if ($floatingPlatform -match 'Microsoft\.UI\.Xaml') { Fail 'Windows floating-surface adapter must not own WinUI controls.' }

if ((Count-Matches $floatingXaml '<Button(?:\s|>)') -ne 1) { Fail 'FloatingWindow must contain exactly one primary F button.' }
if ($floatingXaml -match '<NavigationView(?:\s|>)' -or $floatingXaml -match '<Frame(?:\s|>)') {
    Fail 'FloatingWindow must not duplicate the Main Shell navigation tree.'
}
if ($floatingXaml -match '#[0-9A-Fa-f]{6,8}') { Fail 'FloatingWindow must use semantic theme resources, not hard-coded colors.' }
if ($floatingXaml -notmatch 'FacmPrimaryButtonStyle' -or
    $floatingXaml -notmatch 'FacmAccentBrush' -or
    $floatingXaml -notmatch 'FacmAccentTextBrush') {
    Fail 'FloatingWindow must reuse the shared FACM design system and fill its shaped client surface.'
}
if ($floatingXaml -notmatch 'Width="64"' -or $floatingXaml -notmatch 'Height="64"' -or $floatingXaml -notmatch 'CornerRadius="32"') {
    Fail 'FloatingWindow primary control must fill the circular 64-DIP host surface.'
}

foreach ($forbidden in @(
    'WindowsLeagueTransportSessionSource', 'LeagueHttpGateway', 'HttpClient',
    'Settings2Repository', 'BoundedJsonLinesDiagnosticSink', '\bFile\.', '\bDirectory\.',
    'SetWindowsHookEx', 'GetAsyncKeyState', 'LowLevelKeyboardProc', '\bTimer\b',
    'DllImport', 'LibraryImport', 'CreateEllipticRgn', 'SetWindowRgn', 'GetClientRect', 'ClientToScreen'
)) {
    if ($floatingCode -match $forbidden) { Fail "FloatingWindow gained forbidden runtime/platform ownership: $forbidden" }
}
foreach ($required in @(
    'IDesktopWorkAreaProvider', 'WindowsFloatingSurfacePlatform', 'AnchorPlacementService',
    'FloatingSurfaceDragService', 'ApplyPlacement', '_toggleCompactLauncher', '_persistPlacement',
    'GetCurrentBounds', 'MoveAndResize', 'AppWindow.Move', 'PointerPressedEvent', 'PointerMovedEvent', 'PointerReleasedEvent',
    'PointerCanceledEvent', 'PointerCaptureLostEvent', 'AddHandler', 'handledEventsToo: true',
    'TryGetCursorPosition', 'HasExceededLegacyBallThreshold', 'ClampLegacyBallTopLeft',
    'DefaultLegacyBallTopLeft', '_dragCursorStart', '_dragWindowStart',
    'TryApplyCircularRegion', 'ExtendsContentIntoTitleBar', 'IsShownInSwitchers'
)) {
    if ($floatingCode -notmatch [regex]::Escape($required)) { Fail "FloatingWindow desktop behavior missing: $required" }
}
if ($floatingCode -match 'GetPointerScreenPoint|RasterizationScale') {
    Fail 'Floating drag must use frozen absolute screen cursor coordinates, not moving-window-relative pointer math.'
}
if ($floatingCode -match 'FloatingButton\.PointerPressed\s*\+=|FloatingButton\.PointerMoved\s*\+=|FloatingButton\.PointerReleased\s*\+=') {
    Fail 'Floating drag must not rely on Button default pointer routing; root handledEventsToo routing is required.'
}
if ($floatingCode -match 'FloatingRoot\.CapturePointer|FloatingRoot\.ReleasePointerCapture') {
    Fail 'Floating root must not steal pointer capture from the Button control.'
}
if ($floatingCode -notmatch '(?s)OnFloatingButtonClick.*_toggleCompactLauncher\s*\(') {
    Fail 'Ordinary WinUI Button.Click must toggle the compact launcher.'
}
if ($floatingCode -match '(?s)OnFloatingPointerReleased.*_toggleCompactLauncher\s*\(') {
    Fail 'PointerReleased must not race Button.Click for compact launcher ownership.'
}

if ($compactXaml -match '<NavigationView(?:\s|>)' -or $compactXaml -match '<Frame(?:\s|>)') {
    Fail 'Compact launcher must remain a lightweight 3.5-style entry surface, not duplicate Main Shell navigation.'
}
if ($compactXaml -match '#[0-9A-Fa-f]{6,8}') { Fail 'Compact launcher must use semantic theme resources.' }
foreach ($id in @('FACM.Compact.Repair', 'FACM.Compact.League', 'FACM.Compact.Personalization', 'FACM.Compact.Settings')) {
    if ($compactXaml -notmatch [regex]::Escape($id)) { Fail "Compact launcher AutomationId missing: $id" }
}
foreach ($required in @(
    'BaseWidthDip = 420d', 'BaseHeightDip = 680d', 'ShowNextTo',
    'DesktopDpi.DipsToPixels', 'DesktopDpi.UniformDipsToPixels',
    'AppWindow.MoveAndResize', 'IsShownInSwitchers', 'OpenSection'
)) {
    if ($compactCode -notmatch [regex]::Escape($required)) { Fail "Compact launcher behavior missing: $required" }
}
foreach ($forbidden in @(
    'WindowsLeagueTransportSessionSource', 'LeagueHttpGateway', 'HttpClient',
    'Settings2Repository', 'BoundedJsonLinesDiagnosticSink', 'DllImport', 'LibraryImport',
    'SetWindowsHookEx', 'GetAsyncKeyState', 'LowLevelKeyboardProc'
)) {
    if ($compactCode -match $forbidden) { Fail "Compact launcher gained forbidden runtime/platform ownership: $forbidden" }
}

if ((Count-Matches $appCode 'new\s+WindowsLeagueTransportSessionSource\s*\(') -ne 1) {
    Fail 'FACM.App must still create exactly one League session owner.'
}
if ((Count-Matches $appCode 'new\s+FloatingWindow\s*\(') -ne 1) { Fail 'App must create exactly one floating desktop surface.' }
if ((Count-Matches $appCode 'new\s+CompactLauncherWindow\s*\(') -ne 1) { Fail 'App must own exactly one compact launcher construction path.' }
if ((Count-Matches $appCode 'new\s+WindowsFloatingSurfacePlatform\s*\(') -ne 1) {
    Fail 'App must compose exactly one Windows floating-surface platform adapter.'
}
foreach ($required in @(
    'PrepareMainWindow', 'EnsureMainWindow', 'OpenMainWindowSection', 'ToggleCompactLauncher',
    'GetOrCreateMainWindow', 'NavigateToSection', 'desktop-launcher-ready', 'desktop.launcher'
)) {
    if ($appCode -notmatch [regex]::Escape($required)) { Fail "App compact-launcher composition missing: $required" }
}
if ($appCode -notmatch 'new\s+MainWindow\s*\(' -or $appCode -notmatch 'window\.Activate\s*\(') {
    Fail 'Detailed Main Shell must still be create-or-activate capable from the compact launcher.'
}
if ($appCode -match '(?m)^\s*EnsureMainWindow\(\);\s*$') {
    Fail 'FACM 4.0 startup must not automatically activate the large Main Shell; launcher-first parity is required.'
}
if ($appCode -notmatch 'PersistFloatingPlacementAsync' -or
    $appCode -notmatch 'Pets\.BallX\s*=' -or
    $appCode -notmatch 'Pets\.BallY\s*=' -or
    $appCode -notmatch 'settings\.SaveAsync') {
    Fail 'App must persist user-dragged floating-surface coordinates through Settings 2.0.'
}
if ($appCode -notmatch 'RecoveredLastKnownGood' -or $appCode -notmatch 'RecoveryDefaults') {
    Fail 'Floating-surface persistence must preserve corrupt-primary Settings recovery semantics.'
}
if ($appCode -match '(?is)Pets\.Enabled.{0,600}new\s+FloatingWindow') {
    Fail 'pets.enabled controls optional desktop pets, not the built-in F launcher.'
}
if ($appCode -match 'SetWindowsHookEx|GetAsyncKeyState|LowLevelKeyboardProc') {
    Fail 'Desktop surface must not introduce low-level keyboard hooks or polling.'
}

if ($text -notmatch 'public const string\s+DesktopOpenShell\s*=') { Fail 'DesktopOpenShell UI text key missing.' }
if ($text -notmatch '\[UiTextKeys\.DesktopOpenShell\]\s*=') { Fail 'DesktopOpenShell default text missing.' }

Write-Host 'Core desktop placement/drag boundary: OK'
Write-Host 'Windows work-area/DPI adapter: OK'
Write-Host 'FACM 3.5-compatible absolute cursor drag model on WinUI: OK'
Write-Host 'WinUI Button.Click owns compact launcher toggle: OK'
Write-Host '420x680 compact launcher parity surface: OK'
Write-Host 'Circular floating-surface client alignment: OK'
Write-Host 'FACM 4.0 Desktop contract: SUCCESS'
