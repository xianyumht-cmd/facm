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
    if (-not (Test-Path $path)) { Fail "Gate 9 contract file missing: $RelativePath" }
    return Get-Content $path -Raw
}

function Count-Matches([string]$Text, [string]$Pattern) {
    return @([regex]::Matches($Text, $Pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)).Count
}

$core = Read-Required 'src/FACM.Core/Observability/DiagnosticsCenterContracts.cs'
$services = Read-Required 'src/FACM.Infrastructure/Observability/DiagnosticsCenterServices.cs'
$viewModel = Read-Required 'src/FACM.App/ViewModels/DiagnosticsCenterViewModel.cs'
$mainCode = Read-Required 'src/FACM.App/MainWindow.xaml.cs'
$mainXaml = Read-Required 'src/FACM.App/MainWindow.xaml'
$logViewer = Read-Required 'src/FACM.App/MainWindow.LogViewer.cs'
$maintenanceWindow = Read-Required 'src/FACM.App/MainWindow.Maintenance.cs'
$tray = Read-Required 'src/FACM.App/App.Tray.cs'
$app = Read-Required 'src/FACM.App/App.xaml.cs'
$text = Read-Required 'src/FACM.Core/Text/UiTextContracts.cs'

foreach ($required in @(
    'DiagnosticsExportPolicy', 'DiagnosticsSnapshot', 'DiagnosticsExportReceipt',
    'IDiagnosticsSnapshotSource', 'IDiagnosticsBundleExporter',
    'DiagnosticsExportSanitizer', 'DiagnosticsSummaryFormatter'
)) {
    if ($core -notmatch [regex]::Escape($required)) { Fail "Core diagnostics contract missing: $required" }
}

foreach ($marker in @('Basic|Bearer', 'WindowsPathRegex', 'UncPathRegex', 'DiagnosticRedactor')) {
    if ($core -notmatch [regex]::Escape($marker)) { Fail "Export sanitizer is missing defense: $marker" }
}

foreach ($forbidden in @(
    'ILeagueWriteGateway', 'LeagueWriteCommand', 'LeagueWriteCapability',
    'ICleanupExecutor', 'CleanupApplicationService',
    'IUpdateInstaller', 'HttpClient', 'Process\.', 'Registry\.'
)) {
    if ($services -match $forbidden) { Fail "Diagnostics infrastructure gained business/network write capability: $forbidden" }
}

foreach ($forbidden in @('EnumerateFiles', 'GetFiles\s*\(', 'EnumerateDirectories', 'GetDirectories\s*\(')) {
    if ($services -match $forbidden) { Fail "Diagnostics reader must not enumerate arbitrary directories: $forbidden" }
}
if ($services -notmatch '_currentLogPath \+ "\.1"' -or $services -notmatch '_currentLogPath') {
    Fail 'Diagnostics reader must use only the current log and its .1 rotation.'
}

foreach ($entry in @('summary.txt', 'events.jsonl', 'manifest.json')) {
    if ((Count-Matches $services ([regex]::Escape('"' + $entry + '"'))) -ne 1) {
        Fail "Diagnostics ZIP entry must be declared exactly once: $entry"
    }
}
if ($services -notmatch 'EntryCount,\s*3' -and $services -notmatch 'finalPath,\s*bundleBytes,\s*3') {
    Fail 'Diagnostics exporter must report exactly three allowlisted entries.'
}

$ui = $viewModel + "`n" + $mainCode
foreach ($forbidden in @(
    'System\.IO', 'File\.', 'Directory\.', 'ZipArchive', 'ZipFile',
    'FACM\.Infrastructure', 'FACM\.Platform\.Windows',
    'ILeagueWriteGateway', 'LeagueWriteCommand', 'ICleanupExecutor', 'IUpdateInstaller'
)) {
    if ($ui -match $forbidden) { Fail "Diagnostics UI crossed its Core intent boundary: $forbidden" }
}
foreach ($required in @('IDiagnosticsSnapshotSource', 'IDiagnosticsBundleExporter', 'DiagnosticsSummaryFormatter.Format')) {
    if ($viewModel -notmatch [regex]::Escape($required)) { Fail "Diagnostics ViewModel missing Core contract: $required" }
}

foreach ($name in @(
    'DiagnosticsPanel', 'DiagnosticsSummaryText', 'DiagnosticsRefreshButton',
    'DiagnosticsCopyButton', 'DiagnosticsExportButton', 'DiagnosticsStatus'
)) {
    if ((Count-Matches $mainXaml ('x:Name="' + [regex]::Escape($name) + '"')) -ne 1) {
        Fail "Diagnostics Center XAML surface missing or duplicated: $name"
    }
}

foreach ($name in @(
    'LogViewerSurface', 'LogSearchBox', 'LogDomainFilter', 'LogOutcomeFilter',
    'LogRefreshButton', 'LogOpenFolderButton', 'LogCopyPathButton', 'LogRowsPanel'
)) {
    if ((Count-Matches $mainXaml ('x:Name="' + [regex]::Escape($name) + '"')) -ne 1) {
        Fail "Structured log XAML surface missing or duplicated: $name"
    }
}
foreach ($required in @(
    'RefreshEventsAsync', 'IReadOnlyList<DiagnosticEvent> Events', 'LogPath', 'LogDirectory',
    'LaunchFolderPathAsync', 'Clipboard.SetContent', 'OrderByDescending', 'Take(120)',
    'LogsTime', 'LogsDomain', 'LogsOperation', 'LogsOutcome', 'LogsDuration'
)) {
    if ($viewModel -notmatch [regex]::Escape($required) -and
        $logViewer -notmatch [regex]::Escape($required) -and
        $mainCode -notmatch [regex]::Escape($required)) {
        Fail "Structured log surface contract missing: $required"
    }
}
foreach ($forbidden in @('WindowsLogFileOpener', 'OpenLogAsync', 'Process\.Start')) {
    if ($logViewer -match $forbidden) { Fail "Structured log UI may not shell-open the raw JSONL file: $forbidden" }
}
foreach ($required in @('OpenLogRequested', 'OpenStructuredLogSurface')) {
    if ($maintenanceWindow -notmatch [regex]::Escape($required) -and $tray -notmatch [regex]::Escape($required)) {
        Fail "Primary operation-log entry is not routed to the internal structured log surface: $required"
    }
}

if ((Count-Matches $app 'new\s+FileDiagnosticsSnapshotSource\s*\(') -ne 1) {
    Fail 'App composition must create exactly one read-only diagnostics snapshot source.'
}
if ((Count-Matches $app 'new\s+DiagnosticsBundleExporter\s*\(') -ne 1) {
    Fail 'App composition must create exactly one diagnostics bundle exporter.'
}
if ($app -notmatch 'Path\.Combine\(layout\.LogsDirectory,\s*"facm4-events\.jsonl"\)') {
    Fail 'Diagnostics source must reuse the bounded FACM JSONL path.'
}
if ($app -notmatch 'Path\.Combine\(layout\.RuntimeDirectory,\s*"diagnostics"\)') {
    Fail 'Diagnostics export directory must be a stable runtime child, not a UI-provided arbitrary path.'
}

foreach ($constant in @(
    'DiagnosticsTitle', 'DiagnosticsSubtitle', 'DiagnosticsSummaryLabel',
    'DiagnosticsRefresh', 'DiagnosticsCopySummary', 'DiagnosticsExportBundle',
    'DiagnosticsStatusReady', 'DiagnosticsStatusRefreshed', 'DiagnosticsStatusCopied',
    'DiagnosticsStatusExported', 'DiagnosticsStatusFailed'
)) {
    if ($text -notmatch ('public const string\s+' + [regex]::Escape($constant) + '\s*=')) {
        Fail "Diagnostics UI Text key missing: $constant"
    }
    if ($text -notmatch ('\[UiTextKeys\.' + [regex]::Escape($constant) + '\]\s*=')) {
        Fail "Diagnostics UI Text default missing: $constant"
    }
}
foreach ($constant in @(
    'LogsTitle', 'LogsSearch', 'LogsAllDomains', 'LogsAllOutcomes', 'LogsRefresh',
    'LogsOpenFolder', 'LogsCopyPath', 'LogsTime', 'LogsDomain', 'LogsOperation',
    'LogsOutcome', 'LogsDuration', 'LogsNoEvents', 'LogsRefreshed', 'LogsPathCopied'
)) {
    if ($text -notmatch ('public const string\s+' + [regex]::Escape($constant) + '\s*=')) {
        Fail "Structured log UI Text key missing: $constant"
    }
    if ($text -notmatch ('\[UiTextKeys\.' + [regex]::Escape($constant) + '\]\s*=')) {
        Fail "Structured log UI Text default missing: $constant"
    }
}

Write-Host 'Diagnostics input allowlist: facm4-events.jsonl + .1 + in-memory Product State'
Write-Host 'Diagnostics ZIP allowlist: summary.txt / events.jsonl / manifest.json'
Write-Host 'FACM 4.0 Diagnostics Center contract: SUCCESS'
