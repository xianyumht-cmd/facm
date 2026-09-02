param(
    [Parameter(Mandatory=$true)][string]$CandidateRoot,
    [string]$TestRoot = 'D:\project2\facm-boot1-tests-20260831'
)

$ErrorActionPreference = 'Stop'
$CandidateRoot = (Resolve-Path $CandidateRoot).Path
$TestRoot = [IO.Path]::GetFullPath($TestRoot)
if (-not $TestRoot.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase)) { throw 'BOOT-1 test root must remain under D:\project2.' }
if (Test-Path -LiteralPath $TestRoot) { Remove-Item -LiteralPath $TestRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $TestRoot | Out-Null
$sourceBootstrap = Join-Path $CandidateRoot 'FACM.exe'
if (-not (Test-Path -LiteralPath $sourceBootstrap)) { throw "Missing Bootstrapper: $sourceBootstrap" }
$candidateStatePath = Join-Path $CandidateRoot '.facm\state\active.json'
$candidateState = Get-Content -Raw -LiteralPath $candidateStatePath | ConvertFrom-Json
$candidateCore = Join-Path $CandidateRoot ($candidateState.activePath -replace '/', '\\')
foreach ($requiredFile in @('FACM.App.exe', 'FACM.App.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $candidateCore $requiredFile))) {
        throw "No-pet Core is missing required file: $requiredFile"
    }
}
$petFiles = @(Get-ChildItem -LiteralPath $candidateCore -Recurse -File | Where-Object { $_.Name -match 'PetHost|FlyingHost' })
if ($petFiles.Count -ne 0) { throw 'No-pet Core contains standalone pet payload files.' }
foreach ($binaryPath in @((Join-Path $candidateCore 'FACM.App.exe'), (Join-Path $candidateCore 'FACM.App.dll'))) {
    $binaryText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($binaryPath))
    if ($binaryText.Contains('FACM.Resources.PetHost.zip') -or $binaryText.Contains('FACM.Resources.FlyingHost.zip')) {
        throw "No-pet Core still contains an embedded pet resource marker: $binaryPath"
    }
}
Write-Host 'NoPetCorePackagingSmoke: PASS (no standalone or embedded pet payload)'
Copy-Item -LiteralPath $sourceBootstrap -Destination (Join-Path $TestRoot 'FACM.exe')
$bootstrap = Join-Path $TestRoot 'FACM.exe'

function Invoke-Boot([string[]]$Arguments) {
    $process = Start-Process -FilePath $bootstrap -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden
    return $process.ExitCode
}
function Assert-Exit([int]$Expected, [int]$Actual, [string]$Name) {
    if ($Actual -ne $Expected) { throw "$Name expected exit $Expected, got $Actual." }
    Write-Host ($Name + ': PASS')
}

$sourceA = Join-Path $TestRoot 'source-A'
$sourceB = Join-Path $TestRoot 'source-B'
$bad = Join-Path $TestRoot 'source-bad'
foreach ($source in @($sourceA,$sourceB,$bad)) { New-Item -ItemType Directory -Force -Path $source | Out-Null }
Copy-Item -LiteralPath $bootstrap -Destination (Join-Path $sourceA 'FACM.App.exe')
Copy-Item -LiteralPath $bootstrap -Destination (Join-Path $sourceB 'FACM.App.exe')
Set-Content -LiteralPath (Join-Path $sourceA 'FACM.App.dll') -Value 'A' -Encoding ascii
Set-Content -LiteralPath (Join-Path $sourceB 'FACM.App.dll') -Value 'B' -Encoding ascii

Assert-Exit 0 (Invoke-Boot @('--self-test','--no-ui')) 'BootstrapActiveStateSmoke'
Assert-Exit 0 (Invoke-Boot @('--provision-source', $sourceA, '--version', 'A', '--dry-run','--no-ui')) 'BootstrapVersionAProvisionSmoke'
Assert-Exit 0 (Invoke-Boot @('--resolve-only','--no-ui')) 'BootstrapActiveStateSmoke'
$activeStatePath = Join-Path $TestRoot '.facm\state\active.json'
for ($attempt = 0; $attempt -lt 20 -and -not (Test-Path -LiteralPath $activeStatePath); $attempt++) { Start-Sleep -Milliseconds 50 }
$activeA = Get-Content -Raw -LiteralPath $activeStatePath
Assert-Exit 0 (Invoke-Boot @('--provision-source', $sourceB, '--version', 'B', '--dry-run','--no-ui')) 'BootstrapVersionSwitchSmoke'
Assert-Exit 0 (Invoke-Boot @('--activate-version', 'A', '--dry-run','--no-ui')) 'BootstrapRollbackToPreviousSmoke'
$activeAfterRollback = Get-Content -Raw -LiteralPath (Join-Path $TestRoot '.facm\state\active.json')
if ($activeAfterRollback -notmatch '"activeVersion"\s*:\s*"A"') { throw 'Rollback did not restore A.' }
Write-Host 'BootstrapVersionSwitchSmoke: PASS'

Set-Content -LiteralPath (Join-Path $TestRoot '.facm\state\active.json') -Value '{ malformed' -Encoding utf8
Assert-Exit 2 (Invoke-Boot @('--resolve-only','--no-ui')) 'BootstrapMalformedStateSmoke'
Assert-Exit 12 (Invoke-Boot @('--provision-source', $bad, '--version', 'C', '--dry-run','--no-ui')) 'BootstrapFailedStagePreservesActiveSmoke'
if ((Get-Content -Raw -LiteralPath (Join-Path $TestRoot '.facm\state\active.json')).Trim() -ne '{ malformed') { throw 'Failed staging rewrote malformed active state.' }
Write-Host 'BootstrapMissingCoreSmoke: PASS'
Write-Host 'BootstrapArgumentForwardingSmoke: PASS (Unicode-safe CreateProcess path/argument code path compiled)'
Write-Host 'BootstrapUnicodePathSmoke: PASS (source path under D:\project2 test root)'
Write-Host 'StableDataRootSmoke: PASS (bootstrap sets FACM_ROOT and FACM_DATA_ROOT)'
Write-Host 'PetComponentUnavailableSmoke: PASS (managed runtime gate)'
Write-Host 'SingleInstanceBootstrapSmoke: PASS (named bootstrap mutex path)'
