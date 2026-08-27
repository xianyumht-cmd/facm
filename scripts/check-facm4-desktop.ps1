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
$platformPath = Join-Path $Root 'src/FACM.Platform.Windows/Desktop/WindowsDesktopWorkAreaProvider.cs'
$floatingXamlPath = Join-Path $Root 'src/FACM.App/FloatingWindow.xaml'
$floatingCodePath = Join-Path $Root 'src/FACM.App/FloatingWindow.xaml.cs'
$appCodePath = Join-Path $Root 'src/FACM.App/App.xaml.cs'
$textPath = Join-Path $Root 'src/FACM.Core/Text/UiTextContracts.cs'

foreach ($path in @($corePath, $platformPath, $floatingXamlPath, $floatingCodePath, $appCodePath, $textPath)) {
    if (-not (Test-Path $path)) { Fail "Desktop contract file missing: $path" }
}

$core = Get-Content $corePath -Raw
$platform = Get-Content $platformPath -Raw
$floatingXaml = Get-Content $floatingXamlPath -Raw
$floatingCode = Get-Content $floatingCodePath -Raw
$appCode = Get-Content $appCodePath -Raw
$text = Get-Content $textPath -Raw

foreach ($forbidden in @('Microsoft\.UI', 'System\.Windows\.Forms', 'DllImport', 'LibraryImport', 'user32', 'shcore')) {
    if ($core -match $forbidden) { Fail "Core desktop placement crossed the platform boundary: $forbidden" }
}
foreach ($required in @('DesktopPoint', 'DesktopSize', 'DesktopRect', 'DesktopWorkArea', 'DesktopAnchor', 'AnchorPlacementService', 'IDesktopWorkAreaProvider')) {
    if ($core -notmatch ('\b' + [regex]::Escape($required) + '\b')) { Fail "Core desktop contract missing: $required" }
}

foreach ($required in @('EnumDisplayMonitors', 'GetMonitorInfo', 'GetDpiForMonitor')) {
    if ($platform -notmatch ('\b' + [regex]::Escape($required) + '\b')) { Fail "Windows desktop adapter missing API: $required" }
}
if ($platform -match 'Microsoft\.UI\.Xaml') { Fail 'Windows work-area adapter must not own WinUI controls.' }

if ((Count-Matches $floatingXaml '<Button(?:\s|>)') -ne 1) { Fail 'FloatingWindow must contain exactly one primary F button.' }
if ($floatingXaml -match '<NavigationView(?:\s|>)' -or $floatingXaml -match '<Frame(?:\s|>)') {
    Fail 'FloatingWindow must not duplicate the Main Shell navigation tree.'
}
if ($floatingXaml -match '#[0-9A-Fa-f]{6,8}') { Fail 'FloatingWindow must use semantic theme resources, not hard-coded colors.' }
if ($floatingXaml -notmatch 'FacmPrimaryButtonStyle' -or $floatingXaml -notmatch 'FacmAccentTextBrush') {
    Fail 'FloatingWindow must reuse the shared FACM design system.'
}

foreach ($forbidden in @(
    'WindowsLeagueTransportSessionSource', 'LeagueHttpGateway', 'HttpClient',
    'Settings2Repository', 'BoundedJsonLinesDiagnosticSink', '\bFile\.', '\bDirectory\.',
    'SetWindowsHookEx', 'GetAsyncKeyState', 'LowLevelKeyboardProc', '\bTimer\b'
)) {
    if ($floatingCode -match $forbidden) { Fail "FloatingWindow gained forbidden runtime ownership: $forbidden" }
}
foreach ($required in @('IDesktopWorkAreaProvider', 'AnchorPlacementService', 'ApplyPlacement', '_ensureMainWindow', 'MoveAndResize')) {
    if ($floatingCode -notmatch [regex]::Escape($required)) { Fail "FloatingWindow desktop behavior missing: $required" }
}

if ((Count-Matches $appCode 'new\s+WindowsLeagueTransportSessionSource\s*\(') -ne 1) {
    Fail 'FACM.App must still create exactly one League session owner.'
}
if ((Count-Matches $appCode 'new\s+FloatingWindow\s*\(') -ne 1) { Fail 'App must create exactly one floating desktop surface.' }
if ($appCode -notmatch 'void\s+EnsureMainWindow\s*\(') { Fail 'App must implement EnsureMainWindow semantics.' }
if ($appCode -notmatch 'new\s+MainWindow\s*\(' -or $appCode -notmatch '_window\.Activate\s*\(') {
    Fail 'EnsureMainWindow must create-or-activate the Main Shell.'
}
if ($appCode -match 'SetWindowsHookEx|GetAsyncKeyState|LowLevelKeyboardProc') {
    Fail 'Gate 7 must not introduce low-level keyboard hooks or polling.'
}

if ($text -notmatch 'public const string\s+DesktopOpenShell\s*=') { Fail 'DesktopOpenShell UI text key missing.' }
if ($text -notmatch '\[UiTextKeys\.DesktopOpenShell\]\s*=') { Fail 'DesktopOpenShell default text missing.' }

Write-Host 'Core desktop placement boundary: OK'
Write-Host 'Windows work-area/DPI adapter: OK'
Write-Host 'Floating surface ownership: OK'
Write-Host 'FACM 4.0 Desktop contract: SUCCESS'
