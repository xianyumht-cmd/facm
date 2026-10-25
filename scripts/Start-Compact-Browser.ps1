$ErrorActionPreference = 'Stop'

$previewFolder = Split-Path -Parent $MyInvocation.MyCommand.Path
$previewHtml = Join-Path $previewFolder 'GGman-Compact-Preview.html'
if (-not (Test-Path -LiteralPath $previewHtml -PathType Leaf)) {
    throw "Compact preview HTML is missing: $previewHtml"
}

$edgeExecutable = $null
foreach ($installationRoot in @(
    $env:ProgramFiles,
    [Environment]::GetEnvironmentVariable('ProgramFiles(x86)'),
    $env:LOCALAPPDATA
)) {
    if ([string]::IsNullOrWhiteSpace($installationRoot)) { continue }
    $candidate = Join-Path $installationRoot 'Microsoft\Edge\Application\msedge.exe'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $edgeExecutable = $candidate
        break
    }
}

if ($null -eq $edgeExecutable) {
    throw 'Microsoft Edge was not found. Open GGman-Compact-Preview.html in an installed browser.'
}

$previewUrl = ([System.Uri]::new($previewHtml)).AbsoluteUri
Start-Process -FilePath $edgeExecutable -ArgumentList @(
    "--app=$previewUrl",
    '--window-size=410,335',
    '--no-first-run'
)
