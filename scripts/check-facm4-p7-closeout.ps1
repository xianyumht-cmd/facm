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

$mainXamlPath = Join-Path $Root 'src/FACM.App/MainWindow.xaml'
$mainCodePath = Join-Path $Root 'src/FACM.App/MainWindow.xaml.cs'
$personalizationPath = Join-Path $Root 'src/FACM.App/MainWindow.Personalization.cs'
$maintenanceWindowPath = Join-Path $Root 'src/FACM.App/MainWindow.Maintenance.cs'
$appPath = Join-Path $Root 'src/FACM.App/App.xaml.cs'
$appProjectPath = Join-Path $Root 'src/FACM.App/FACM.App.csproj'
$startupCrashPath = Join-Path $Root 'src/FACM.App/StartupCrashDiagnostics.cs'
$appMaintenancePath = Join-Path $Root 'src/FACM.App/App.Maintenance.cs'
$appPersonalizationPath = Join-Path $Root 'src/FACM.App/App.Personalization.cs'
$appLeagueProductPath = Join-Path $Root 'src/FACM.App/App.LeagueWorkbenchProductization.cs'

foreach ($path in @(
    $mainXamlPath, $mainCodePath, $personalizationPath, $maintenanceWindowPath,
    $appPath, $appProjectPath, $startupCrashPath, $appMaintenancePath,
    $appPersonalizationPath, $appLeagueProductPath
)) {
    if (-not (Test-Path $path)) { Fail "P7 closeout file missing: $path" }
}

$mainXaml = Get-Content $mainXamlPath -Raw
$mainCode = Get-Content $mainCodePath -Raw
$personalization = Get-Content $personalizationPath -Raw
$maintenanceWindow = Get-Content $maintenanceWindowPath -Raw
$app = Get-Content $appPath -Raw
$appProject = Get-Content $appProjectPath -Raw
$startupCrash = Get-Content $startupCrashPath -Raw
$appMaintenance = Get-Content $appMaintenancePath -Raw
$appPersonalization = Get-Content $appPersonalizationPath -Raw
$appLeagueProduct = Get-Content $appLeagueProductPath -Raw

# Four product navigation entries must stay stable and point at real feature surfaces.
$nav = @(
    @{ Name = 'repair'; Id = 'FACM.Nav.Repair'; Item = 'RepairNav' },
    @{ Name = 'league'; Id = 'FACM.Nav.League'; Item = 'LeagueNav' },
    @{ Name = 'personalization'; Id = 'FACM.Nav.Personalization'; Item = 'PersonalizationNav' },
    @{ Name = 'settings'; Id = 'FACM.Nav.Settings'; Item = 'SettingsNav' }
)
foreach ($entry in $nav) {
    if ($mainXaml -notmatch [regex]::Escape($entry.Id)) { Fail "P7 primary navigation AutomationId missing: $($entry.Id)" }
    if ($mainXaml -notmatch ('x:Name="' + [regex]::Escape($entry.Item) + '"[^>]*Tag="' + [regex]::Escape($entry.Name) + '"')) {
        Fail "P7 primary navigation tag/owner missing: $($entry.Item) -> $($entry.Name)"
    }
}
foreach ($required in @(
    'CleanupPanel.Visibility = isRepair',
    'LeagueWorkbenchPanel.Visibility = isLeague',
    'DiagnosticsPanel.Visibility = isSettings',
    'InitializePersonalizationSurface()',
    'EnsureCleanupInitializedAsync()',
    'ApplyLeagueRuntimeState()',
    'RefreshDiagnosticsAsync()'
)) {
    if ($mainCode -notmatch [regex]::Escape($required)) { Fail "P7 primary entry is not wired to real behavior: $required" }
}

# Personalization must be a real surface, not the generic overview placeholder.
foreach ($required in @(
    'CreatePersonalizationViewModel', 'BuildPersonalizationPanel',
    'FACM.Personalization.ThemePicker', 'FACM.Personalization.PetPicker',
    'FACM.Personalization.EnablePet', 'FACM.Personalization.RestoreLauncher',
    'FACM.Personalization.ResetDesktopPosition', 'OnPersonalizationClosed'
)) {
    if ($personalization -notmatch [regex]::Escape($required)) { Fail "P7 personalization surface missing: $required" }
}

# More Settings must host the real maintenance surface and dispose its window hooks.
foreach ($required in @(
    'MaintenanceSettingsControl', 'DiagnosticsPanel.Children.Insert(0, control)',
    'ApplyMaintenanceForceLock', 'OnMaintenanceWindowClosed', '.Detach()'
)) {
    if ($maintenanceWindow -notmatch [regex]::Escape($required)) { Fail "P7 maintenance surface missing: $required" }
}

# User-facing primary surfaces may not regress to development placeholders.
foreach ($pair in @(
    @{ Name = 'MainWindow.xaml'; Text = $mainXaml },
    @{ Name = 'MainWindow.xaml.cs'; Text = $mainCode },
    @{ Name = 'MainWindow.Personalization.cs'; Text = $personalization },
    @{ Name = 'MainWindow.Maintenance.cs'; Text = $maintenanceWindow }
)) {
    foreach ($token in @('Coming soon', '暂未实现', '开发测试', '>TODO<', '>placeholder<')) {
        if ($pair.Text -match [regex]::Escape($token)) { Fail "P7 user-visible placeholder remains in $($pair.Name): $token" }
    }
}

# Process-wide desktop / League topology: one owner each, no second session/polling loop.
foreach ($rule in @(
    @{ Pattern = 'new\s+WindowsLeagueTransportSessionSource\s*\('; Count = 1; Name = 'League session source' },
    @{ Pattern = 'new\s+LeagueHttpGateway\s*\('; Count = 1; Name = 'League HTTP gateway' },
    @{ Pattern = 'new\s+LeagueGameflowMonitor\s*\('; Count = 1; Name = 'League gameflow monitor' },
    @{ Pattern = 'new\s+FloatingWindow\s*\('; Count = 1; Name = 'floating F window' }
)) {
    $actual = Count-Matches $app $rule.Pattern
    if ($actual -ne $rule.Count) { Fail "P7 lifecycle owner count drifted for $($rule.Name): expected $($rule.Count), actual $actual" }
}
foreach ($required in @(
    'PrepareMainWindow()', 'ToggleCompactLauncher', 'OnFloatingWindowClosed',
    '_compactLauncher.Close()', '_window.Close()', 'DisposeRuntime()',
    '_matchmakingAutomation?.Dispose()', '_gameflow?.Dispose()', '_leagueGateway?.Dispose()',
    '_httpUpdateManifestSource?.Dispose()', '_diagnostics?.Dispose()'
)) {
    if ($app -notmatch [regex]::Escape($required)) { Fail "P7 App lifecycle missing: $required" }
}

# Startup access-denied diagnostics must not depend on the normal logs sink. The first-chance record
# is bounded to the startup window, redacts stable/local paths, and falls back to the portable root.
foreach ($required in @(
    '[ModuleInitializer]', 'FirstChanceException', 'UnauthorizedAccessException',
    'UnhandledException', 'StartupWindow = TimeSpan.FromMinutes(2)',
    'runtime", "recovery", CrashFileName', 'startup-crash.json',
    'exception.HResult', 'exception.ToString()', '%FACM_ROOT%', '%USERPROFILE%', '%TEMP%'
)) {
    if ($startupCrash -notmatch [regex]::Escape($required)) { Fail "P7 startup crash diagnostic contract missing: $required" }
}
foreach ($forbidden in @('HttpClient', 'Registry.', 'Process.Start(', 'File.Delete(path)', 'Directory.Delete(')) {
    if ($startupCrash -match [regex]::Escape($forbidden)) { Fail "P7 startup crash diagnostics may not add side effects: $forbidden" }
}
foreach ($required in @(
    '<Version>4.0.0</Version>', '<AssemblyVersion>4.0.0.0</AssemblyVersion>', '<FileVersion>4.0.0.0</FileVersion>'
)) {
    if ($appProject -notmatch [regex]::Escape($required)) { Fail "P7 FACM.App candidate version metadata missing: $required" }
}

# Maintenance/single-instance shutdown is explicit and idempotent.
foreach ($required in @(
    'WindowsSingleInstanceGate', 'SingleInstanceDisposition.Primary',
    'DisposeMaintenanceRuntime', '_maintenanceCenter?.Dispose()',
    '_httpAnnouncementSource?.Dispose()', '_singleInstanceGate?.Dispose()'
)) {
    if ($appMaintenance -notmatch [regex]::Escape($required)) { Fail "P7 maintenance lifecycle missing: $required" }
}

# Desktop pet stays process-scoped and is tied to the floating entry lifecycle.
foreach ($required in @(
    'WindowsVPetRuntime', 'AttachDesktopPetCloseHook', 'OnDesktopPetFloatingWindowClosed',
    '_desktopPetRuntime?.Dispose()', '_desktopPetRuntime = null'
)) {
    if ($appPersonalization -notmatch [regex]::Escape($required)) { Fail "P7 personalization lifecycle missing: $required" }
}

# League automation/hotkey owners are process scoped, use the shared gameflow/gateway, and dispose on exit.
foreach ($required in @(
    'if (_recommendedAutoApply is not null) return',
    'if (_leagueEfficiencyRuntime is not null) return',
    'if (_postGameAutomation is null)',
    'AppDomain.CurrentDomain.ProcessExit += OnLeagueRecommendedAutoApplyProcessExit',
    'AppDomain.CurrentDomain.ProcessExit += OnLeagueEfficiencyProcessExit',
    'AppDomain.CurrentDomain.ProcessExit += OnLeaguePostGameProcessExit',
    '_recommendedAutoApply?.Dispose()', '_leagueEfficiencyRuntime?.Dispose()', '_postGameAutomation?.Dispose()'
)) {
    if ($appLeagueProduct -notmatch [regex]::Escape($required)) { Fail "P7 League product lifecycle missing: $required" }
}
if ((Count-Matches $appLeagueProduct 'new\s+LeagueRecommendedAutoApplyService\s*\(') -ne 1) {
    Fail 'P7 must compose at most one process-wide recommended auto-apply owner.'
}
if ((Count-Matches $appLeagueProduct 'new\s+LeagueEfficiencyRuntime\s*\(') -ne 1) {
    Fail 'P7 must compose at most one process-wide League efficiency/hotkey owner.'
}
if ((Count-Matches $appLeagueProduct 'new\s+LeaguePostGameAutomationService\s*\(') -ne 1) {
    Fail 'P7 must compose at most one process-wide post-game automation owner.'
}

Write-Host 'P7 primary navigation: Repair / League / Personalization / Settings -> real surfaces'
Write-Host 'P7 placeholder audit: no user-visible development placeholder on primary surfaces'
Write-Host 'P7 lifecycle: one floating owner + one League session/gateway/gameflow owner'
Write-Host 'P7 lifecycle: maintenance/single-instance, PetHost and process-scoped League automation dispose paths retained'
Write-Host 'P7 startup diagnostics: access-denied first-chance/unhandled fallback + 4.0.0.0 candidate version metadata retained'
Write-Host 'FACM 4.0 P7 entry/lifecycle closeout contract: SUCCESS'
