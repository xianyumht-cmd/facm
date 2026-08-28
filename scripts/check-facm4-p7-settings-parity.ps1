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

$legacyPath = Join-Path $Root 'src/FACM/Services/AppSettings.cs'
$contractPath = Join-Path $Root 'src/FACM.Core/Settings/LegacySettingsContract.cs'
$codecPath = Join-Path $Root 'src/FACM.Core/Settings/LegacySettings.cs'
$settings2SmokePath = Join-Path $Root 'src/FACM.FoundationSmoke/Settings2Smoke.cs'
$paritySmokePath = Join-Path $Root 'src/FACM.FoundationSmoke/LegacySettingsParitySmoke.cs'
$programPath = Join-Path $Root 'src/FACM.FoundationSmoke/Program.cs'

foreach ($path in @($legacyPath, $contractPath, $codecPath, $settings2SmokePath, $paritySmokePath, $programPath)) {
    if (-not (Test-Path $path)) { Fail "P7 settings parity file missing: $path" }
}

$legacy = Get-Content $legacyPath -Raw
$contract = Get-Content $contractPath -Raw
$codec = Get-Content $codecPath -Raw
$settings2Smoke = Get-Content $settings2SmokePath -Raw
$paritySmoke = Get-Content $paritySmokePath -Raw
$program = Get-Content $programPath -Raw

$expected = @(
    'BallX',
    'BallY',
    'GamePath',
    'AutoUpdateEnabled',
    'LastAnnouncementId',
    'ThemeId',
    'PetStyleId',
    'AnimalPetEnabled',
    'LeagueAutoApplyRecommended',
    'LeagueExitGameHotkey',
    'LeagueCloseLobbyHotkey',
    'LeagueAutoHonorTeammateEnabled',
    'LeagueAutoReturnLobbyEnabled',
    'LeagueAutoMatchmakingEnabled',
    'LeagueAutoAcceptEnabled'
)

$buildMatch = [regex]::Match(
    $legacy,
    'internal\s+string\[\]\s+BuildLines\(\)(?<body>.*?)internal\s+static\s+void\s+ApplyLineForSmokeTest',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $buildMatch.Success) { Fail 'Could not isolate production 3.5.15 AppSettings.BuildLines().' }

$legacyKeys = @([regex]::Matches($buildMatch.Groups['body'].Value, '"(?<key>[A-Za-z0-9]+)="') | ForEach-Object {
    $_.Groups['key'].Value
})
if ($legacyKeys.Count -ne $expected.Count) {
    Fail "Production legacy BuildLines key count drifted: expected $($expected.Count), actual $($legacyKeys.Count)."
}
if ([string]::Join('|', $legacyKeys) -ne [string]::Join('|', $expected)) {
    Fail "Production legacy BuildLines key ordering drifted. actual=$([string]::Join(',', $legacyKeys))"
}

if ($contract -notmatch [regex]::Escape('ProductionBaseline = "FACM 3.5.15"')) {
    Fail 'LegacySettingsContract is not frozen to FACM 3.5.15.'
}
if ($contract -notmatch [regex]::Escape('KeyCount = 15')) {
    Fail 'LegacySettingsContract key count is not frozen to 15.'
}
foreach ($key in $expected) {
    if ($contract -notmatch ('public\s+const\s+string\s+' + [regex]::Escape($key) + '\s*=\s*"' + [regex]::Escape($key) + '"')) {
        Fail "LegacySettingsContract is missing the production key: $key"
    }
    if ((Count-Matches $codec ('LegacySettingsContract\.' + [regex]::Escape($key))) -lt 2) {
        Fail "LegacySettingsCodec is not binding both parse/serialize behavior to the frozen key: $key"
    }
}

foreach ($required in @(
    'MigratesAllLegacyKeysAndPreservesLegacyAsync',
    'Equal(legacyText, files.Get(legacyPath)',
    'SettingsLoadOrigin.MigratedLegacy',
    'SettingsLoadOrigin.ExistingV2',
    'RejectsCorruptionAndUnsupportedVersionAsync',
    'FailedAtomicWritePreservesExistingAsync'
)) {
    if ($settings2Smoke -notmatch [regex]::Escape($required)) {
        Fail "Settings2 migration smoke lost a required invariant: $required"
    }
}

foreach ($required in @(
    'Production3515Keys',
    'LegacySettingsContract.KeyCount',
    'LegacySettingsContract.OrderedKeys.SequenceEqual',
    'LegacySettingsCodec.Serialize',
    'serializedKeys.SequenceEqual',
    'LegacySettingsCodec.Parse(serialized)'
)) {
    if ($paritySmoke -notmatch [regex]::Escape($required)) {
        Fail "P7 production-key parity smoke is missing: $required"
    }
}
if ((Count-Matches $program 'LegacySettingsParitySmoke\.RunAsync') -ne 1) {
    Fail 'Foundation smoke must execute LegacySettingsParitySmoke exactly once.'
}

foreach ($forbidden in @('Microsoft\.UI', 'System\.Windows\.Forms', 'HttpClient', 'Process\.')) {
    if ($contract -match $forbidden) {
        Fail "LegacySettingsContract leaked UI/network/process detail: $forbidden"
    }
}

Write-Host 'P7 settings legacy source: production AppSettings.BuildLines = exact 15-key FACM 3.5.15 contract'
Write-Host 'P7 settings Core: parse + serialize bind to one ordered LegacySettingsContract'
Write-Host 'P7 settings migration: legacy preserved, validated V2 persisted, corruption/atomic failure remain fail-safe'
Write-Host 'P7 settings deterministic smoke: production key order + legacy round-trip executes in Foundation'
Write-Host 'FACM 4.0 P7 settings parity contract: SUCCESS'
