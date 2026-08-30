param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Read-Required([string]$RelativePath) {
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path $path)) { Fail "Desktop pet split contract file missing: $RelativePath" }
    return Get-Content $path -Raw
}

$vpetRuntime = Read-Required 'src/FACM.Platform.Windows/Personalization/WindowsVPetRuntime.cs'
$flyingRuntime = Read-Required 'src/FACM.Platform.Windows/Personalization/WindowsFlyingPetRuntime.cs'
$router = Read-Required 'src/FACM.Platform.Windows/Personalization/WindowsDesktopPetRuntimeRouter.cs'
$petStore = Read-Required 'src/FACM.Platform.Windows/Personalization/WindowsPetHostBundleStore.cs'
$flyingStore = Read-Required 'src/FACM.Platform.Windows/Personalization/WindowsFlyingHostBundleStore.cs'
$petHostProgram = Read-Required 'src/FACM.PetHost/Program.cs'
$petHostProject = Read-Required 'src/FACM.PetHost/FACM.PetHost.csproj'
$flyingHostProgram = Read-Required 'src/FACM.FlyingHost/Program.cs'
$flyingHostProject = Read-Required 'src/FACM.FlyingHost/FACM.FlyingHost.csproj'
$appComposition = Read-Required 'src/FACM.App/App.Personalization.cs'
$appProject = Read-Required 'src/FACM.App/FACM.App.csproj'
$petSmoke = Read-Required 'src/FACM.WindowsSmoke/PetHostBundleSmoke.cs'
$flyingSmoke = Read-Required 'src/FACM.WindowsSmoke/FlyingHostBundleSmoke.cs'
$solution = Read-Required 'FACM4.sln'

foreach ($required in @(
    'pet.Runtime != FacmPetRuntimeKind.VPetCore',
    'WindowsPetHostBundleStore',
    'FACM.PetHost.',
    '--pet-id',
    'runtime-unsupported:'
)) {
    if ($vpetRuntime -notmatch [regex]::Escape($required)) { Fail "VPet runtime split guard missing: $required" }
}
foreach ($forbidden in @(
    'FacmPetRuntimeKind.FlyingSprite',
    '--runtime',
    'WindowsFlyingHostBundleStore',
    'FACM.FlyingHost.'
)) {
    if ($vpetRuntime -match [regex]::Escape($forbidden)) { Fail "VPet runtime crossed into FlyingSprite chain: $forbidden" }
}

foreach ($required in @(
    'pet.Runtime != FacmPetRuntimeKind.FlyingSprite',
    'WindowsFlyingHostBundleStore',
    'FACM.FlyingHost.',
    '--pet-id',
    'runtime-unsupported:'
)) {
    if ($flyingRuntime -notmatch [regex]::Escape($required)) { Fail "Flying runtime split guard missing: $required" }
}
foreach ($forbidden in @(
    'FacmPetRuntimeKind.VPetCore',
    'WindowsPetHostBundleStore',
    'PetHostDataDirectory',
    'VPet-Simulator',
    'VPet_Simulator',
    '--runtime'
)) {
    if ($flyingRuntime -match [regex]::Escape($forbidden)) { Fail "Flying runtime crossed into VPet chain: $forbidden" }
}

foreach ($required in @(
    'WindowsDesktopPetRuntimeRouter',
    'FacmPetRuntimeKind.FlyingSprite',
    'FacmPetRuntimeKind.VPetCore',
    'SetActiveKind(null)',
    '_stateSync'
)) {
    if ($router -notmatch [regex]::Escape($required)) { Fail "Desktop pet router split behavior missing: $required" }
}

foreach ($required in @('FACM.Resources.PetHost.zip', 'FACM.Resources.PetHost.sha256', 'pethost-host')) {
    if (($petStore + "`n" + $appProject) -notmatch [regex]::Escape($required)) { Fail "VPet PetHost resource boundary missing: $required" }
}
foreach ($required in @('FACM.Resources.FlyingHost.zip', 'FACM.Resources.FlyingHost.sha256', 'flying-host', 'FACM.FlyingHost.exe')) {
    if (($flyingStore + "`n" + $appProject) -notmatch [regex]::Escape($required)) { Fail "FlyingHost resource boundary missing: $required" }
}
if ($flyingStore -match 'VPet-Simulator|VPet_Simulator') { Fail 'FlyingHost bundle store must not know any VPet payload.' }

foreach ($required in @('VPet-Simulator.Core', 'PetHostWindow', 'VPetAssetCacheValidator')) {
    if (($petHostProject + "`n" + $petHostProgram) -notmatch [regex]::Escape($required)) { Fail "VPet PetHost ownership missing: $required" }
}
if ($petHostProgram -match 'FlyingPetHostWindow|FlyingPetProfiles|FlyingSprite') {
    Fail 'FACM.PetHost must be VPet-only and must not own FlyingSprite behavior.'
}

foreach ($required in @(
    '<RootNamespace>FACM.FlyingHost</RootNamespace>',
    '<AssemblyName>FACM.FlyingHost</AssemblyName>',
    'FlyingPetHostWindow',
    'FlyingHostSelfTest'
)) {
    if (($flyingHostProject + "`n" + $flyingHostProgram) -notmatch [regex]::Escape($required)) { Fail "FlyingHost ownership missing: $required" }
}
if (($flyingHostProject + "`n" + $flyingHostProgram) -match 'VPet-Simulator|VPet_Simulator|VPetAssetCacheValidator') {
    Fail 'FACM.FlyingHost must stay independent from VPet.'
}

$flyingHostSourceRoot = Join-Path $Root 'src/FACM.FlyingHost'
foreach ($file in Get-ChildItem $flyingHostSourceRoot -Filter '*.cs' -File) {
    if ($file.Name -in @('GlobalUsings.cs', 'WpfAliases.cs')) { continue }
    $text = Get-Content $file.FullName -Raw
    if ($text -notmatch [regex]::Escape('namespace FACM.FlyingHost;')) {
        Fail "FlyingHost source must declare FACM.FlyingHost namespace: $($file.Name)"
    }
    if ($text -match [regex]::Escape('namespace FACM.PetHost;')) {
        Fail "FlyingHost source leaked legacy FACM.PetHost namespace: $($file.Name)"
    }
}

foreach ($required in @(
    'WindowsPetHostBundleStore',
    'WindowsFlyingHostBundleStore',
    'WindowsVPetRuntime',
    'WindowsFlyingPetRuntime',
    'WindowsDesktopPetRuntimeRouter',
    'ReadPetHostBundleSha256',
    'ReadFlyingHostBundleSha256'
)) {
    if ($appComposition -notmatch [regex]::Escape($required)) { Fail "App split composition missing: $required" }
}

foreach ($required in @(
    'RejectsFlyingSpriteWithoutOpeningPetHostBundleAsync',
    'runtime-unsupported:FlyingSprite',
    'FlyingSprite route must not open the VPet PetHost bundle'
)) {
    if ($petSmoke -notmatch [regex]::Escape($required)) { Fail "VPet anti-cross-route smoke missing: $required" }
}
foreach ($required in @(
    'RejectsVPetCoreWithoutOpeningFlyingBundleAsync',
    'runtime-unsupported:VPetCore',
    'VPetCore route must not open the FlyingHost bundle'
)) {
    if ($flyingSmoke -notmatch [regex]::Escape($required)) { Fail "Flying anti-cross-route smoke missing: $required" }
}

if ($solution -notmatch [regex]::Escape('src\FACM.FlyingHost\FACM.FlyingHost.csproj')) {
    Fail 'FACM4.sln must build FACM.FlyingHost as a first-class project.'
}

Write-Host 'FlyingSprite -> FACM.FlyingHost ownership: OK'
Write-Host 'VPetCore -> FACM.PetHost ownership: OK'
Write-Host 'FlyingHost root/source namespace isolation: OK'
Write-Host 'Cross-route payload opening is rejected before bundle preparation: OK'
Write-Host 'Separate bundle resource/cache identities: OK'
Write-Host 'Thread-safe runtime router ownership: OK'
Write-Host 'FACM 4.0 desktop pet runtime split contract: SUCCESS'
