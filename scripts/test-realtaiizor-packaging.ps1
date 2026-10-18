[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\ui-compatibility'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$inputDirectory = Join-Path $OutputDirectory 'preview'
$toolDirectory = Join-Path $OutputDirectory 'merge-tool'
New-Item -ItemType Directory -Force -Path $toolDirectory | Out-Null
$package = Join-Path $toolDirectory 'ilrepack.2.0.48.nupkg'
Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/ilrepack/2.0.48/ilrepack.2.0.48.nupkg' -OutFile $package
$expected = '799017B829A6ED69FAC0D4FC0A874A4A6A9951F46D73A3CCE20BA016796B949F'
if ((Get-FileHash $package -Algorithm SHA256).Hash -ne $expected) { throw 'ILRepack package hash mismatch.' }
& dotnet nuget verify $package --all
if ($LASTEXITCODE -ne 0) { throw 'ILRepack signature verification failed.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($package, (Join-Path $toolDirectory 'extracted'))
$tool = Join-Path $toolDirectory 'extracted\tools\ILRepack.exe'
$mergedDirectory = Join-Path $OutputDirectory 'single-exe'
New-Item -ItemType Directory -Path $mergedDirectory | Out-Null
$exe = Join-Path $mergedDirectory 'GGman.UiCompatibility.exe'
$framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
& $tool '/target:winexe' '/targetplatform:v4' '/ndebug' '/skipconfig' "/lib:$framework" "/out:$exe" (Join-Path $inputDirectory 'GGman.UiCompatibility.exe') (Join-Path $inputDirectory 'ReaLTaiizor.dll')
if ($LASTEXITCODE -ne 0) { throw 'Assembly merge failed.' }
# Run with only the EXE in its directory: no DLL, config, tool or NuGet cache copy.
if (@(Get-ChildItem $mergedDirectory -File).Count -ne 1) { throw 'Unexpected runtime sidecar.' }
for ($trial = 1; $trial -le 5; $trial++) {
    $report = Join-Path $OutputDirectory "merged-smoke-$trial.json"
    $process = Start-Process $exe -WorkingDirectory $mergedDirectory -ArgumentList @('--smoke-merged', "`"$report`"") -PassThru
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Merged smoke timeout.' }
    if (Test-Path $report) { Write-Host (Get-Content $report -Raw) }
    if ($process.ExitCode -ne 0) { throw 'Merged smoke failed.' }
    $result = Get-Content $report -Raw | ConvertFrom-Json
    if (-not $result.mergedExecutable) { throw 'Merged identity was not verified.' }
}
Copy-Item (Join-Path $inputDirectory 'ReaLTaiizor.LICENSE.txt') $mergedDirectory
Copy-Item (Join-Path $inputDirectory 'Open-Preview.cmd') $mergedDirectory
$zip = Join-Path $OutputDirectory 'GGman-UI-SingleExe-Experiment.zip'
Compress-Archive -Path "$mergedDirectory\*" -DestinationPath $zip -Force
$measurement = [pscustomobject]@{
    mode = 'Build-time ILRepack 2.0.48 merge; unsigned isolated fixture, not product migration'
    exeBytes = (Get-Item $exe).Length
    exeSha256 = (Get-FileHash $exe -Algorithm SHA256).Hash
    zipBytes = (Get-Item $zip).Length
    runtimeSidecars = @()
    requiredFramework = '.NET Framework 4.8'
    productionBudgetChanged = $false
    win10OfflineAcceptance = 'NOT RUN; Windows CI runner only'
    dpiWithoutConfig = 'Requires native multi-monitor validation; config omitted deliberately'
}
$measurement | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'merged-distribution.json') -Encoding utf8
Write-Host ($measurement | ConvertTo-Json -Compress)
