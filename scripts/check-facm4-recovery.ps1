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
    if (-not (Test-Path $path)) { Fail "Gate 11 contract file missing: $RelativePath" }
    return Get-Content $path -Raw
}

$featureCore = Read-Required 'src/FACM.Core/Recovery/FeatureFlags.cs'
$recoveryCore = Read-Required 'src/FACM.Core/Recovery/RecoveryContracts.cs'
$featureSource = Read-Required 'src/FACM.Infrastructure/Recovery/FeatureKillSwitchFileSource.cs'
$recoveryStore = Read-Required 'src/FACM.Infrastructure/Recovery/JsonRecoveryStateStore.cs'
$settingsRecovery = Read-Required 'src/FACM.Infrastructure/Settings/RecoveringSettings2Repository.cs'
$settingsCore = Read-Required 'src/FACM.Core/Settings/Settings2.cs'
$layout = Read-Required 'src/FACM.Core/Runtime/RuntimePathLayout.cs'
$app = Read-Required 'src/FACM.App/App.xaml.cs'
$smoke = Read-Required 'src/FACM.FoundationSmoke/Gate11Smoke.cs'

$approved = @(
    'CleanupExecute', 'UpdateCheck', 'UpdateInstall', 'DiagnosticsExport',
    'LeagueApplyMySelection', 'LeagueCreatePerkPage', 'LeagueUpdatePerkPage', 'LeagueSetCurrentPerkPage'
)
foreach ($capability in $approved) {
    if ($featureCore -notmatch ('FacmFeatureCapability\.' + [regex]::Escape($capability))) {
        Fail "Approved Gate 11 feature capability missing: $capability"
    }
}
if ($featureCore -match 'Enum\.GetValues') {
    Fail 'Feature baseline must be explicit; new enum values may not become enabled automatically.'
}
if ($featureCore -notmatch 'Where\s*\(\s*capability\s*=>\s*!killSwitch\.Disables\(capability\)\s*\)') {
    Fail 'Feature evaluator must derive effective policy by subtracting disabled capabilities.'
}
if ($featureCore -notmatch 'IsNoMorePermissive') {
    Fail 'Feature policy monotonicity check is missing.'
}
foreach ($wrapper in @(
    'FeatureGatedLeagueWriteGateway', 'FeatureGatedCleanupExecutor',
    'FeatureGatedUpdateManifestSource', 'FeatureGatedUpdateInstaller',
    'FeatureGatedDiagnosticsBundleExporter'
)) {
    if ($featureCore -notmatch ('class\s+' + $wrapper)) { Fail "Feature-gated wrapper missing: $wrapper" }
}
if ($featureCore -match 'https?://' -or $featureCore -match '\bHttpClient\b' -or $featureCore -match '\bProcess\.Start\b') {
    Fail 'Core feature policy must remain platform/network/process neutral.'
}

if ($featureSource -notmatch 'property\.Name\s+is\s+not\s+\("schemaVersion"\s+or\s+"disabled"\)') {
    Fail 'Kill-switch parser must reject fields other than schemaVersion + disabled.'
}
if ($featureSource -notmatch 'FeatureKillSwitch\.DisableAllApproved\(\)') {
    Fail 'Malformed/unknown kill-switch input must fail closed by disabling all approved capabilities.'
}
if ($featureSource -match 'Directory\.Enumerate' -or $featureSource -match 'Directory\.GetFiles') {
    Fail 'Feature kill-switch source must read one explicit file, not enumerate directories.'
}

foreach ($phase in @('Clean', 'Starting', 'Running', 'Failed', 'Recovering')) {
    if ($recoveryCore -notmatch ('\b' + $phase + '\b')) { Fail "Recovery phase missing: $phase" }
}
if ($recoveryCore -notmatch 'previous-start-incomplete') {
    Fail 'Recovery state machine must detect an incomplete previous Starting state.'
}
if ($recoveryCore -notmatch 'ValidatedReceipt' -or $recoveryCore -notmatch 'OldVersionPreserved') {
    Fail 'Update recovery must require validated receipt and explicit old-version preservation evidence.'
}
if ($recoveryCore -match '\bFile\.' -or $recoveryCore -match '\bDirectory\.' -or $recoveryCore -match '\bProcess\.') {
    Fail 'Core recovery state machine must stay platform/file/process neutral.'
}

foreach ($required in @('MaxDocumentBytes = 64 * 1024', 'FileOptions.Asynchronous | FileOptions.WriteThrough', 'Flush(flushToDisk: true)', 'File.Move(temp, _path, overwrite: true)')) {
    if ($recoveryStore -notmatch [regex]::Escape($required)) { Fail "Recovery atomic-store invariant missing: $required" }
}
if ($recoveryStore -notmatch 'RecoveryLoadOrigin\.Malformed') {
    Fail 'Malformed recovery metadata must map to a safe explicit origin.'
}

foreach ($origin in @('RecoveredLastKnownGood', 'RecoveryDefaults')) {
    if ($settingsCore -notmatch $origin -or $settingsRecovery -notmatch $origin) {
        Fail "Settings recovery origin missing: $origin"
    }
}
if ($settingsRecovery -notmatch 'catch\s*\(InvalidDataException\)') {
    Fail 'Settings recovery must wrap strict invalid-data failure rather than weakening strict parsing.'
}
if ($settingsRecovery -notmatch 'safeDefaults\.Online\.AutoUpdateEnabled\s*=\s*false') {
    Fail 'Recovery defaults must disable automatic update checks.'
}
if ($settingsRecovery -notmatch 'Settings2Validator\.ThrowIfInvalid') {
    Fail 'Settings LKG/default recovery must remain validator-backed.'
}

foreach ($pathProperty in @('RecoveryDirectory', 'RecoveryStatePath', 'Settings2LastKnownGoodPath', 'FeatureKillSwitchPath')) {
    if ($layout -notmatch ('public string\s+' + $pathProperty)) { Fail "Stable recovery runtime path missing: $pathProperty" }
}
if ($layout -match 'AppContext\.BaseDirectory') {
    Fail 'Recovery paths must derive from distribution runtime layout, never AppContext.BaseDirectory.'
}

foreach ($composition in @(
    'FeatureKillSwitchFileSource\(layout\.FeatureKillSwitchPath\)',
    'FeaturePolicyEvaluator\.Evaluate',
    'RecoveringSettings2Repository',
    'JsonSettings2RecoveryStore\(layout\.Settings2LastKnownGoodPath\)',
    'JsonRecoveryStateStore\(layout\.RecoveryStatePath',
    'FeatureGatedUpdateManifestSource',
    'FeatureGatedDiagnosticsBundleExporter'
)) {
    if ($app -notmatch $composition) { Fail "Gate 11 App composition missing: $composition" }
}
if ($app -match 'exception\.Message') {
    Fail 'Recovery diagnostics must not persist arbitrary exception messages.'
}

foreach ($evidence in @(
    'FutureMagicWriter', 'EnabledCapabilities.Count', 'previous-start-incomplete',
    'RecoveredLastKnownGood', 'RecoveryDefaults', 'corrupt primary must remain untouched',
    'unvalidated receipt must block replacement', 'failed replacement keeps old version'
)) {
    if ($smoke -notmatch [regex]::Escape($evidence)) { Fail "Gate11Smoke evidence missing: $evidence" }
}

Write-Host 'Feature policy: explicit baseline + disable-only kill switch'
Write-Host 'Recovery: bounded atomic metadata + validated Settings LKG'
Write-Host 'Update recovery: validated receipt + old-version preservation required'
Write-Host 'FACM 4.0 Recovery/Feature Flags contract: SUCCESS'
