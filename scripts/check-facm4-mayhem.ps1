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

$corePath = Join-Path $Root 'src/FACM.Core/Mayhem/MayhemContracts.cs'
$aliasesPath = Join-Path $Root 'src/FACM.Core/Mayhem/MayhemChampionAliases.cs'
$queryPath = Join-Path $Root 'src/FACM.Infrastructure/Mayhem/MayhemQueryService.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MayhemQuerySmoke.cs'
$smokeProgramPath = Join-Path $Root 'src/FACM.FoundationSmoke/Program.cs'

foreach ($path in @($corePath, $aliasesPath, $queryPath, $smokePath, $smokeProgramPath)) {
    if (-not (Test-Path $path)) { Fail "Mayhem contract file missing: $path" }
}

$core = Get-Content $corePath -Raw
$aliases = Get-Content $aliasesPath -Raw
$query = Get-Content $queryPath -Raw
$smoke = Get-Content $smokePath -Raw
$smokeProgram = Get-Content $smokeProgramPath -Raw

foreach ($required in @(
    'MayhemChampionResult', 'MayhemTopChampion', 'MayhemAugmentRow', 'MayhemDecisionRoute',
    'MayhemBuildItem', 'MayhemBuildPath', 'MayhemSkillPriority', 'IMayhemQueryService',
    'QueryAsync', 'SourceNote', 'BuildSourceRoute', 'AugmentSourceRoute', 'ErrorMessage'
)) {
    if ($core -notmatch [regex]::Escape($required)) {
        Fail "Mayhem Core data contract is missing: $required"
    }
}
foreach ($forbidden in @(
    'System\.Drawing', 'System\.Windows\.Forms', 'Microsoft\.UI', 'HttpClient',
    'HttpRequestMessage', 'FACM\.Infrastructure', 'FACM\.Platform'
)) {
    if ($core -match $forbidden) {
        Fail "Mayhem Core leaked rendering/network/platform detail: $forbidden"
    }
}

foreach ($required in @(
    '["寒冰"] = "ashe"', '["滑板鞋"] = "kalista"', '["vn"] = "vayne"',
    'TryResolve', 'Normalize', 'Slugify', 'IsLikelySlug'
)) {
    if ($aliases -notmatch [regex]::Escape($required)) {
        Fail "Mayhem 3.5 champion alias compatibility is missing: $required"
    }
}
foreach ($forbidden in @('HttpClient', 'System\.Drawing', 'Microsoft\.UI', 'FACM\.Infrastructure')) {
    if ($aliases -match $forbidden) {
        Fail "Mayhem alias resolver leaked implementation detail: $forbidden"
    }
}

foreach ($required in @(
    'MayhemSourceKind', 'HexdataHeroes', 'RankingHome', 'RankingBuild', 'OpggIndex', 'OpggBuild',
    'https://hexdata.com.cn/heroes', 'https://op.gg/zh-cn/lol/modes/aram-mayhem', 'https://arammayhem.com',
    'TimeSpan.FromSeconds(2.8)', 'TimeSpan.FromSeconds(3.4)', 'TimeSpan.FromSeconds(3.8)',
    'TimeSpan.FromSeconds(1.5)', 'TimeSpan.FromSeconds(2.2)',
    'CacheDuration = TimeSpan.FromMinutes(10)', 'OverallBudget = TimeSpan.FromSeconds(5.5)',
    'overall.CancelAfter(OverallBudget)', 'MayhemChampionAliases.TryResolve',
    'ResolveSlugFromHexdata', 'ResolveSlugFromOpgg', 'ParseHexdataRows', 'ApplyHexdata',
    'ParseRankingChampion', 'ParseTopTen', 'ParseOpggChampion', 'InferTier',
    'Hexdata 国内优先', 'ARAMMayhem 备用', 'OP.GG 已补充'
)) {
    if ($query -notmatch [regex]::Escape($required)) {
        Fail "Mayhem base query pipeline is missing 3.5 behavior: $required"
    }
}

if ((Count-Matches $query 'new\s+HttpClient\s*\(') -ne 1) {
    Fail 'Mayhem base query must own exactly one fixed-destination public HttpClient.'
}
if ((Count-Matches $query 'TryGetStringAsync\s*\(new\s+MayhemSourceRequest') -lt 4) {
    Fail 'Mayhem query must access public data through typed fixed-source requests.'
}
foreach ($forbidden in @(
    'System\.Drawing', 'System\.Windows\.Forms', 'Microsoft\.UI', 'Clipboard', 'Bitmap',
    'ILeagueWriteGateway', 'LeagueWriteCommand', 'Process\.', 'RegisterHotKey'
)) {
    if ($query -match $forbidden) {
        Fail "Mayhem base query crossed query/render/write boundary: $forbidden"
    }
}
if ($query -match 'QueryAsync\s*\([^)]*(url|uri)') {
    Fail 'Mayhem public query contract must not accept arbitrary URL/URI input.'
}

foreach ($required in @(
    'ValidateAliasCompatibility', 'ValidateNormalizationAndSlugGrammar', 'ValidateQueryBudgets',
    'ValidateResultContract', 'ValidateEmptyQueryIsLocalAsync',
    'CacheDuration == TimeSpan.FromMinutes(10)', 'OverallBudget == TimeSpan.FromSeconds(5.5)'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) {
        Fail "Mayhem deterministic smoke is missing: $required"
    }
}
if ((Count-Matches $smokeProgram 'MayhemQuerySmoke\.RunAsync') -ne 1) {
    Fail 'Foundation smoke must register Mayhem base query exactly once.'
}

Write-Host 'Mayhem Core: platform-neutral 3.5 result model + query intent'
Write-Host 'Mayhem aliases: 3.5 Chinese/common-name compatibility retained'
Write-Host 'Mayhem public source: typed fixed HTTPS destinations with per-source budgets'
Write-Host 'Mayhem query: 5.5s overall budget + 10m cache + Hexdata priority + ranking/OP.GG fallback'
Write-Host 'Mayhem deterministic smoke: aliases / grammar / budget / local empty-query behavior'
Write-Host 'FACM 4.0 Mayhem base query contract: SUCCESS'
