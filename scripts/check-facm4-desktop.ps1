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
$appCodePath = Join-Path $Root 'src/FACM.App/App.xaml.cs'
$textPath = Join-Path $Root 'src/FACM.Core/Text/UiTextContracts.cs'

foreach ($path in @(
    $corePath, $dragCorePath, $platformPath, $floatingPlatformPath,
    $floatingXamlPath, $floatingCodePath, $appCodePath, $textPath
)) {
    if (-not (Test-Path $path)) { Fail "Desktop contract file missing: $path" }
}

$core = Get-Content $corePath -Raw
$dragCore = Get-Content $dragCorePath -Raw
$platform = Get-Content $platformPath -Raw
$floatingPlatform = Get-Content $floatingPlatformPath -Raw
$floatingXaml = Get-Content $floatingXamlPath -Raw
$floatingCode = Get-Content $floatingCodePath -Raw
$appCode = Get-Content $appCodePath -Raw
$text = Get-Content $textPath -Raw

$coreDesktop = $core + "`n" + $dragCore
foreach ($forbidden in @('Microsoft\.UI', 'System\.Windows\.Forms', 'DllImport', 'LibraryImport', 'user32', 'gdi32', 'shcore')) {
    if ($coreDesktop -match $forbidden) { Fail "Core desktop placement crossed the platform boundary: $forbidden" }
}
foreach ($required in @(
    'DesktopPoint', 'DesktopSize', 'DesktopRect', 'DesktopWorkArea', 'DesktopAnchor',
    'AnchorPlacementService', 'IDesktopWorkAreaProvider', 'FloatingSurfaceDragService',
    'HasExceededThreshold', 'ClampTopLeft'
)) {
    if ($coreDesktop -notmatch ('\b' + [regex]::Escape($required) + '\b')) { Fail "Core desktop contract missing: $required" }
}

foreach ($required in @('EnumDisplayMonitors', 'GetMonitorInfo', 'GetDpiForMonitor')) {
    if ($platform -notmatch ('\b' + [regex]::Escape($required) + '\b')) { Fail "Windows desktop adapter missing API: $required" }
}
if ($platform -match 'Microsoft\.UI\.Xaml') { Fail 'Windows work-area adapter must not own WinUI controls.' }

foreach ($required in @(
    'WindowsFloatingSurfacePlatform', 'CreateEllipticRgn', 'SetWindowRgn', 'DeleteObject',
    'GetWindowRect', 'GetClientRect', 'ClientToScreen', 'TryGetClientBoundsInWindow'
)) {
    if ($floatingPlatform -notmatch ('\b' + [regex]::Escape($required) + '\b')) {
        Fail "Windows floating-surface adapter missing native shape API: $required"
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
    'FloatingSurfaceDragService', 'ApplyPlacement', '_ensureMainWindow', '_persistPlacement',
    'MoveAndResize', 'AppWindow.Move', 'PointerPressedEvent', 'PointerMovedEvent', 'PointerReleasedEvent',
    'PointerCanceledEvent', 'PointerCaptureLostEvent', 'AddHandler', 'handledEventsToo: true',
    'RasterizationScale', 'TryApplyCircularRegion', 'ExtendsContentIntoTitleBar', 'IsShownInSwitchers'
)) {
    if ($floatingCode -notmatch [regex]::Escape($required)) { Fail "FloatingWindow desktop behavior missing: $required" }
}
if ($floatingCode -match 'FloatingButton\.PointerPressed\s*\+=|FloatingButton\.PointerMoved\s*\+=|FloatingButton\.PointerReleased\s*\+=') {
    Fail 'Floating drag must not rely on Button default pointer routing; root handledEventsToo routing is required.'
}
if ($floatingCode -match 'FloatingRoot\.CapturePointer|FloatingRoot\.ReleasePointerCapture') {
    Fail 'Floating root must not steal pointer capture from the Button control.'
}

if ((Count-Matches $appCode 'new\s+WindowsLeagueTransportSessionSource\s*\(') -ne 1) {
    Fail 'FACM.App must still create exactly one League session owner.'
}
if ((Count-Matches $appCode 'new\s+FloatingWindow\s*\(') -ne 1) { Fail 'App must create exactly one floating desktop surface.' }
if ((Count-Matches $appCode 'new\s+WindowsFloatingSurfacePlatform\s*\(') -ne 1) {
    Fail 'App must compose exactly one Windows floating-surface platform adapter.'
}
if ($appCode -notmatch 'void\s+EnsureMainWindow\s*\(') { Fail 'App must implement EnsureMainWindow semantics.' }
if ($appCode -notmatch 'new\s+MainWindow\s*\(' -or $appCode -notmatch '_window\.Activate\s*\(') {
    Fail 'EnsureMainWindow must create-or-activate the Main Shell.'
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
Write-Host 'Circular floating-surface client alignment: OK'
Write-Host 'Handled pointer routing/click-drag ownership: OK'
Write-Host 'FACM 4.0 Desktop contract: SUCCESS'
