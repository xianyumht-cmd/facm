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

if (Test-Path $artifactDir) { Remove-Item $artifactDir -Recurse -Force }
New-Item -ItemType Directory -Path $artifactDir | Out-Null

& $msbuildPath $solution /restore /t:Rebuild /m /p:Configuration=$Configuration /p:Platform="Any CPU" /p:ContinuousIntegrationBuild=true /v:minimal
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
Write-Host "Embedded tool bundle verified."

$artifactExe = Join-Path $artifactDir "FACM.exe"
Copy-Item $outputExe $artifactExe -Force
$hash = Get-FileHash $artifactExe -Algorithm SHA256
"$($hash.Hash) *FACM.exe" | Set-Content (Join-Path $artifactDir "SHA256.txt") -Encoding ascii
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

if (Test-Path $packagePath) { Remove-Item $packagePath -Force }
Compress-Archive -Path "$artifactDir\*" -DestinationPath $packagePath -Force

Write-Host "Build completed: $artifactExe"
Write-Host "Package: $packagePath"
Write-Host "SHA-256: $($hash.Hash)"
