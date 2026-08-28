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

$servicePath = Join-Path $Root 'src/FACM.Infrastructure/Mayhem/MayhemBaseAramBalanceService.cs'
$transportPath = Join-Path $Root 'src/FACM.Infrastructure/Mayhem/MayhemCachedPublicDataTransport.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MayhemBaseBalanceSmoke.cs'
$querySmokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MayhemQuerySmoke.cs'

foreach ($path in @($servicePath, $transportPath, $smokePath, $querySmokePath)) {
    if (-not (Test-Path $path)) { Fail "Mayhem base-balance parity file missing: $path" }
}

$service = Get-Content $servicePath -Raw
$transport = Get-Content $transportPath -Raw
$smoke = Get-Content $smokePath -Raw
$querySmoke = Get-Content $querySmokePath -Raw

foreach ($required in @(
    'MayhemBaseAramBalanceService', 'SnapshotCacheDuration = TimeSpan.FromMinutes(10)',
    'SourceBudget = TimeSpan.FromSeconds(2.2)',
    'MayhemPublicResourceKind.AramLocalizedBuild', 'MayhemPublicResourceKind.AramGlobalBuild',
    'damage_dealt', 'damage_taken', 'attack_speed', 'ability_haste', 'healing',
    'shielding', 'tenacity', 'minion_damage', 'resource_regen',
    'unparsed_balance_values', 'patch_mismatch', '旧完整数值已隐藏',
    '基础 ARAM（完整）', '完整平衡暂不可用（不等于无修正）',
    'Mayhem：', '基础平衡：OP.GG ARAM', 'CachedPatchIsUsable', 'DisplayPatch', 'PatchesMatch'
)) {
    if ($service -notmatch [regex]::Escape($required)) {
        Fail "Mayhem base ARAM balance service lost 3.5 behavior: $required"
    }
}
foreach ($forbidden in @(
    'new HttpClient', 'HttpRequestMessage', 'System\.Drawing', 'System\.Windows\.Forms',
    'Microsoft\.UI', 'Clipboard', 'LeagueWriteCommand', 'Process\.'
)) {
    if ($service -match $forbidden) {
        Fail "Mayhem base ARAM balance crossed typed read-only boundary: $forbidden"
    }
}
if ((Count-Matches $service '_transport\.GetAsync') -ne 1) {
    Fail 'Base ARAM service must use one looped typed transport call site for localized/global sources.'
}
if ($service -match 'public\s+[^\r\n]+\([^)]*\b(url|uri)\b') {
    Fail 'Base ARAM service must not accept arbitrary URL/URI input.'
}

foreach ($required in @(
    'AramLocalizedBuild', 'AramGlobalBuild',
    'https://op.gg/zh-cn/lol/modes/aram', 'https://op.gg/lol/modes/aram',
    'MayhemPublicResourceRequest', 'Resolve(MayhemPublicResourceRequest request)'
)) {
    if ($transport -notmatch [regex]::Escape($required)) {
        Fail "Typed public-data transport cannot support base ARAM balance: $required"
    }
}

foreach ($required in @(
    'ValidateCurrentPatchParse', 'ValidatePatchMismatchFailsClosed',
    'ValidateUnknownSignedModifierFailsClosed', 'ValidateLayeredProjection',
    'ValidateTypedFallbackAndSnapshotCacheAsync',
    'snapshot.Status == "syncing"', 'snapshot.ErrorClass == "unparsed_balance_values"',
    'handler.Calls == 2', '基础 ARAM（完整）', 'Mayhem：造成伤害 +3%'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) {
        Fail "Mayhem base-balance deterministic smoke is missing: $required"
    }
}
if ((Count-Matches $querySmoke 'MayhemBaseBalanceSmoke\.RunAsync') -ne 1) {
    Fail 'Mayhem base smoke must execute base-balance parity smoke exactly once.'
}

Write-Host 'Mayhem base ARAM: typed localized/global OP.GG sources + 2.2s per-source budget'
Write-Host 'Mayhem base ARAM: 10m complete-snapshot cache with patch usability guard'
Write-Host 'Mayhem base ARAM: unknown signed modifiers and stale patches fail closed'
Write-Host 'Mayhem base ARAM: base/Mayhem layers remain separate; no numeric stacking'
Write-Host 'Mayhem base-balance deterministic smoke: parse/mismatch/fallback/cache covered offline'
Write-Host 'FACM 4.0 Mayhem base ARAM balance parity contract: SUCCESS'
