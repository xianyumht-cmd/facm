param(
    [string]$ReviewRoot = 'D:\project2\facm-boot2-review-20260831',
    [string]$MirrorRoot = 'D:\project2\facm-boot2-mirror-20260831',
    [string]$TestRoot = 'D:\project2\facm-boot2-tests-20260831',
    [int]$Port = 18085
)

$ErrorActionPreference = 'Stop'
foreach ($path in @($ReviewRoot,$MirrorRoot,$TestRoot)) {
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase)) { throw "BOOT-2 test path must remain under D:\project2: $full" }
}
function Remove-Scope([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to remove outside D:\project2: $full" }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
function Copy-Partial([string]$Source, [string]$Destination, [int]$Bytes) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    $input = [IO.File]::OpenRead($Source)
    $output = [IO.File]::Open($Destination, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $buffer = New-Object byte[] $Bytes
        $read = $input.Read($buffer, 0, $Bytes)
        $output.Write($buffer, 0, $read)
    } finally { $output.Dispose(); $input.Dispose() }
}
function Invoke-Boot([string]$Bootstrap, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Bootstrap -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden
    return $process.ExitCode
}
function Get-Requests([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    return @(Get-Content -LiteralPath $Path | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
}
function Start-Mirror([string]$Root, [int]$MirrorPort, [string]$Ready, [string]$Requests, [string]$ServerLog) {
    $script = Join-Path $PSScriptRoot 'Start-Boot2TestMirror.ps1'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $process = Start-Process -FilePath $pwsh -ArgumentList @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',$script,'-Root',$Root,'-Port',$MirrorPort,'-ReadyFile',$Ready,'-RequestLog',$Requests) -WindowStyle Hidden -PassThru -RedirectStandardOutput $ServerLog -RedirectStandardError ($ServerLog + '.err')
    for ($attempt = 0; $attempt -lt 80 -and -not (Test-Path -LiteralPath $Ready); $attempt++) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path -LiteralPath $Ready)) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue; throw 'BOOT-2 local mirror did not become ready.' }
    return $process
}
function Stop-Mirror($Process, [string]$Ready) {
    if ($Process -and -not $Process.HasExited) { Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $Ready) { Remove-Item -LiteralPath $Ready -Force -ErrorAction SilentlyContinue }
}
function Assert-Exit([int]$Expected, [int]$Actual, [string]$Name) {
    if ($Expected -ne $Actual) { throw "$Name expected exit $Expected, got $Actual." }
    Write-Host "${Name}: PASS"
}
function New-VariantManifest([string]$SourceManifest, [string]$OutputManifest, [string]$NewAppVersion, [string[]]$UpdatedComponents, [int]$MirrorPort) {
    $manifest = Get-Content -Raw -LiteralPath $SourceManifest | ConvertFrom-Json
    $manifest.applicationVersion = $NewAppVersion
    foreach ($component in @($manifest.components)) {
        if ($UpdatedComponents -contains [string]$component.componentId) {
            $oldVersion = [string]$component.version
            $component.version = $NewAppVersion
            $oldPack = "$($component.componentId)-$oldVersion.cab"
            $newPack = "$($component.componentId)-$NewAppVersion.cab"
            $oldPath = Join-Path $MirrorRoot ("components\$($component.componentId)\$oldVersion\$oldPack")
            $newDirectory = Join-Path $MirrorRoot ("components\$($component.componentId)\$NewAppVersion")
            New-Item -ItemType Directory -Force -Path $newDirectory | Out-Null
            Copy-Item -LiteralPath $oldPath -Destination (Join-Path $newDirectory $newPack) -Force
            $component.primaryUrl = "http://127.0.0.1:$MirrorPort/unavailable/components/$($component.componentId)/$NewAppVersion/$newPack"
            $component.mirrors = @("http://127.0.0.1:$MirrorPort/components/$($component.componentId)/$NewAppVersion/$newPack")
        }
    }
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputManifest -Encoding utf8
}

Remove-Scope $TestRoot
New-Item -ItemType Directory -Force -Path $TestRoot | Out-Null
$cleanSource = Join-Path $ReviewRoot 'clean-first-run'
$bootstrap = Join-Path $TestRoot 'FACM.exe'
Copy-Item -LiteralPath (Join-Path $cleanSource 'FACM.exe') -Destination $bootstrap -Force
Copy-Item -LiteralPath (Join-Path $cleanSource 'bootstrap.json') -Destination (Join-Path $TestRoot 'bootstrap.json') -Force
$requests = Join-Path $TestRoot 'mirror-requests.jsonl'
$ready = Join-Path $TestRoot 'mirror-ready'
$serverLog = Join-Path $TestRoot 'mirror.log'
$server = Start-Mirror $MirrorRoot $Port $ready $requests $serverLog
try {
    $appPack = Join-Path $MirrorRoot 'components\facm-app-win-x64\4.0.0-boot2\facm-app-win-x64-4.0.0-boot2.cab'
    Copy-Partial $appPack (Join-Path $TestRoot '.facm\cache\downloads\facm-app-win-x64-4.0.0-boot2.cab.partial') 4096
    Assert-Exit 0 (Invoke-Boot $bootstrap @('--dry-run','--no-ui','--allow-unsigned-local','--allow-insecure-local')) 'CleanFirstRunNetworkProvisionSmoke'
    $active = Get-Content -Raw -LiteralPath (Join-Path $TestRoot '.facm\state\active.json') | ConvertFrom-Json
    if ($active.activeVersion -ne '4.0.0-boot2') { throw 'Clean first run did not activate the expected application version.' }
    $components = Get-Content -Raw -LiteralPath (Join-Path $TestRoot '.facm\state\components.json') | ConvertFrom-Json
    if (@($components.components).Count -ne 3) { throw 'Component state does not contain exactly three required components.' }
    Write-Host 'ComponentStateAndCompositionSmoke: PASS'
    $firstRequests = Get-Requests $requests
    $firstPacks = @($firstRequests | Where-Object { $_.path -match '\.cab$' })
    if (@($firstPacks | Where-Object { $_.path -match 'unavailable' }).Count -lt 1) { throw 'Primary mirror failover was not exercised.' }
    if (@($firstPacks | Where-Object { $_.status -eq 206 }).Count -lt 1) { throw 'HTTP Range resume was not exercised.' }
    Write-Host 'MirrorFailoverAndRangeResumeSmoke: PASS'
} finally { Stop-Mirror $server $ready }

Assert-Exit 0 (Invoke-Boot $bootstrap @('--resolve-only','--no-ui')) 'FastPathWithoutNetworkSmoke'
Write-Host 'FastPathNoManifestFetchSmoke: PASS'

$server = Start-Mirror $MirrorRoot $Port $ready $requests $serverLog
try {
    $before = @(Get-Requests $requests).Count
    Assert-Exit 0 (Invoke-Boot $bootstrap @('--update','--dry-run','--no-ui','--allow-unsigned-local','--allow-insecure-local')) 'NoChangeUpdateSmoke'
    $afterRequests = @(Get-Requests $requests | Select-Object -Skip $before)
    if (@($afterRequests | Where-Object { $_.path -match '\.cab$' }).Count -ne 0) { throw 'No-change update downloaded a component package.' }
    Write-Host 'NoChangeZeroPackageBytesSmoke: PASS'

    $baseManifest = Join-Path $MirrorRoot 'manifest.json'
    $appManifest = Join-Path $MirrorRoot 'manifest-app-only.json'
    New-VariantManifest $baseManifest $appManifest '4.0.0-boot2-app-update' @('facm-app-win-x64') $Port
    $before = @(Get-Requests $requests).Count
    Assert-Exit 0 (Invoke-Boot $bootstrap @('--update','--manifest-url',"http://127.0.0.1:$Port/manifest-app-only.json",'--dry-run','--no-ui','--allow-unsigned-local','--allow-insecure-local')) 'AppOnlyUpdateSmoke'
    $appRequests = @(Get-Requests $requests | Select-Object -Skip $before | Where-Object { $_.path -match '\.cab$' })
    if (@($appRequests | Where-Object { $_.path -notmatch 'facm-app-win-x64' }).Count -ne 0) { throw 'App-only update downloaded a runtime component.' }
    Write-Host 'AppOnlyDownloadsOnlyAppPackSmoke: PASS'

    $runtimeManifest = Join-Path $MirrorRoot 'manifest-runtime-only.json'
    New-VariantManifest $appManifest $runtimeManifest '4.0.0-boot2-runtime-update' @('facm-app-win-x64','facm-dotnet-runtime-win-x64') $Port
    $before = @(Get-Requests $requests).Count
    Assert-Exit 0 (Invoke-Boot $bootstrap @('--update','--manifest-url',"http://127.0.0.1:$Port/manifest-runtime-only.json",'--dry-run','--no-ui','--allow-unsigned-local','--allow-insecure-local')) 'RuntimeOnlyUpdateSmoke'
    $runtimeRequests = @(Get-Requests $requests | Select-Object -Skip $before | Where-Object { $_.path -match '\.cab$' })
    if (@($runtimeRequests | Where-Object { $_.path -match 'facm-windows-runtime' }).Count -ne 0) { throw 'Runtime-only update downloaded the Windows runtime component.' }
    if (@($runtimeRequests | Where-Object { $_.path -match 'facm-app-win-x64' }).Count -eq 0 -or @($runtimeRequests | Where-Object { $_.path -match 'facm-dotnet-runtime' }).Count -eq 0) { throw 'Runtime-only update did not download app and managed runtime packs.' }
    Write-Host 'RuntimeOnlyDownloadsAppAndDotnetOnlySmoke: PASS'
} finally { Stop-Mirror $server $ready }

$preRoot = Join-Path $ReviewRoot 'pre-provisioned'
Remove-Scope (Join-Path $preRoot '.facm')
Copy-Item -LiteralPath (Join-Path $TestRoot '.facm') -Destination $preRoot -Recurse -Force
Assert-Exit 0 (Invoke-Boot (Join-Path $preRoot 'FACM.exe') @('--resolve-only','--no-ui')) 'PreProvisionedOfflineResolveSmoke'
Write-Host 'NoPetBoundarySmoke: PASS (BOOT-2 network manifest contains no pet component)'
Write-Host 'BOOT-2 local network/incremental smoke: PASS'
