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

$policyPath = Join-Path $Root 'src/FACM.Core/Mayhem/MayhemAugmentDecisionPolicy.cs'
$servicePath = Join-Path $Root 'src/FACM.Infrastructure/Mayhem/MayhemAugmentEnrichmentService.cs'
$transportPath = Join-Path $Root 'src/FACM.Infrastructure/Mayhem/MayhemCachedPublicDataTransport.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MayhemAugmentSmoke.cs'
$querySmokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MayhemQuerySmoke.cs'

foreach ($path in @($policyPath, $servicePath, $transportPath, $smokePath, $querySmokePath)) {
    if (-not (Test-Path $path)) { Fail "Mayhem augment parity file missing: $path" }
}

$policy = Get-Content $policyPath -Raw
$service = Get-Content $servicePath -Raw
$transport = Get-Content $transportPath -Raw
$smoke = Get-Content $smokePath -Raw
$querySmoke = Get-Content $querySmokePath -Raw

foreach ($required in @(
    'MayhemAugmentDecisionPolicy', 'StableRouteTitle = "稳定赢法"',
    'HighWinRouteTitle = "高上限玩法"', 'PopularRouteTitle = "热门好上手"',
    'StableWinWeight = 0.72d', 'StablePickWeight = 0.28d',
    'BuildRoutes', 'HashSet<string>', 'StableScore'
)) {
    if ($policy -notmatch [regex]::Escape($required)) {
        Fail "Mayhem augment decision policy lost 3.5 behavior: $required"
    }
}
foreach ($forbidden in @('HttpClient', 'HttpRequestMessage', 'FACM\.Infrastructure', 'Microsoft\.UI', 'System\.Drawing')) {
    if ($policy -match $forbidden) {
        Fail "Mayhem augment decision policy leaked transport/UI detail: $forbidden"
    }
}

foreach ($required in @(
    'MayhemAugmentEnrichmentService', 'MayhemPublicResourceKind.MayhemAugments',
    'MayhemPublicResourceKind.RankingBuild', 'TimeSpan.FromSeconds(5.5)', 'TimeSpan.FromSeconds(3)',
    'ParseOpggRows', 'FindNextArrayMarker', 'FindBalancedEnd', 'ScoreRows',
    'if (string.IsNullOrWhiteSpace(icon)) return null', '.Take(12)', '.Take(5)',
    'MayhemAugmentDecisionPolicy.BuildRoutes', 'AugmentSourceRoute', 'AugmentSourceStale',
    'Best Augments for', 'Augment Combos', 'raw.communitydragon.org', 'Rarity = "未知"'
)) {
    if ($service -notmatch [regex]::Escape($required)) {
        Fail "Mayhem rich augment enrichment is missing 3.5 behavior: $required"
    }
}
foreach ($forbidden in @(
    'new HttpClient', 'HttpRequestMessage', 'System\.Drawing', 'System\.Windows\.Forms',
    'Microsoft\.UI', 'Clipboard', 'ILeagueWriteGateway', 'LeagueWriteCommand', 'Process\.'
)) {
    if ($service -match $forbidden) {
        Fail "Mayhem augment enrichment crossed its typed read-only boundary: $forbidden"
    }
}
if ($service -match 'public\s+[^\r\n]+\([^)]*\b(url|uri)\b') {
    Fail 'Mayhem augment enrichment must not accept arbitrary URL/URI input.'
}
if ((Count-Matches $service '_transport\.GetAsync') -ne 2) {
    Fail 'Mayhem augment enrichment must use exactly one rich typed request plus one bounded legacy fallback request.'
}

foreach ($required in @(
    'MayhemAugments', 'RankingBuild',
    'https://op.gg/zh-cn/lol/modes/aram-mayhem', 'https://arammayhem.com',
    'MayhemPublicResourceRequest', 'Resolve(MayhemPublicResourceRequest request)'
)) {
    if ($transport -notmatch [regex]::Escape($required)) {
        Fail "Typed public-data transport cannot support augment enrichment: $required"
    }
}

foreach ($required in @(
    'ValidateDecisionPolicy', 'ValidateRichParserRequiresIcon', 'ValidateLegacyProjection',
    'ValidateTypedRichEnrichmentAsync', 'ValidateTypedLegacyFallbackAsync',
    'StableScore', 'No Icon', 'handler.Calls == 1', 'handler.Calls == 2'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) {
        Fail "Mayhem augment deterministic smoke is missing: $required"
    }
}
if ((Count-Matches $querySmoke 'MayhemAugmentSmoke\.RunAsync') -ne 1) {
    Fail 'Mayhem base smoke must execute augment parity smoke exactly once.'
}

Write-Host 'Mayhem augment policy: 3 distinct routes + fixed 72/28 stable score'
Write-Host 'Mayhem rich parser: icon-required OP.GG rows + percent/rarity/sample projection'
Write-Host 'Mayhem legacy fallback: typed ARAMMayhem build + max five Kiwi-backed rows'
Write-Host 'Mayhem augment source: typed transport only; no arbitrary URLs or League writes'
Write-Host 'Mayhem augment deterministic smoke: rich/fallback/decision behavior covered offline'
Write-Host 'FACM 4.0 Mayhem augment parity contract: SUCCESS'
