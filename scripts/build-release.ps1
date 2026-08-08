[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "FACM.sln"
$artifactDir = Join-Path $repoRoot "artifacts"
$outputExe = Join-Path $repoRoot "src\FACM\bin\$Configuration\net48\FACM.exe"
$packagePath = Join-Path $repoRoot "FACM-Windows-x64.zip"
$toolManifestPath = Join-Path $repoRoot "tools\EXTRACTED-TOOLS.json"
$petHostProject = Join-Path $repoRoot "src\FACM.PetHost\FACM.PetHost.csproj"
$petHostPublish = Join-Path $repoRoot "out\PetHostPublish"
$petHostBundle = Join-Path $repoRoot "out\PetHostBundle.zip"

if (-not (Test-Path $toolManifestPath -PathType Leaf)) {
    throw "Tool manifest is missing: $toolManifestPath"
}

$toolManifest = Get-Content $toolManifestPath -Raw -Encoding utf8 | ConvertFrom-Json
foreach ($entry in $toolManifest.files) {
    $toolPath = Join-Path $repoRoot ("tools\" + [string]$entry.name)
    if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
        throw "Bundled tool is missing: $($entry.name)"
    }
    $item = Get-Item -LiteralPath $toolPath
    if ($item.Length -ne [long]$entry.size) {
        throw "Bundled tool size mismatch: $($entry.name)"
    }
    $actualHash = (Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash
    if ($actualHash -ne [string]$entry.sha256) {
        throw "Bundled tool SHA-256 mismatch: $($entry.name)"
    }
    Write-Host "Verified: $($entry.name)"
}

$msbuildCommand = Get-Command msbuild.exe -ErrorAction SilentlyContinue
$msbuildPath = if ($msbuildCommand) { $msbuildCommand.Source } else { $null }
if (-not $msbuildPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "MSBuild was not found. Run the repository root setup BAT first."
    }
    $msbuildPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if (-not $msbuildPath) { throw "MSBuild.exe was not found." }
}

if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found. FACM release builds require .NET 8 to publish PetHost."
}

if (Test-Path $artifactDir) { Remove-Item $artifactDir -Recurse -Force }
New-Item -ItemType Directory -Path $artifactDir | Out-Null
if (Test-Path $petHostPublish) { Remove-Item $petHostPublish -Recurse -Force }
if (Test-Path $petHostBundle) { Remove-Item $petHostBundle -Force }

& dotnet publish $petHostProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -o $petHostPublish
if ($LASTEXITCODE -ne 0) { throw "PetHost publish failed. Exit code: $LASTEXITCODE" }

$petHostExe = Join-Path $petHostPublish "FACM.PetHost.exe"
if (-not (Test-Path $petHostExe -PathType Leaf)) { throw "PetHost executable was not found: $petHostExe" }
$selfTestRoot = Join-Path $env:TEMP "FACM-PetHost-Local-SelfTest"
if (Test-Path $selfTestRoot) { Remove-Item $selfTestRoot -Recurse -Force }
New-Item -ItemType Directory -Path $selfTestRoot -Force | Out-Null
$selfTest = Start-Process -FilePath $petHostExe -ArgumentList "--self-test --data-root `"$selfTestRoot`"" -Wait -PassThru
if ($selfTest.ExitCode -ne 0) { throw "PetHost self-test failed. Exit code: $($selfTest.ExitCode)" }

Compress-Archive -Path "$petHostPublish\*" -DestinationPath $petHostBundle -CompressionLevel Optimal -Force
if (-not (Test-Path $petHostBundle -PathType Leaf)) { throw "PetHost bundle was not created: $petHostBundle" }
$petHostBundleHash = Get-FileHash $petHostBundle -Algorithm SHA256
$petHostExeHash = Get-FileHash $petHostExe -Algorithm SHA256
Write-Host "PetHost bundle verified: $($petHostBundleHash.Hash)"

& $msbuildPath $solution /restore /t:Rebuild /m /p:Configuration=$Configuration /p:Platform="Any CPU" /p:ContinuousIntegrationBuild=true /p:RequirePetHostBundle=true /p:PetHostBundlePath="$petHostBundle" /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed. Exit code: $LASTEXITCODE" }
if (-not (Test-Path $outputExe -PathType Leaf)) { throw "Build completed but output was not found: $outputExe" }

$resolvedOutputExe = (Resolve-Path $outputExe).Path
if ($PSVersionTable.PSVersion.Major -ge 6) {
    $resourceVerifier = Join-Path $env:TEMP "facm-local-resource-verifier.ps1"
    @'
param([Parameter(Mandatory=$true)][string]$ExePath)
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($ExePath)
$assembly.GetManifestResourceNames()
'@ | Set-Content -LiteralPath $resourceVerifier -Encoding utf8

    $windowsPowerShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    $resources = @(
        & $windowsPowerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass `
            -File $resourceVerifier -ExePath $resolvedOutputExe
    )
    if ($LASTEXITCODE -ne 0) {
        throw "FACM.exe resource verification failed. Exit code: $LASTEXITCODE"
    }
}
else {
    $assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($resolvedOutputExe)
    $resources = @($assembly.GetManifestResourceNames())
}

Write-Host "FACM.exe embedded resources:"
$resources | Sort-Object | ForEach-Object { Write-Host ("  - " + $_) }

if ($resources -notcontains 'FACM.Resources.FACM.ToolBundle.dll') {
    throw "FACM.ToolBundle.dll was not embedded in FACM.exe."
}
if ($resources -notcontains 'FACM.Resources.PetHost.zip') {
    throw "PetHost bundle was not embedded in FACM.exe."
}
Write-Host "Embedded tool and PetHost bundles verified."

$artifactExe = Join-Path $artifactDir "FACM.exe"
Copy-Item $outputExe $artifactExe -Force
$hash = Get-FileHash $artifactExe -Algorithm SHA256
@(
    "$($hash.Hash) *FACM.exe",
    "$($petHostBundleHash.Hash) *embedded:FACM.Resources.PetHost.zip",
    "$($petHostExeHash.Hash) *embedded:PetHost/FACM.PetHost.exe"
) | Set-Content (Join-Path $artifactDir "SHA256.txt") -Encoding ascii
Get-AuthenticodeSignature $artifactExe |
    Format-List Status,StatusMessage,SignerCertificate,TimeStamperCertificate |
    Out-File (Join-Path $artifactDir "SIGNATURE.txt") -Encoding utf8

$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($artifactExe)
$buildInfo = [ordered]@{
    file_version = $version.FileVersion
    product_version = $version.ProductVersion
    sha256 = $hash.Hash
    signature_status = [string](Get-AuthenticodeSignature $artifactExe).Status
    tool_bundle = "embedded FACM.ToolBundle.dll"
    pet_host = "embedded ZIP; self-contained .NET 8 x64"
    pet_host_bundle_sha256 = $petHostBundleHash.Hash
    pet_host_exe_sha256 = $petHostExeHash.Hash
    pet_host_runtime = "auto-extracted under FACM/runtime/pethost-host/<FACM-MVID>"
    built_at_utc = [DateTime]::UtcNow.ToString("o")
}
$buildInfo | ConvertTo-Json | Set-Content (Join-Path $artifactDir "BUILD-INFO.json") -Encoding utf8

Copy-Item (Join-Path $repoRoot "README.md") (Join-Path $artifactDir "README.md") -Force
Copy-Item (Join-Path $repoRoot "docs\DEVELOPER-CLEANUP-CONFIG.md") (Join-Path $artifactDir "DEVELOPER-CLEANUP-CONFIG.md") -Force
Copy-Item (Join-Path $repoRoot "docs\SIGNING.md") (Join-Path $artifactDir "SIGNING.md") -Force
if (Test-Path (Join-Path $repoRoot "docs\ONLINE-MANAGEMENT.md")) {
    Copy-Item (Join-Path $repoRoot "docs\ONLINE-MANAGEMENT.md") (Join-Path $artifactDir "ONLINE-MANAGEMENT.md") -Force
}
if (Test-Path (Join-Path $repoRoot "docs\PORTABLE-LAYOUT.md")) {
    Copy-Item (Join-Path $repoRoot "docs\PORTABLE-LAYOUT.md") (Join-Path $artifactDir "PORTABLE-LAYOUT.md") -Force
}
if (Test-Path (Join-Path $repoRoot "docs\VPET-PETHOST.md")) {
    Copy-Item (Join-Path $repoRoot "docs\VPET-PETHOST.md") (Join-Path $artifactDir "VPET-PETHOST.md") -Force
}

if (Test-Path $packagePath) { Remove-Item $packagePath -Force }
Compress-Archive -Path "$artifactDir\*" -DestinationPath $packagePath -Force

Write-Host "Build completed: $artifactExe"
Write-Host "Package: $packagePath"
Write-Host "SHA-256: $($hash.Hash)"
