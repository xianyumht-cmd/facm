param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Read-Required([string]$RelativePath) {
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path $path)) { Fail "Accessibility contract file missing: $RelativePath" }
    return Get-Content $path -Raw
}

$manifest = Read-Required 'src/FACM.App/app.manifest'
$mainXaml = Read-Required 'src/FACM.App/MainWindow.xaml'
$mainCode = Read-Required 'src/FACM.App/MainWindow.xaml.cs'
$floatingXaml = Read-Required 'src/FACM.App/FloatingWindow.xaml'
$floatingCode = Read-Required 'src/FACM.App/FloatingWindow.xaml.cs'
$dpiCore = Read-Required 'src/FACM.Core/Desktop/DesktopDpi.cs'
$windowsMonitor = Read-Required 'src/FACM.Platform.Windows/Desktop/WindowsDesktopWorkAreaProvider.cs'
$text = Read-Required 'src/FACM.Core/Text/UiTextContracts.cs'
$tokens = Read-Required 'src/FACM.App/Themes/FacmTokens.xaml'
$smoke = Read-Required 'src/FACM.FoundationSmoke/Gate10Smoke.cs'

if ($manifest -notmatch '<dpiAware[^>]*>\s*true/pm\s*</dpiAware>') {
    Fail 'FACM.App manifest must declare legacy per-monitor DPI fallback (true/pm).'
}
if ($manifest -notmatch '<dpiAwareness[^>]*>\s*PerMonitorV2,\s*PerMonitor\s*</dpiAwareness>') {
    Fail 'FACM.App manifest must explicitly declare PerMonitorV2, PerMonitor.'
}
if ($manifest -notmatch 'requestedExecutionLevel\s+level="asInvoker"') {
    Fail 'DPI migration must not silently change the app to always-elevated execution.'
}

foreach ($required in @('DefaultDpi = 96d', 'ScaleFromDpi', 'DipsToPixels', 'UniformDipsToPixels')) {
    if ($dpiCore -notmatch [regex]::Escape($required)) { Fail "Core DPI contract missing: $required" }
}
if ($windowsMonitor -notmatch 'DesktopDpi\.ScaleFromDpi') {
    Fail 'Windows monitor adapter must use the Core DPI conversion contract.'
}
if ($floatingCode -notmatch 'DesktopDpi\.DipsToPixels' -or $floatingCode -notmatch 'DesktopDpi\.UniformDipsToPixels') {
    Fail 'FloatingWindow must use the Core DPI conversion contract.'
}
if ($floatingCode -match 'SurfaceSideDip\s*\*\s*selected\.DpiScale') {
    Fail 'FloatingWindow restored duplicate DPI scale math.'
}

foreach ($dpi in @('96', '120', '144', '168', '192')) {
    if ($smoke -notmatch ('\(' + $dpi + ',\s*')) { Fail "Gate10Smoke missing DPI case: $dpi" }
}
foreach ($monitor in @('left-125', 'primary-100', 'right-200', 'top-175')) {
    if ($smoke -notmatch [regex]::Escape($monitor)) { Fail "Gate10Smoke missing mixed-DPI monitor: $monitor" }
}
if ($smoke -notmatch 'RecoveredOffScreen') { Fail 'Gate10Smoke must preserve off-screen recovery coverage.' }

$automationIds = @(
    'FACM.Nav.Repair', 'FACM.Nav.League', 'FACM.Nav.Personalization', 'FACM.Nav.Settings',
    'FACM.Diagnostics.Summary', 'FACM.Diagnostics.Refresh', 'FACM.Diagnostics.Copy', 'FACM.Diagnostics.Export'
)
foreach ($id in $automationIds) {
    if ($mainXaml -notmatch [regex]::Escape($id)) { Fail "MainWindow accessibility AutomationId missing: $id" }
}
if ($floatingXaml -notmatch 'FACM\.Desktop\.OpenShell') {
    Fail 'Floating desktop entry must keep a stable AutomationId.'
}

foreach ($control in @(
    'RepairNav', 'LeagueNav', 'PersonalizationNav', 'SettingsNav',
    'DiagnosticsSummaryText', 'DiagnosticsRefreshButton', 'DiagnosticsCopyButton', 'DiagnosticsExportButton'
)) {
    if ($mainCode -notmatch ('AutomationProperties\.SetName\s*\(\s*' + [regex]::Escape($control))) {
        Fail "Accessible Name is not provider-driven for: $control"
    }
}
foreach ($control in @(
    'RepairNav', 'LeagueNav', 'PersonalizationNav', 'SettingsNav',
    'DiagnosticsSummaryText', 'DiagnosticsRefreshButton', 'DiagnosticsCopyButton', 'DiagnosticsExportButton'
)) {
    if ($mainCode -notmatch ('AutomationProperties\.SetHelpText\s*\(\s*' + [regex]::Escape($control))) {
        Fail "Accessible HelpText is not provider-driven for: $control"
    }
}
if ($floatingCode -notmatch 'AutomationProperties\.SetName\s*\(\s*FloatingButton' -or
    $floatingCode -notmatch 'AutomationProperties\.SetHelpText\s*\(\s*FloatingButton') {
    Fail 'Floating button must have provider-driven accessible Name and HelpText.'
}

foreach ($key in @(
    'DesktopOpenShellHelp', 'DiagnosticsRefreshHelp',
    'DiagnosticsCopySummaryHelp', 'DiagnosticsExportBundleHelp'
)) {
    if ($text -notmatch ('public const string\s+' + $key + '\s*=') -or
        $text -notmatch ('\[UiTextKeys\.' + $key + '\]\s*=')) {
        Fail "Accessibility UI Text key/default missing: $key"
    }
}

foreach ($control in @('SectionSubtitle', 'OverviewBody', 'StateBody', 'DiagnosticsSubtitle', 'DiagnosticsSummaryText')) {
    $pattern = 'x:Name="' + [regex]::Escape($control) + '"[^>]*TextWrapping="Wrap"'
    if ($mainXaml -notmatch $pattern) { Fail "Text-scaling friendly wrapping missing: $control" }
}
if ($mainXaml -match '<TextBlock[^>]*\sHeight="[0-9]') {
    Fail 'MainWindow user text must not use fixed TextBlock heights that can clip text scaling.'
}

if ($mainXaml -match '\b(?:Tapped|DoubleTapped|PointerPressed|PointerReleased)="') {
    Fail 'MainWindow actionable behavior must not rely on mouse/pointer-only gestures.'
}
if ($floatingXaml -match '\b(?:Tapped|DoubleTapped|PointerPressed|PointerReleased)="') {
    Fail 'Floating entry must use keyboard-capable Button activation, not pointer-only gestures.'
}
if ($mainXaml -match 'IsTabStop="False"') {
    Fail 'MainWindow must not remove actionable controls from keyboard tab navigation.'
}

foreach ($platformResource in @('ApplicationPageBackgroundThemeBrush', 'TextFillColorPrimaryBrush')) {
    if ($tokens -notmatch [regex]::Escape($platformResource)) {
        Fail "Semantic theme contract missing platform resource: $platformResource"
    }
}
if ($tokens -match '#[0-9A-Fa-f]{6,8}') {
    Fail 'FACM semantic tokens must continue to rely on platform theme resources, including High Contrast.'
}

Write-Host 'DPI awareness: PerMonitorV2, PerMonitor'
Write-Host 'DPI smoke scales: 100 / 125 / 150 / 175 / 200 percent'
Write-Host 'Accessibility controls: navigation + diagnostics + floating entry'
Write-Host 'FACM 4.0 DPI/Accessibility contract: SUCCESS'
