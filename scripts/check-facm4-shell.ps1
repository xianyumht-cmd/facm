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

$app = Join-Path $Root 'src/FACM.App'
$mainXamlPath = Join-Path $app 'MainWindow.xaml'
$mainCodePath = Join-Path $app 'MainWindow.xaml.cs'
$tokensPath = Join-Path $app 'Themes/FacmTokens.xaml'
$controlsPath = Join-Path $app 'Themes/FacmControls.xaml'
$appXamlPath = Join-Path $app 'App.xaml'
$textPath = Join-Path $Root 'src/FACM.Core/Text/UiTextContracts.cs'

foreach ($path in @($mainXamlPath, $mainCodePath, $tokensPath, $controlsPath, $appXamlPath, $textPath)) {
    if (-not (Test-Path $path)) { Fail "Shell contract file missing: $path" }
}

$mainXaml = Get-Content $mainXamlPath -Raw
$mainCode = Get-Content $mainCodePath -Raw
$tokens = Get-Content $tokensPath -Raw
$controls = Get-Content $controlsPath -Raw
$appXaml = Get-Content $appXamlPath -Raw
$text = Get-Content $textPath -Raw

if ((Count-Matches $mainXaml '<NavigationView(?:\s|>)') -ne 1) { Fail 'MainWindow must contain exactly one NavigationView.' }
if ((Count-Matches $mainXaml '<Frame(?:\s|>)') -ne 1) { Fail 'MainWindow must contain exactly one Frame.' }
if ((Count-Matches $mainXaml '<NavigationViewItem(?:\s|>)') -ne 4) { Fail 'MainWindow must expose exactly four product navigation items.' }

foreach ($tag in @('repair', 'league', 'personalization', 'settings')) {
    if ((Count-Matches $mainXaml ('Tag="' + [regex]::Escape($tag) + '"')) -ne 1) {
        Fail "Shell navigation tag must appear exactly once: $tag"
    }
}
if ($mainXaml -match 'Tag="home"') { Fail 'Gate 6 Shell must not restore the temporary home navigation item.' }

if ((Count-Matches $mainXaml 'x:Name="AppTitleBar"') -ne 1) { Fail 'MainWindow must have exactly one AppTitleBar owner.' }
if ((Count-Matches $mainCode 'SetTitleBar\s*\(\s*AppTitleBar\s*\)') -ne 1) { Fail 'MainWindow code must set exactly one AppTitleBar.' }
if ($mainCode -notmatch 'ExtendsContentIntoTitleBar\s*=\s*true') { Fail 'MainWindow must extend content into its owned title bar.' }

$cjkPattern = '[\u4e00-\u9fff]'
if ($mainXaml -match $cjkPattern) { Fail 'MainWindow.xaml must not contain hard-coded CJK user copy; use UI Text contract.' }
if ($mainCode -match $cjkPattern) { Fail 'MainWindow.xaml.cs must not contain hard-coded CJK user copy; use UI Text contract.' }
if ($mainCode -notmatch '_text\.Get\s*\(') { Fail 'MainWindow must obtain user-visible copy through IUiTextProvider.' }

foreach ($forbidden in @('System\.Windows\.Forms', 'WindowsFormsHost', 'FormBorderStyle', 'new\s+WindowsLeagueTransportSessionSource', 'new\s+HttpClient', '\bFile\.', '\bDirectory\.')) {
    if ($mainCode -match $forbidden) { Fail "Shell implementation crossed its UI boundary: $forbidden" }
}

foreach ($xamlFile in Get-ChildItem $app -Recurse -Filter '*.xaml') {
    $xaml = Get-Content $xamlFile.FullName -Raw
    if ($xaml -match '#[0-9A-Fa-f]{6,8}') { Fail "Hard-coded color found in FACM.App XAML: $($xamlFile.FullName)" }
}

foreach ($key in @(
    'FacmBackgroundBrush', 'FacmSurfaceBrush', 'FacmSurfaceSecondaryBrush',
    'FacmTextPrimaryBrush', 'FacmTextMutedBrush', 'FacmAccentBrush', 'FacmStrokeBrush',
    'FacmCardCornerRadius', 'FacmControlCornerRadius', 'FacmPagePadding', 'FacmCardPadding'
)) {
    if ($tokens -notmatch ('x:Key="' + [regex]::Escape($key) + '"')) { Fail "Semantic token missing: $key" }
}
if ($tokens -notmatch 'ApplicationPageBackgroundThemeBrush' -or $tokens -notmatch 'TextFillColorPrimaryBrush') {
    Fail 'FACM semantic tokens must alias platform theme resources.'
}

foreach ($style in @(
    'FacmPageTitleTextStyle', 'FacmSectionTitleTextStyle', 'FacmCardTitleTextStyle',
    'FacmBodyTextStyle', 'FacmMutedTextStyle', 'FacmCardBorderStyle',
    'FacmStatusChipStyle', 'FacmPrimaryButtonStyle', 'FacmNavigationItemStyle'
)) {
    if ($controls -notmatch ('x:Key="' + [regex]::Escape($style) + '"')) { Fail "Shared Shell style missing: $style" }
}

foreach ($resource in @('Themes/FacmTokens.xaml', 'Themes/FacmControls.xaml')) {
    if ($appXaml -notmatch [regex]::Escape($resource)) { Fail "App.xaml must merge Shell resource dictionary: $resource" }
}

if ((Count-Matches $appXaml '<XamlControlsResources(?:\s|/|>)') -ne 1) {
    Fail 'App.xaml must merge exactly one WinUI XamlControlsResources dictionary.'
}
if ($appXaml -notmatch '<XamlControlsResources\s+xmlns="using:Microsoft\.UI\.Xaml\.Controls"\s*/>') {
    Fail 'App.xaml must load XamlControlsResources from Microsoft.UI.Xaml.Controls.'
}
$platformResourcesIndex = $appXaml.IndexOf('<XamlControlsResources', [System.StringComparison]::OrdinalIgnoreCase)
$facmTokensIndex = $appXaml.IndexOf('Themes/FacmTokens.xaml', [System.StringComparison]::OrdinalIgnoreCase)
$facmControlsIndex = $appXaml.IndexOf('Themes/FacmControls.xaml', [System.StringComparison]::OrdinalIgnoreCase)
if ($platformResourcesIndex -lt 0 -or
    $platformResourcesIndex -gt $facmTokensIndex -or
    $platformResourcesIndex -gt $facmControlsIndex) {
    Fail 'WinUI XamlControlsResources must be merged before FACM custom resource dictionaries.'
}

$shellConstants = @([regex]::Matches($text, 'public const string\s+(Shell\w+)\s*=') | ForEach-Object { $_.Groups[1].Value })
if ($shellConstants.Count -lt 16) { Fail "Unexpectedly small Shell UI text registry: $($shellConstants.Count)" }
foreach ($constant in $shellConstants) {
    if ($text -notmatch ('\[UiTextKeys\.' + [regex]::Escape($constant) + '\]\s*=')) {
        Fail "Shell UI text key has no default: $constant"
    }
}

if ($mainXaml -match '\b(?:Text|Content)="[^{}][^"]+"') {
    Fail 'MainWindow.xaml contains a literal Text/Content value; Shell copy must be provider-driven.'
}

Write-Host "Shell navigation items: 4"
Write-Host "Shell UI text defaults: $($shellConstants.Count)"
Write-Host 'FACM 4.0 Shell contract: SUCCESS'
