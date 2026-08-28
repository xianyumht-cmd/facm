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

$transportPath = Join-Path $Root 'src/FACM.Infrastructure/Mayhem/MayhemCachedPublicDataTransport.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MayhemPublicDataTransportSmoke.cs'
$officialSmokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MayhemOfficialPatchSmoke.cs'

foreach ($path in @($transportPath, $smokePath, $officialSmokePath)) {
    if (-not (Test-Path $path)) { Fail "Mayhem public-data contract file missing: $path" }
}

$transport = Get-Content $transportPath -Raw
$smoke = Get-Content $smokePath -Raw
$officialSmoke = Get-Content $officialSmokePath -Raw

foreach ($required in @(
    'MayhemPublicResourceKind', 'MayhemPublicResourceRequest', 'MayhemPublicDataResponse',
    'MayhemAugments', 'MayhemBuild', 'RankingBuild', 'AramLocalizedBuild', 'AramGlobalBuild',
    'CommunityDragonItems', 'CommunityDragonAugments', 'CommunityDragonSummonerSpells',
    'CommunityDragonChampionSummary', 'CommunityDragonChampionDetail',
    'MaximumBodyBytes = 12L * 1024L * 1024L',
    'FreshCacheAge = TimeSpan.FromMinutes(15)', 'StaleCacheAge = TimeSpan.FromHours(24)',
    'https://op.gg/zh-cn/lol/modes/aram-mayhem', 'https://arammayhem.com',
    'https://op.gg/zh-cn/lol/modes/aram', 'https://op.gg/lol/modes/aram',
    'https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/zh_cn/v1',
    'ConcurrentDictionary<string, SemaphoreSlim>', 'GetOrAdd(uri.AbsoluteUri',
    'fresh-cache', 'stale-cache', 'direct',
    'SHA256.HashData', 'File.GetLastWriteTimeUtc', 'File.Move(temporary, path, overwrite: true)',
    'response.Content.Headers.ContentLength', 'total > MaximumBodyBytes'
)) {
    if ($transport -notmatch [regex]::Escape($required)) {
        Fail "Mayhem cached public-data transport is missing: $required"
    }
}

if ((Count-Matches $transport 'new\s+HttpClient\s*\(') -ne 1) {
    Fail 'Mayhem cached public-data transport must own exactly one HttpClient.'
}
if ($transport -match 'public\s+[^\r\n]+\([^)]*\b(url|uri)\b') {
    Fail 'Mayhem cached public-data public surface must not accept arbitrary URL/URI input.'
}
foreach ($forbidden in @(
    'http://', '127\.0\.0\.1', 'localhost', 'Authorization', 'CookieContainer',
    'ILeagueWriteGateway', 'LeagueWriteCommand', 'System\.Drawing', 'System\.Windows\.Forms',
    'Microsoft\.UI', 'Process\.', 'Registry\.'
)) {
    if ($transport -match $forbidden) {
        Fail "Mayhem cached public-data transport crossed its read-only public boundary: $forbidden"
    }
}

foreach ($required in @(
    'ValidateTypedResolvers', 'ValidateBudgetsAndCaps', 'ValidateDirectAndFreshCacheAsync',
    'ValidateStaleFallbackAsync', 'ValidateSingleFlightAsync',
    'Route: "direct"', 'Route: "fresh-cache"', 'Route: "stale-cache"',
    'handler.Calls == 1', 'File.SetLastWriteTimeUtc'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) {
        Fail "Mayhem public-data deterministic smoke is missing: $required"
    }
}
if ($officialSmoke -notmatch [regex]::Escape('MayhemPublicDataTransportSmoke.RunAsync')) {
    Fail 'Mayhem official smoke must execute the cached public-data smoke exactly once.'
}

Write-Host 'Mayhem public-data resolver: typed fixed HTTPS resources only'
Write-Host 'Mayhem public-data cache: 15m fresh / 24h stale / SHA256 disk key'
Write-Host 'Mayhem public-data transport: 12MB cap + per-resource single-flight + no LCU credentials/writes'
Write-Host 'Mayhem public-data deterministic smoke: direct / fresh / stale / single-flight'
Write-Host 'FACM 4.0 Mayhem cached public-data transport contract: SUCCESS'
