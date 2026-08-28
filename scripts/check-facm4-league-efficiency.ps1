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

$corePath = Join-Path $Root 'src/FACM.Core/League/LeagueEfficiency.cs'
$coreRuntimePath = Join-Path $Root 'src/FACM.Core/League/LeagueEfficiencyRuntime.cs'
$platformPath = Join-Path $Root 'src/FACM.Platform.Windows/League/WindowsLeagueEfficiencyServices.cs'
$runtimePath = Join-Path $Root 'src/FACM.Infrastructure/League/LeagueEfficiencyRuntime.cs'
$settingsPath = Join-Path $Root 'src/FACM.Core/Settings/Settings2.cs'
$compositionPath = Join-Path $Root 'src/FACM.App/App.LeagueWorkbenchProductization.cs'
$uiPath = Join-Path $Root 'src/FACM.App/MainWindow.LeagueEfficiency.cs'
$runtimeUiPath = Join-Path $Root 'src/FACM.App/MainWindow.LeagueWorkbenchRuntime.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/LeagueEfficiencySmoke.cs'
$smokeProgramPath = Join-Path $Root 'src/FACM.FoundationSmoke/Program.cs'

foreach ($path in @(
    $corePath, $coreRuntimePath, $platformPath, $runtimePath, $settingsPath,
    $compositionPath, $uiPath, $runtimeUiPath, $smokePath, $smokeProgramPath
)) {
    if (-not (Test-Path $path)) { Fail "League efficiency contract file missing: $path" }
}

$core = Get-Content $corePath -Raw
$coreRuntime = Get-Content $coreRuntimePath -Raw
$platform = Get-Content $platformPath -Raw
$runtime = Get-Content $runtimePath -Raw
$settings = Get-Content $settingsPath -Raw
$composition = Get-Content $compositionPath -Raw
$ui = Get-Content $uiPath -Raw
$runtimeUi = Get-Content $runtimeUiPath -Raw
$smoke = Get-Content $smokePath -Raw
$smokeProgram = Get-Content $smokeProgramPath -Raw

foreach ($required in @(
    'LeagueEfficiencyAction', 'ExitGame', 'CloseLobby', 'LeagueHotkeyModifiers',
    'LeagueHotkeyBinding', 'TryParse', 'ILeagueEfficiencyActionService',
    'ILeagueGlobalHotkeyService', 'LeagueGlobalHotkeyPressedEventArgs'
)) {
    if ($core -notmatch [regex]::Escape($required)) {
        Fail "League efficiency Core contract is missing: $required"
    }
}
foreach ($forbidden in @(
    'System\.Diagnostics', 'System\.Runtime\.InteropServices', 'FACM\.Platform',
    'Microsoft\.UI', 'Windows\.System', 'RegisterHotKey', 'Process\.'
)) {
    if ($core -match $forbidden) {
        Fail "League efficiency Core contract leaked platform/UI detail: $forbidden"
    }
}

foreach ($required in @(
    'LeagueEfficiencyRuntimeState', 'ILeagueEfficiencyRuntime', 'InitializeAsync',
    'UpdateBindingsAsync', 'RunActionAsync', 'StateChanged'
)) {
    if ($coreRuntime -notmatch [regex]::Escape($required)) {
        Fail "League efficiency runtime Core boundary is missing: $required"
    }
}
foreach ($forbidden in @(
    'System\.Diagnostics', 'System\.Runtime\.InteropServices', 'FACM\.Infrastructure',
    'FACM\.Platform', 'Microsoft\.UI', 'RegisterHotKey', 'Process\.'
)) {
    if ($coreRuntime -match $forbidden) {
        Fail "League efficiency runtime Core boundary leaked implementation detail: $forbidden"
    }
}

foreach ($name in @(
    '"League of Legends(TM)"', '"League of Legends"',
    '"LeagueClient"', '"LeagueClientUx"', '"LeagueClientUxRender"'
)) {
    if ($platform -notmatch [regex]::Escape($name)) {
        Fail "Windows efficiency process allowlist is missing: $name"
    }
}
foreach ($required in @(
    'TryKillIfStillMatches', 'Process.GetProcessById', 'process.ProcessName', 'process.Kill()',
    'RegisterHotKey', 'UnregisterHotKey', 'ModNoRepeat',
    'FACM.LeagueEfficiency.Hotkeys', 'HwndMessage', 'ApplyRequest',
    'UnregisterAll(_active)', 'Restore(hwnd, previous)', 'PostQuitMessage'
)) {
    if ($platform -notmatch [regex]::Escape($required)) {
        Fail "Windows League efficiency adapter is missing safety/lifecycle behavior: $required"
    }
}
foreach ($forbidden in @(
    'System\.Windows\.Forms', 'Application\.Run', 'Task\.Delay',
    'new\s+LeagueGameflowMonitor', 'WindowsLeagueTransportSessionSource',
    'ProcessLockfileLeagueSessionDiscovery'
)) {
    if ($platform -match $forbidden) {
        Fail "Windows League efficiency adapter crossed runtime/UI boundary: $forbidden"
    }
}

foreach ($required in @(
    'ISettings2Repository', 'ILeagueEfficiencyActionService', 'ILeagueGlobalHotkeyService',
    'SettingsLoadOrigin.RecoveredLastKnownGood', 'SettingsLoadOrigin.RecoveryDefaults',
    'TryParseBindings', '_hotkeys.TryApply', '_settings.SaveAsync',
    'HotkeyPressed += OnHotkeyPressed', 'RunHotkeyActionSafelyAsync',
    'LeagueEfficiencyAction.ExitGame', 'LeagueEfficiencyAction.CloseLobby'
)) {
    if ($runtime -notmatch [regex]::Escape($required)) {
        Fail "League efficiency process runtime is missing behavior: $required"
    }
}
foreach ($forbidden in @(
    'System\.Diagnostics', 'System\.Runtime\.InteropServices', 'Microsoft\.UI',
    'RegisterHotKey', 'Process\.', 'LeagueGameflowMonitor', 'Task\.Delay'
)) {
    if ($runtime -match $forbidden) {
        Fail "League efficiency process runtime leaked platform/polling detail: $forbidden"
    }
}

foreach ($required in @('ExitGameHotkey', 'CloseLobbyHotkey')) {
    if ($settings -notmatch [regex]::Escape($required)) {
        Fail "Settings 2.0 is missing League efficiency field: $required"
    }
}

foreach ($required in @(
    'EnsureLeagueEfficiencyRuntime', 'new LeagueEfficiencyRuntime',
    'new WindowsLeagueEfficiencyActionService()', 'new WindowsLeagueGlobalHotkeyService()',
    'GetLeagueEfficiencyRuntime', 'ProcessExit'
)) {
    if ($composition -notmatch [regex]::Escape($required)) {
        Fail "App composition is missing League efficiency process ownership: $required"
    }
}
if ((Count-Matches $composition 'new\s+LeagueEfficiencyRuntime\s*\(') -ne 1) {
    Fail 'App composition must create exactly one League efficiency runtime.'
}
if ((Count-Matches $composition 'new\s+WindowsLeagueGlobalHotkeyService\s*\(') -ne 1) {
    Fail 'App composition must create exactly one Windows global-hotkey owner.'
}

foreach ($id in @(
    'FACM.League.ExitGameHotkey', 'FACM.League.CloseLobbyHotkey',
    'FACM.League.SaveEfficiencyHotkeys', 'FACM.League.ClearEfficiencyHotkeys',
    'FACM.League.EfficiencyStatus'
)) {
    if ($ui -notmatch [regex]::Escape($id)) {
        Fail "League efficiency WinUI automation id is missing: $id"
    }
}
foreach ($required in @(
    'ILeagueEfficiencyRuntime', 'GetLeagueEfficiencyRuntime', 'UpdateBindingsAsync',
    'StateChanged += OnLeagueEfficiencyStateChanged', 'StateChanged -= OnLeagueEfficiencyStateChanged'
)) {
    if ($ui -notmatch [regex]::Escape($required)) {
        Fail "League efficiency WinUI intent boundary is missing: $required"
    }
}
foreach ($forbidden in @(
    'FACM\.Platform', 'System\.Diagnostics', 'System\.Runtime\.InteropServices',
    'Process\.', 'RegisterHotKey', 'UnregisterHotKey', 'user32'
)) {
    if ($ui -match $forbidden) {
        Fail "League efficiency WinUI crossed its intent boundary: $forbidden"
    }
}
foreach ($required in @('InitializeLeagueEfficiencySurface()', 'DisposeLeagueEfficiencySurface()')) {
    if ($runtimeUi -notmatch [regex]::Escape($required)) {
        Fail "League Workbench lifecycle is missing efficiency surface hook: $required"
    }
}

foreach ($required in @(
    'ValidateHotkeyGrammar', 'ValidateInitializationAndPersistenceAsync',
    'ValidateFailedRegistrationDoesNotPersistAsync', 'ValidateRecoveryIsReadOnlyAsync',
    'ValidateHotkeyDispatchAsync', 'bare letter rejected', 'duplicate action hotkeys rejected',
    'failed registration must not persist settings', 'recovery session never overwrites primary settings'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) {
        Fail "League efficiency deterministic smoke is missing: $required"
    }
}
if ((Count-Matches $smokeProgram 'LeagueEfficiencySmoke\.RunAsync') -ne 1) {
    Fail 'Foundation smoke must register League efficiency exactly once.'
}

Write-Host 'League efficiency Core: platform-neutral hotkey grammar + two narrow actions'
Write-Host 'League efficiency Windows: exact process allowlists + PID/name revalidation + transactional RegisterHotKey owner'
Write-Host 'League efficiency runtime: Settings 2.0 persistence + recovery read-only + serialized action dispatch'
Write-Host 'League efficiency WinUI: intent-only edit/save/disable controls'
Write-Host 'League efficiency deterministic smoke: grammar / rollback / recovery / dispatch'
Write-Host 'FACM 4.0 League Efficiency contract: SUCCESS'
