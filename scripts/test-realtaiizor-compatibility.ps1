[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\ui-compatibility'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tools\ui-compatibility\GGman.UiCompatibility.csproj'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$packages = Join-Path $OutputDirectory 'package-input'
New-Item -ItemType Directory -Force -Path $packages | Out-Null
$package = Join-Path $packages 'realtaiizor.3.8.2.1.nupkg'
Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/realtaiizor/3.8.2.1/realtaiizor.3.8.2.1.nupkg' -OutFile $package
$expected = 'B38263144A60D4EEB078C4D10355FD138E4709110AC41B70DCDFB225A563F12A'
if ((Get-FileHash $package -Algorithm SHA256).Hash -ne $expected) { throw 'ReaLTaiizor package hash mismatch.' }
& dotnet nuget verify $package --all
if ($LASTEXITCODE -ne 0) { throw 'NuGet package signature verification failed.' }
& dotnet restore $project --locked-mode --source $packages
if ($LASTEXITCODE -ne 0) { throw 'Locked net48 restore failed.' }
& dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Compatibility build failed.' }
$bin = Join-Path $root 'tools\ui-compatibility\bin\Release\net48'
$distribution = Join-Path $OutputDirectory 'preview'
New-Item -ItemType Directory -Force -Path $distribution | Out-Null
Get-ChildItem $bin -File | Where-Object { $_.Extension -in '.exe', '.dll', '.config', '.txt' } | Copy-Item -Destination $distribution
$files = @(Get-ChildItem $distribution -File | ForEach-Object {
    [pscustomobject]@{ name=$_.Name; bytes=$_.Length; sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash }
})
$files | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'distribution.json') -Encoding utf8
Write-Host ($files | ConvertTo-Json -Compress)
$exe = Join-Path $distribution 'GGman.UiCompatibility.exe'
for ($trial = 1; $trial -le 5; $trial++) {
    $report = Join-Path $OutputDirectory "smoke-$trial.json"
    $process = Start-Process $exe -ArgumentList @('--smoke', "`"$report`"") -PassThru
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Compatibility smoke timeout.' }
    if (Test-Path $report) { Write-Host (Get-Content $report -Raw) }
    if ($process.ExitCode -ne 0) { throw "Compatibility smoke failed: $report" }
    if (-not (Test-Path $report)) { throw 'Smoke report missing.' }
}
@'
@echo off
cd /d "%~dp0"
start "" "GGman.UiCompatibility.exe"
'@ | Set-Content (Join-Path $distribution 'Open-Preview.cmd') -Encoding ascii
$library = Get-Item (Join-Path $distribution 'ReaLTaiizor.dll')
[pscustomobject]@{
    verdict = 'NO-GO for broad migration under current single-EXE distribution; native acceptance INCONCLUSIVE'
    libraryBytes = $library.Length
    existingExeGateBytes = 10MB
    requiresSidecarWithOrdinaryPackageReference = $true
    packageSha256 = $expected
    nativeWin10VisualAcceptance = 'NOT RUN'
    coldWarmProductionComparison = 'NOT RUN; smoke timing is construction plus checks on this runner only'
    cleanOfflineWin10Launch = 'NOT RUN'
} | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'decision.json') -Encoding utf8
Compress-Archive -Path "$distribution\*" -DestinationPath (Join-Path $OutputDirectory 'GGman-UI-Compatibility.zip') -Force
Write-Host "Compatibility evidence: $OutputDirectory"

$rendered = Join-Path $OutputDirectory 'rendered'
$process = Start-Process $exe -ArgumentList @('--capture', "`"$rendered`"") -PassThru
if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Repair rendering timeout.' }
if ($process.ExitCode -ne 0) { throw 'Repair rendering failed.' }
Get-ChildItem $rendered -Filter '*.png' | ForEach-Object {
    Write-Host ('REPAIR_RENDER:' + $_.Name + ':' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($_.FullName)))
}
