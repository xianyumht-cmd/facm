param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

$core = Join-Path $Root 'src/FACM.Core'
$coreProject = Join-Path $core 'FACM.Core.csproj'
if (-not (Test-Path $coreProject)) { Fail 'FACM.Core project is missing.' }

$coreProjectText = Get-Content $coreProject -Raw
foreach ($token in @('ProjectReference', 'PackageReference', 'UseWinUI', 'UseWPF', 'UseWindowsForms')) {
    if ($coreProjectText -match $token) { Fail "FACM.Core must not contain $token." }
}

$coreForbidden = @(
    'System\.Windows\.Forms',
    'Microsoft\.UI\.Xaml',
    'System\.Drawing',
    'System\.Windows\.Controls',
    'Windows\.UI\.Xaml'
)
foreach ($file in Get-ChildItem $core -Recurse -Filter '*.cs') {
    $text = Get-Content $file.FullName -Raw
    foreach ($pattern in $coreForbidden) {
        if ($text -match $pattern) { Fail "Core UI-framework dependency detected in $($file.FullName): $pattern" }
    }
}

$viewModels = Join-Path $Root 'src/FACM.App/ViewModels'
if (Test-Path $viewModels) {
    $viewModelForbidden = @(
        'FACM\.Infrastructure',
        'FACM\.Platform\.Windows',
        'System\.Net\.Http',
        'System\.IO',
        'System\.Diagnostics',
        'Microsoft\.UI\.Xaml',
        'LeagueTransportSession',
        'LeagueClientSession',
        'HttpClient',
        'File\.',
        'Directory\.',
        'Process\.',
        'Registry\.',
        'https?://'
    )
    foreach ($file in Get-ChildItem $viewModels -Recurse -Filter '*.cs') {
        $text = Get-Content $file.FullName -Raw
        foreach ($pattern in $viewModelForbidden) {
            if ($text -match $pattern) { Fail "ViewModel crossed the Core intent/state boundary in $($file.FullName): $pattern" }
        }
    }
}

function Get-ProjectRefNames([string]$Path) {
    [xml]$xml = Get-Content $Path -Raw
    $refs = @()
    foreach ($group in @($xml.Project.ItemGroup)) {
        foreach ($reference in @($group.ProjectReference)) {
            if ($null -eq $reference) { continue }
            $include = [string]$reference.Include
            if ([string]::IsNullOrWhiteSpace($include)) { continue }
            $refs += [System.IO.Path]::GetFileNameWithoutExtension($include)
        }
    }
    return @($refs)
}

$infraRefs = @(Get-ProjectRefNames (Join-Path $Root 'src/FACM.Infrastructure/FACM.Infrastructure.csproj'))
$platformRefs = @(Get-ProjectRefNames (Join-Path $Root 'src/FACM.Platform.Windows/FACM.Platform.Windows.csproj'))
$appRefs = @(Get-ProjectRefNames (Join-Path $Root 'src/FACM.App/FACM.App.csproj'))

if (($infraRefs.Count -ne 1) -or ($infraRefs[0] -ne 'FACM.Core')) { Fail "Infrastructure references must equal FACM.Core; actual=$($infraRefs -join ',')" }
if (($platformRefs.Count -ne 1) -or ($platformRefs[0] -ne 'FACM.Core')) { Fail "Platform.Windows references must equal FACM.Core; actual=$($platformRefs -join ',')" }
foreach ($required in @('FACM.Core', 'FACM.Infrastructure', 'FACM.Platform.Windows')) {
    if ($appRefs -notcontains $required) { Fail "FACM.App missing reference to $required; actual=$($appRefs -join ',')" }
}
if ($appRefs.Count -ne 3) { Fail "FACM.App has unexpected project references: $($appRefs -join ',')" }

$solution = Get-Content (Join-Path $Root 'FACM4.sln') -Raw
foreach ($project in @('FACM.Core', 'FACM.Infrastructure', 'FACM.Platform.Windows', 'FACM.App', 'FACM.FoundationSmoke', 'FACM.WindowsSmoke')) {
    if ($solution -notmatch [regex]::Escape($project)) { Fail "FACM4.sln missing $project." }
}

$appComposition = Get-Content (Join-Path $Root 'src/FACM.App/App.xaml.cs') -Raw
if ($appComposition -match 'UnavailableUpdateManifestSource') { Fail 'Gate 3 App must use a real update manifest adapter.' }
$leagueOwnerCount = ([regex]::Matches($appComposition, 'new\s+WindowsLeagueTransportSessionSource\s*\(')).Count
if ($leagueOwnerCount -ne 1) { Fail "Gate 3 composition root must create exactly one League session owner; actual=$leagueOwnerCount" }

$diffBase = git -C $Root rev-parse --verify 'HEAD^' 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($diffBase)) { Fail 'Unable to compare Gate branch with its PR base.' }
$changed = @(git -C $Root diff --name-only $diffBase HEAD 2>$null)
if ($LASTEXITCODE -ne 0) { Fail 'Unable to compare Gate branch with its PR base.' }
foreach ($protected in @('online/version.json', 'release/request.json')) {
    if ($changed -contains $protected) { Fail "Gate migration must not modify production release control: $protected" }
}

Write-Host "Infrastructure refs: $($infraRefs -join ', ')"
Write-Host "Platform refs: $($platformRefs -join ', ')"
Write-Host "App refs: $($appRefs -join ', ')"
Write-Host 'FACM 4.0 architecture contract: SUCCESS'
