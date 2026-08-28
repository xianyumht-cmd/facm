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

$servicePath = Join-Path $Root 'src/FACM.Infrastructure/Mayhem/MayhemDecisionLocalizationService.cs'
$gatewayPath = Join-Path $Root 'src/FACM.Core/League/LeagueContracts.cs'
$transportPath = Join-Path $Root 'src/FACM.Infrastructure/Mayhem/MayhemCachedPublicDataTransport.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MayhemLocalizationSmoke.cs'
$querySmokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MayhemQuerySmoke.cs'

foreach ($path in @($servicePath, $gatewayPath, $transportPath, $smokePath, $querySmokePath)) {
    if (-not (Test-Path $path)) { Fail "Mayhem localization parity file missing: $path" }
}

$service = Get-Content $servicePath -Raw
$gateway = Get-Content $gatewayPath -Raw
$transport = Get-Content $transportPath -Raw
$smoke = Get-Content $smokePath -Raw
$querySmoke = Get-Content $querySmokePath -Raw

foreach ($required in @(
    'MayhemDecisionLocalizationService', 'ILeagueReadGateway',
    'CacheLifetime = TimeSpan.FromMinutes(20)', 'OverallBudget = TimeSpan.FromMilliseconds(1650)',
    '/lol-game-data/assets/v1/items.json', '/lol-game-data/assets/v1/cherry-augments.json',
    '/lol-game-data/assets/v1/summoner-spells.json', '/lol-game-data/assets/v1/champion-summary.json',
    '/lol-game-data/assets/v1/champions/',
    'CommunityDragonItems', 'CommunityDragonAugments', 'CommunityDragonSummonerSpells',
    'CommunityDragonChampionSummary', 'CommunityDragonChampionDetail',
    'nameTRA', 'descTRA', 'augmentSmallIconPath', 'squarePortraitPath', 'abilityIconPath',
    'route.AugmentName = localizedName', 'ReprojectLegacyLists', 'AssetReference',
    'TryGetCache', 'PutCache'
)) {
    if ($service -notmatch [regex]::Escape($required)) {
        Fail "Mayhem localization service lost 3.5 behavior: $required"
    }
}
foreach ($forbidden in @(
    'new LeagueHttpGateway', 'WindowsLeagueTransportSessionSource', 'new HttpClient', 'HttpRequestMessage',
    'ILeagueWriteGateway', 'LeagueWriteCommand', 'System\.Drawing', 'System\.Windows\.Forms',
    'Microsoft\.UI', 'Clipboard', 'Process\.'
)) {
    if ($service -match $forbidden) {
        Fail "Mayhem localization created a second League/network/UI owner: $forbidden"
    }
}
if ((Count-Matches $service '_league\.TryGetBytesAsync') -ne 1) {
    Fail 'Mayhem localization must reuse exactly one ILeagueReadGateway call site.'
}
if ((Count-Matches $service '_publicData\.GetAsync') -ne 1) {
    Fail 'Mayhem localization must use exactly one typed CommunityDragon fallback call site.'
}
if ($service -match 'public\s+[^\r\n]+\([^)]*\b(url|uri)\b') {
    Fail 'Mayhem localization must not accept arbitrary URL/URI input.'
}

foreach ($required in @('public interface ILeagueReadGateway', 'TryGetBytesAsync')) {
    if ($gateway -notmatch [regex]::Escape($required)) {
        Fail "Shared League read contract missing: $required"
    }
}
foreach ($required in @(
    'CommunityDragonItems', 'CommunityDragonAugments', 'CommunityDragonSummonerSpells',
    'CommunityDragonChampionSummary', 'CommunityDragonChampionDetail',
    'https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/zh_cn/v1'
)) {
    if ($transport -notmatch [regex]::Escape($required)) {
        Fail "Typed CommunityDragon resource missing: $required"
    }
}

foreach ($required in @(
    'ValidateFixtureProjection', 'ValidateLcuFirstAndCacheAsync', 'ValidateCommunityDragonFallbackAsync',
    'league.Calls == 5', 'publicHandler.Calls == 0', 'publicHandler.Calls == 5',
    '无尽之刃', '冰霜幽灵', '闪现', '射手的专注',
    'StartsWith("/lol-game-data/assets/v1/"', 'MayhemDecisionLocalizationService'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) {
        Fail "Mayhem localization deterministic smoke is missing: $required"
    }
}
if ((Count-Matches $querySmoke 'MayhemLocalizationSmoke\.RunAsync') -ne 1) {
    Fail 'Mayhem base smoke must execute localization parity smoke exactly once.'
}

Write-Host 'Mayhem localization: shared ILeagueReadGateway only; no second League session owner'
Write-Host 'Mayhem localization: fixed LCU game-data paths -> typed CommunityDragon fallback'
Write-Host 'Mayhem localization: 1.65s total budget + 20m catalog cache'
Write-Host 'Mayhem localization: items / augments+routes / summoners / champion / skill assets'
Write-Host 'Mayhem localization deterministic smoke: fixture / LCU-first / cache / fallback covered offline'
Write-Host 'FACM 4.0 Mayhem localization parity contract: SUCCESS'
