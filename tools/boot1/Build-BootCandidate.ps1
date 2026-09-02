param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$OutputRoot = 'D:\project2\facm-boot1-review-20260831',
    [string]$Version = '4.0.0-boot1',
    [string]$NuGetPackages = 'D:\project2\facm-boot1-nuget',
    [string]$BuildRoot = 'D:\project2\facm-boot1-build-20260831'
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path $RepoRoot).Path
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
$CorePublish = Join-Path $BuildRoot 'core-publish'
$BootstrapBuild = Join-Path $BuildRoot 'bootstrap'
$CorePack = Join-Path $OutputRoot ".facm\components\facm-core-win-x64\$Version\facm-core-win-x64-$Version.zip"
$CoreManifest = Join-Path $OutputRoot ".facm\components\facm-core-win-x64\$Version\component.manifest.json"

if (-not $OutputRoot.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "BOOT-1 output must remain under D:\project2: $OutputRoot"
}
if (-not $BuildRoot.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "BOOT-1 build root must remain under D:\project2: $BuildRoot"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
if (Test-Path -LiteralPath (Join-Path $OutputRoot '_build')) {
    Remove-Item -LiteralPath (Join-Path $OutputRoot '_build') -Recurse -Force
}
if (Test-Path -LiteralPath $CorePublish) { Remove-Item -LiteralPath $CorePublish -Recurse -Force }
if (Test-Path -LiteralPath $BootstrapBuild) { Remove-Item -LiteralPath $BootstrapBuild -Recurse -Force }
New-Item -ItemType Directory -Force -Path $CorePublish,$BootstrapBuild | Out-Null
$env:NUGET_PACKAGES = $NuGetPackages
$env:TEMP = 'D:\project2\facm-boot1-temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP,$NuGetPackages | Out-Null

Write-Host 'Publishing app-local no-pet Core...'
$dotnet = if (Test-Path -LiteralPath 'D:\project2\dotnet10\dotnet.exe') { 'D:\project2\dotnet10\dotnet.exe' } else { (Get-Command dotnet -ErrorAction Stop).Source }
& $dotnet publish (Join-Path $RepoRoot 'src\FACM.App\FACM.App.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $CorePublish `
    --property PublishProfile=BootCore `
    --property FACMIncludeEmbeddedPetPayload=false `
    --property RestorePackagesPath=$NuGetPackages
if ($LASTEXITCODE -ne 0) { throw "FACM.App Core publish failed ($LASTEXITCODE)." }

$toolchainBin = 'D:\project2\w64devkit-2.9.1\w64devkit\bin'
$cmake = Get-Command cmake -ErrorAction SilentlyContinue
if (-not $cmake -and (Test-Path -LiteralPath (Join-Path $toolchainBin 'cmake.exe'))) {
    $env:PATH = $toolchainBin + ';' + $env:PATH
    $cmake = Get-Command cmake -ErrorAction SilentlyContinue
}
if (-not $cmake) { throw 'CMake is required to build the native Bootstrapper; install/use a C++ toolchain under D:\project2.' }
Write-Host 'Building native Win32 Bootstrapper...'
cmake -S (Join-Path $RepoRoot 'src\FACM.Bootstrapper') -B $BootstrapBuild -DCMAKE_BUILD_TYPE=Release
if ($LASTEXITCODE -ne 0) { throw "Bootstrapper configure failed ($LASTEXITCODE)." }
cmake --build $BootstrapBuild --config Release --parallel
if ($LASTEXITCODE -ne 0) { throw "Bootstrapper build failed ($LASTEXITCODE)." }

$bootstrapExe = Get-ChildItem -LiteralPath $BootstrapBuild -Filter FACM.exe -Recurse -File | Select-Object -First 1
if (-not $bootstrapExe) { throw 'Native Bootstrapper output FACM.exe was not found.' }

Write-Host 'Creating review distribution layout...'
$directories = @(
    (Join-Path $OutputRoot '.facm\app'),
    (Join-Path $OutputRoot '.facm\runtime'),
    (Join-Path $OutputRoot '.facm\components\facm-core-win-x64'),
    (Join-Path $OutputRoot '.facm\versions'),
    (Join-Path $OutputRoot '.facm\staging'),
    (Join-Path $OutputRoot '.facm\cache'),
    (Join-Path $OutputRoot '.facm\logs'),
    (Join-Path $OutputRoot '.facm\state')
)
New-Item -ItemType Directory -Force -Path $directories | Out-Null
Copy-Item -LiteralPath $bootstrapExe.FullName -Destination (Join-Path $OutputRoot 'FACM.exe') -Force
$activeCore = Join-Path $OutputRoot ".facm\versions\$Version"
New-Item -ItemType Directory -Force -Path $activeCore | Out-Null
Copy-Item -Path (Join-Path $CorePublish '*') -Destination $activeCore -Recurse -Force

Write-Host 'Creating deterministic ZIP component pack...'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $CorePack) | Out-Null
if (Test-Path -LiteralPath $CorePack) { Remove-Item -LiteralPath $CorePack -Force }
Compress-Archive -Path (Join-Path $CorePublish '*') -DestinationPath $CorePack -CompressionLevel Optimal
$packInfo = Get-Item -LiteralPath $CorePack
$packHash = (Get-FileHash -LiteralPath $CorePack -Algorithm SHA256).Hash.ToLowerInvariant()
$coreFiles = @(Get-ChildItem -LiteralPath $CorePublish -Recurse -File)
$installedSize = ($coreFiles | Measure-Object -Property Length -Sum).Sum
$manifest = [ordered]@{
    schemaVersion = 1
    componentId = 'facm-core-win-x64'
    version = $Version
    architecture = 'win-x64'
    required = $true
    packageSize = [uint64]$packInfo.Length
    installedSize = [uint64]$installedSize
    sha256 = $packHash
    entryPoint = 'FACM.App.exe'
    dependencies = @()
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $CoreManifest -Encoding utf8

$state = [ordered]@{
    schemaVersion = 1
    activeVersion = $Version
    activePath = ".facm/versions/$Version"
    previousVersion = ''
    lastSuccessfulLaunch = ''
}
$state | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $OutputRoot '.facm\state\active.json') -Encoding utf8

Write-Host "BOOT-1 review distribution: $OutputRoot"
Write-Host "FACM.exe bytes: $((Get-Item (Join-Path $OutputRoot 'FACM.exe')).Length)"
Write-Host "Core files: $($coreFiles.Count); raw bytes: $installedSize; pack bytes: $($packInfo.Length)"
