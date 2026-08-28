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

$servicePath = Join-Path $Root 'src/FACM.Infrastructure/Mayhem/MayhemBuildDetailsService.cs'
$transportPath = Join-Path $Root 'src/FACM.Infrastructure/Mayhem/MayhemCachedPublicDataTransport.cs'
$smokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MayhemBuildDetailsSmoke.cs'
$querySmokePath = Join-Path $Root 'src/FACM.FoundationSmoke/MayhemQuerySmoke.cs'

foreach ($path in @($servicePath, $transportPath, $smokePath, $querySmokePath)) {
    if (-not (Test-Path $path)) { Fail "Mayhem detailed-build parity file missing: $path" }
}

$service = Get-Content $servicePath -Raw
$transport = Get-Content $transportPath -Raw
$smoke = Get-Content $smokePath -Raw
$querySmoke = Get-Content $querySmokePath -Raw

foreach ($required in @(
    'MayhemBuildDetailsService', 'MaximumCoreBuilds = 2',
    'MayhemPublicResourceKind.MayhemBuild', 'TimeSpan.FromSeconds(1.8)',
    'if (HasDetailedBuild(result))', 'EnsureFallbackSkillPriority(result)',
    'ExtractItemRows(normalized, "core_items", 3, 5)',
    'ExtractItemRows(normalized, "starter_items", 2, 3)',
    'ExtractItemRows(normalized, "boots", 2, 1)',
    'SummonerSpells Table', 'SkillOrder Table',
    'summoner.Take(2)', 'skills.Take(3)', 'result.CoreBuilds[0].Items.Take(5)',
    'ProjectLegacyBuild', 'key == "R"', 'result.SkillPriority.Count >= 3',
    'BuildSourceRoute', 'BuildSourceStale', 'BuildSourceStatus'
)) {
    if ($service -notmatch [regex]::Escape($required)) {
        Fail "Mayhem detailed-build service lost 3.5 behavior: $required"
    }
}
foreach ($forbidden in @(
    'new HttpClient', 'HttpRequestMessage', 'result.SourceUrl', 'System\.Drawing',
    'System\.Windows\.Forms', 'Microsoft\.UI', 'Clipboard', 'LeagueWriteCommand', 'Process\.'
)) {
    if ($service -match $forbidden) {
        Fail "Mayhem detailed-build service crossed its typed read-only boundary: $forbidden"
    }
}
if ((Count-Matches $service '_transport\.GetAsync') -ne 1) {
    Fail 'Mayhem detailed-build enrichment must perform at most one typed public-data request.'
}

foreach ($required in @(
    'MayhemBuild', 'MayhemPublicResourceRequest',
    'https://op.gg/zh-cn/lol/modes/aram-mayhem',
    'Resolve(MayhemPublicResourceRequest request)'
)) {
    if ($transport -notmatch [regex]::Escape($required)) {
        Fail "Typed public-data transport cannot support detailed build: $required"
    }
}

foreach ($required in @(
    'ValidateHtmlProjection', 'ValidateLegacyProjection', 'ValidateTypedBuildRequestAsync',
    'ValidateExistingDetailsSkipNetworkAsync',
    'result.CoreBuilds.Count == 2', 'result.StarterItems.Count == 3',
    'result.BootItems.Count == 1', 'result.SummonerSpells.Count == 2',
    'SequenceEqual(new[] { "Q", "W", "E" })', 'handler.Calls == 1', 'handler.Calls == 0'
)) {
    if ($smoke -notmatch [regex]::Escape($required)) {
        Fail "Mayhem detailed-build deterministic smoke is missing: $required"
    }
}
if ((Count-Matches $querySmoke 'MayhemBuildDetailsSmoke\.RunAsync') -ne 1) {
    Fail 'Mayhem base smoke must execute detailed-build smoke exactly once.'
}

Write-Host 'Mayhem detailed build: max 2 core paths / 5 core items / 3 starter / 1 boot'
Write-Host 'Mayhem detailed build: max 2 summoners / 3 non-R skill priorities'
Write-Host 'Mayhem detailed build: one typed MayhemBuild request with 1.8s budget'
Write-Host 'Mayhem detailed build: existing-details short-circuit + legacy build/skill fallback'
Write-Host 'Mayhem detailed-build deterministic smoke: HTML / typed source / fallback covered offline'
Write-Host 'FACM 4.0 Mayhem detailed-build parity contract: SUCCESS'
