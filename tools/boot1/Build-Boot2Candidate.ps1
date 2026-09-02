param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$ReviewRoot = 'D:\project2\facm-boot2-review-20260831',
    [string]$MirrorRoot = 'D:\project2\facm-boot2-mirror-20260831',
    [string]$Version = '4.0.0-boot2',
    [int]$MirrorPort = 18085,
    [string]$NuGetPackages = 'D:\project2\facm-boot2-nuget',
    [string]$BuildRoot = 'D:\project2\facm-boot2-build-20260831'
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path $RepoRoot).Path
$ReviewRoot = [IO.Path]::GetFullPath($ReviewRoot)
$MirrorRoot = [IO.Path]::GetFullPath($MirrorRoot)
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
foreach ($path in @($ReviewRoot, $MirrorRoot, $BuildRoot)) {
    if (-not $path.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "BOOT-2 output/build roots must remain under D:\project2: $path"
    }
}
if (-not ([IO.Path]::GetFullPath($NuGetPackages)).StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "BOOT-2 NuGet packages must remain under D:\project2: $NuGetPackages"
}
if ($Version -notmatch '^[A-Za-z0-9._-]+$') { throw "Invalid application version: $Version" }
$versionBase = (($Version -split '[-+]', 2)[0]).Trim()
$versionMatch = [regex]::Match($versionBase, '^(\d+)\.(\d+)\.(\d+)$')
if (-not $versionMatch.Success) { throw "Version must begin with a three-part numeric release version: $Version" }
$nativeFileVersion = "{0},{1},{2},0" -f $versionMatch.Groups[1].Value, $versionMatch.Groups[2].Value, $versionMatch.Groups[3].Value

function Remove-Scope([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to remove outside D:\project2: $full" }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
function New-CleanDirectory([string]$Path) {
    Remove-Scope $Path
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}
function Convert-ToUnixRelative([string]$Path) { return ($Path -replace '\\','/') }
function Get-Sha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-DirectoryDigest([string]$Root) {
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File)) {
        [void]$paths.Add((Convert-ToUnixRelative ([IO.Path]::GetRelativePath($Root, $file.FullName))))
    }
    $paths.Sort([StringComparer]::Ordinal)
    $rows = @()
    foreach ($relative in $paths) {
        $file = Get-Item -LiteralPath (Join-Path $Root ($relative -replace '/', '\'))
        $rows += $relative
        $rows += (Get-Sha256 $file.FullName)
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($rows -join "`n") + "`n")
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Get-ComponentOwner([IO.FileInfo]$File) {
    $name = $File.Name.ToLowerInvariant()
    $relative = (Convert-ToUnixRelative ([IO.Path]::GetRelativePath($script:CorePublish, $File.FullName))).ToLowerInvariant()
    if ($name -match '^facm(\.|$)' -or $name -match '^(onnxruntime|directml)(\.|$)' -or $name -match '^(microsoft\.ml|newtonsoft\.json|humanizer)(\.|$)') { return 'facm-app-win-x64' }
    if ($name -match '^(system\.|microsoft\.csharp|microsoft\.visualbasic|microsoft\.win32|netstandard|hostfxr|hostpolicy|coreclr|clrjit|clrgc|clrgcexp|mscordaccore|mscordbi|createdump|windowsbase|presentation|accessibility|uiautomation|system\.drawing|system\.windows\.forms)') { return 'facm-dotnet-runtime-win-x64' }
    if ($name -match '^(microsoft\.ui|microsoft\.windows|microsoft\.winui|winrt\.|webview2|webview2loader|dwritecore|dcomp|dwmcore|dwmscen|coremessaging|microsoft\.internal\.frameworkudk|microsoft\.directmanipulation|msquic|perceptivestreaming)') { return 'facm-windows-runtime-win-x64' }
    if ($relative -match '(^|/)runtimes/(win|win-x64)/') {
        if ($name -match '^(system|microsoft\.extensions|microsoft\.dependency|microsoft\.build)') { return 'facm-dotnet-runtime-win-x64' }
    }
    return 'facm-app-win-x64'
}
function New-CabPackage([string]$Stage, [string]$OutputDirectory, [string]$CabinetName, [string]$WorkDirectory) {
    New-Item -ItemType Directory -Force -Path $OutputDirectory,$WorkDirectory | Out-Null
    $ddf = Join-Path $WorkDirectory ($CabinetName + '.ddf')
    $lines = @(
        '.OPTION EXPLICIT',
        ('.Set CabinetNameTemplate=' + $CabinetName),
        ('.Set DiskDirectoryTemplate=' + $OutputDirectory),
        '.Set MaxDiskSize=2147483136',
        '.Set Cabinet=on',
        '.Set Compress=on',
        '.Set CompressionType=MSZIP'
    )
    foreach ($file in @(Get-ChildItem -LiteralPath $Stage -Recurse -File | Sort-Object { Convert-ToUnixRelative ([IO.Path]::GetRelativePath($Stage, $_.FullName)) })) {
        $relative = Convert-ToUnixRelative ([IO.Path]::GetRelativePath($Stage, $file.FullName))
        $lines += ('"' + $file.FullName + '" "' + $relative + '"')
    }
    Set-Content -LiteralPath $ddf -Value $lines -Encoding ascii
    $makecab = Join-Path $env:WINDIR 'System32\makecab.exe'
    if (-not (Test-Path -LiteralPath $makecab)) { throw 'Windows makecab.exe is required for BOOT-2 CAB packaging.' }
    Push-Location $WorkDirectory
    $log = Join-Path $WorkDirectory 'makecab.log'
    try { & $makecab /F $ddf *> $log } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "makecab failed for $CabinetName ($LASTEXITCODE)." }
    $cab = Join-Path $OutputDirectory $CabinetName
    if (-not (Test-Path -LiteralPath $cab)) { throw "CAB output missing: $cab" }
    return (Get-Item -LiteralPath $cab)
}

$CorePublish = Join-Path $BuildRoot 'core-publish'
$BootstrapBuild = Join-Path $BuildRoot 'bootstrap'
$ComponentStageRoot = Join-Path $BuildRoot 'component-stages'
$MakeCabWork = Join-Path $BuildRoot 'makecab'
New-CleanDirectory $ReviewRoot
New-CleanDirectory $MirrorRoot
New-CleanDirectory $BuildRoot
New-Item -ItemType Directory -Force -Path $CorePublish,$BootstrapBuild,$ComponentStageRoot,$MakeCabWork,$NuGetPackages | Out-Null
$env:NUGET_PACKAGES = $NuGetPackages
$env:DOTNET_CLI_HOME = 'D:\project2\facm-boot2-dotnet-home'
$env:TEMP = 'D:\project2\facm-boot2-temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null

Write-Host 'Publishing app-local no-pet source once for component classification...'
$dotnet = if (Test-Path -LiteralPath 'D:\project2\dotnet10\dotnet.exe') { 'D:\project2\dotnet10\dotnet.exe' } else { (Get-Command dotnet -ErrorAction Stop).Source }
& $dotnet publish (Join-Path $RepoRoot 'src\FACM.App\FACM.App.csproj') `
    --configuration Release --runtime win-x64 --self-contained true --output $CorePublish `
    --property PublishProfile=BootCore --property FACMIncludeEmbeddedPetPayload=false `
    --property RestorePackagesPath=$NuGetPackages
if ($LASTEXITCODE -ne 0) { throw "FACM.App publish failed ($LASTEXITCODE)." }

$toolchainBin = 'D:\project2\w64devkit-2.9.1\w64devkit\bin'
$cmake = Get-Command cmake -ErrorAction SilentlyContinue
if (-not $cmake -and (Test-Path -LiteralPath (Join-Path $toolchainBin 'cmake.exe'))) { $env:PATH = $toolchainBin + ';' + $env:PATH; $cmake = Get-Command cmake }
if (-not $cmake) { throw 'CMake is required to build the native BOOT-2 bootstrapper.' }
Write-Host 'Building native BOOT-2 bootstrapper...'
cmake -S (Join-Path $RepoRoot 'src\FACM.Bootstrapper') -B $BootstrapBuild -DCMAKE_BUILD_TYPE=Release `
    "-DFACM_BOOTSTRAP_FILE_VERSION=$nativeFileVersion" `
    "-DFACM_BOOTSTRAP_PRODUCT_VERSION=$nativeFileVersion"
if ($LASTEXITCODE -ne 0) { throw "Bootstrapper configure failed ($LASTEXITCODE)." }
cmake --build $BootstrapBuild --config Release --parallel 2
if ($LASTEXITCODE -ne 0) { throw "Bootstrapper build failed ($LASTEXITCODE)." }
$bootstrapExe = Get-ChildItem -LiteralPath $BootstrapBuild -Filter FACM.exe -Recurse -File | Select-Object -First 1
if (-not $bootstrapExe) { throw 'Native BOOT-2 FACM.exe was not found.' }

$componentIds = @('facm-app-win-x64','facm-dotnet-runtime-win-x64','facm-windows-runtime-win-x64')
$stages = @{}
foreach ($id in $componentIds) { $stage = Join-Path $ComponentStageRoot $id; New-Item -ItemType Directory -Force -Path $stage | Out-Null; $stages[$id] = $stage }
$ownership = New-Object System.Collections.Generic.List[object]
foreach ($file in @(Get-ChildItem -LiteralPath $CorePublish -Recurse -File)) {
    $owner = Get-ComponentOwner $file
    $relative = Convert-ToUnixRelative ([IO.Path]::GetRelativePath($CorePublish, $file.FullName))
    $target = Join-Path $stages[$owner] ($relative -replace '/', '\')
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    $ownership.Add([ordered]@{ path=$relative; owner=$owner; size=[uint64]$file.Length; sha256=(Get-Sha256 $file.FullName) })
}
$duplicatePaths = @($ownership | Group-Object { $_['path'] } | Where-Object Count -gt 1 | ForEach-Object { $_.Name })
if ($duplicatePaths.Count -ne 0) { throw "Duplicate ownership paths: $($duplicatePaths -join ', ')" }

$componentRecords = @()
$componentOutputRoot = Join-Path $MirrorRoot 'components'
foreach ($id in @('facm-dotnet-runtime-win-x64','facm-windows-runtime-win-x64','facm-app-win-x64')) {
    $stage = $stages[$id]
    $componentDirectory = Join-Path (Join-Path $componentOutputRoot $id) $Version
    $packName = "$id-$Version.cab"
    $pack = New-CabPackage $stage $componentDirectory $packName (Join-Path $MakeCabWork $id)
    $files = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
    $installedSize = [uint64](($files | Measure-Object -Property Length -Sum).Sum)
    $contentDigest = Get-DirectoryDigest $stage
    $primary = "http://127.0.0.1:$MirrorPort/unavailable/components/$id/$Version/$packName"
    $mirror = "http://127.0.0.1:$MirrorPort/components/$id/$Version/$packName"
    $dependencies = [System.Collections.Generic.List[string]]::new()
    if ($id -eq 'facm-app-win-x64') {
        [void]$dependencies.Add('facm-dotnet-runtime-win-x64')
        [void]$dependencies.Add('facm-windows-runtime-win-x64')
    }
    $entryPoint = ''
    if ($id -eq 'facm-app-win-x64') { $entryPoint = 'FACM.App.exe' }
    $record = [ordered]@{
        schemaVersion=2; componentId=$id; version=$Version; architecture='win-x64'; required=$true
        packageSize=[uint64]$pack.Length; installedSize=$installedSize; sha256=(Get-Sha256 $pack.FullName)
        contentDigest=$contentDigest; fileCount=[uint64]$files.Count; packageFormat='cab'
        entryPoint=$entryPoint
        primaryUrl=$primary; mirrors=@($mirror); dependencies=$dependencies
    }
    $componentRecords += $record
    $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $componentDirectory 'component.manifest.json') -Encoding utf8
}

$manifest = [ordered]@{
    schemaVersion=2; applicationId='FACM'; applicationVersion=$Version; architecture='win-x64'; trustMode='unsigned-local'
    components=$componentRecords
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $MirrorRoot 'manifest.json') -Encoding utf8
$ownershipReport = [ordered]@{
    schemaVersion=1; applicationVersion=$Version; sourceRoot=$CorePublish; duplicatePaths=@($duplicatePaths)
    componentSummary=@($componentIds | ForEach-Object {
        $componentId = $_
        $items = @($ownership | Where-Object { $_['owner'] -eq $componentId })
        $itemSize = (($items | ForEach-Object { [uint64]$_['size'] } | Measure-Object -Sum).Sum)
        [ordered]@{ componentId=$componentId; fileCount=[uint64]$items.Count; installedSize=[uint64]$itemSize; files=$items }
    })
}
$ownershipReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $MirrorRoot 'ownership-report.json') -Encoding utf8

$cleanRoot = Join-Path $ReviewRoot 'clean-first-run'
$preRoot = Join-Path $ReviewRoot 'pre-provisioned'
New-Item -ItemType Directory -Force -Path $cleanRoot,$preRoot | Out-Null
Copy-Item -LiteralPath $bootstrapExe.FullName -Destination (Join-Path $cleanRoot 'FACM.exe') -Force
Copy-Item -LiteralPath $bootstrapExe.FullName -Destination (Join-Path $preRoot 'FACM.exe') -Force
$bootstrapConfig = [ordered]@{ schemaVersion=1; manifestUrl="http://127.0.0.1:$MirrorPort/manifest.json"; allowInsecureLocal=$true; allowUnsignedLocal=$true }
$bootstrapConfig | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $cleanRoot 'bootstrap.json') -Encoding utf8
$bootstrapConfig | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $preRoot 'bootstrap.json') -Encoding utf8

$layout = [ordered]@{
    schemaVersion=1; applicationVersion=$Version; bootstrapSha256=(Get-Sha256 $bootstrapExe.FullName)
    bootstrapBytes=[uint64]$bootstrapExe.Length; components=$componentRecords
    roots=[ordered]@{ clean=$cleanRoot; preProvisioned=$preRoot; mirror=$MirrorRoot }
}
$layout | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ReviewRoot 'boot2-layout.json') -Encoding utf8
Write-Host "BOOT-2 review root: $ReviewRoot"
Write-Host "BOOT-2 mirror root: $MirrorRoot"
Write-Host "FACM.exe bytes: $($bootstrapExe.Length)"
foreach ($record in $componentRecords) { Write-Host "$($record.componentId): raw=$($record.installedSize) pack=$($record.packageSize) files=$($record.fileCount)" }
