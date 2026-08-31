param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
function Read-Required([string]$RelativePath) {
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path)) { throw "BOOT-2 contract file missing: $RelativePath" }
    return Get-Content -LiteralPath $path -Raw
}
function Require([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text -notmatch $Pattern) { throw $Message }
}

$cmake = Read-Required 'src/FACM.Bootstrapper/CMakeLists.txt'
$native = Read-Required 'src/FACM.Bootstrapper/main.cpp'
$build = Read-Required 'tools/boot1/Build-Boot2Candidate.ps1'
$server = Read-Required 'tools/boot1/Start-Boot2TestMirror.ps1'
$test = Read-Required 'tools/boot1/Test-Boot2.ps1'
$bootCore = Read-Required 'src/FACM.App/Properties/PublishProfiles/BootCore.pubxml'

Require $cmake 'winhttp' 'BOOT-2 native target must link WinHTTP.'
Require $cmake 'cabinet' 'BOOT-2 native target must link Cabinet FDI.'
foreach ($marker in @('WinHttpOpen','WinHttpCrackUrl','WinHttpAddRequestHeaders','Range: bytes=','.partial','VerifyPackAgainstManifest','FDICreate','FDICopy','IsSafeArchivePath','IsReparsePoint','components.json','active-composition-committed','--update','allow-unsigned-local')) {
    Require $native $marker "BOOT-2 native boundary missing: $marker"
}
foreach ($component in @('facm-app-win-x64','facm-dotnet-runtime-win-x64','facm-windows-runtime-win-x64')) {
    Require $build ([regex]::Escape($component)) "BOOT-2 component ID missing from packager: $component"
}
foreach ($field in @('packageSize','installedSize','sha256','contentDigest','fileCount','packageFormat','primaryUrl','mirrors','dependencies','ownership-report.json')) {
    Require $build ([regex]::Escape($field)) "BOOT-2 manifest/ownership field missing: $field"
}
Require $build 'CompressionType=MSZIP' 'BOOT-2 CAB compression must be MSZIP.'
Require $build 'MaxDiskSize=2147483136' 'BOOT-2 CAB output must use one 512-byte-aligned bounded cabinet.'
Require $native 'https://|IsLocalDevelopmentUrl' 'BOOT-2 URL policy must distinguish HTTPS from local development HTTP.'
Require $native 'trustMode.*unsigned-local|unsigned-local' 'BOOT-2 must expose explicit local unsigned trust mode.'
Require $build 'FACMIncludeEmbeddedPetPayload=false' 'BOOT-2 app source must be no-pet.'
Require $bootCore 'PublishSingleFile>false|PublishSingleFile=false' 'BOOT-2 Core must remain app-local multi-file.'
foreach ($forbidden in @('Compress-Archive','Expand-Archive','7z.exe','WinRAR')) {
    if ($native -match [regex]::Escape($forbidden) -or $build -match [regex]::Escape($forbidden) -or $test -match [regex]::Escape($forbidden)) {
        throw "BOOT-2 must not depend on forbidden archive mechanism: $forbidden"
    }
}
Require $server 'Range' 'BOOT-2 local mirror must support HTTP Range.'
Require $test 'MirrorFailoverAndRangeResumeSmoke' 'BOOT-2 smoke must exercise failover and resume.'
Require $test 'AppOnlyDownloadsOnlyAppPackSmoke' 'BOOT-2 smoke must exercise app-only update scope.'
Require $test 'RuntimeOnlyDownloadsAppAndDotnetOnlySmoke' 'BOOT-2 smoke must exercise runtime update scope.'
Write-Host 'BOOT-2 component ownership/manifest/HTTPS/no-pet/archive/update source contract: SUCCESS'
