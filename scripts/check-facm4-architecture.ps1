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

$forbiddenSource = @(
    'System\.Windows\.Forms',
    'Microsoft\.UI\.Xaml',
    'System\.Drawing',
    'System\.Windows\.Controls',
    'Windows\.UI\.Xaml'
)
foreach ($file in Get-ChildItem $core -Recurse -Filter '*.cs') {
    $text = Get-Content $file.FullName -Raw
    foreach ($pattern in $forbiddenSource) {
        if ($text -match $pattern) { Fail "Core UI-framework dependency detected in $($file.FullName): $pattern" }
    }
}

function Get-ProjectRefs([string]$Path) {
    [xml]$xml = Get-Content $Path -Raw
    return @($xml.Project.ItemGroup.ProjectReference | ForEach-Object { [string]$_.Include })
}

$infraRefs = Get-ProjectRefs (Join-Path $Root 'src/FACM.Infrastructure/FACM.Infrastructure.csproj')
$platformRefs = Get-ProjectRefs (Join-Path $Root 'src/FACM.Platform.Windows/FACM.Platform.Windows.csproj')
$appRefs = Get-ProjectRefs (Join-Path $Root 'src/FACM.App/FACM.App.csproj')

if (($infraRefs.Count -ne 1) -or ($infraRefs[0] -notmatch 'FACM\.Core')) { Fail 'Infrastructure may reference only FACM.Core in Gate 1.' }
if (($platformRefs.Count -ne 1) -or ($platformRefs[0] -notmatch 'FACM\.Core')) { Fail 'Platform.Windows may reference only FACM.Core in Gate 1.' }
foreach ($required in @('FACM.Core', 'FACM.Infrastructure', 'FACM.Platform.Windows')) {
    if (-not ($appRefs | Where-Object { $_ -match [regex]::Escape($required) })) { Fail "FACM.App missing reference to $required." }
}

$solution = Get-Content (Join-Path $Root 'FACM4.sln') -Raw
foreach ($project in @('FACM.Core', 'FACM.Infrastructure', 'FACM.Platform.Windows', 'FACM.App', 'FACM.FoundationSmoke')) {
    if ($solution -notmatch [regex]::Escape($project)) { Fail "FACM4.sln missing $project." }
}

try {
    $changed = @(git -C $Root diff --name-only origin/main...HEAD 2>$null)
    foreach ($protected in @('online/version.json', 'release/request.json')) {
        if ($changed -contains $protected) { Fail "Gate migration must not modify production release control: $protected" }
    }
} catch {
    Write-Warning 'git diff release-control check skipped because origin/main is unavailable.'
}

Write-Host 'FACM 4.0 architecture contract: SUCCESS'
