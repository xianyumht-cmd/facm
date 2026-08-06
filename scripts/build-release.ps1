[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "FACM.sln"
$artifactDir = Join-Path $repoRoot "artifacts"
$outputExe = Join-Path $repoRoot "src\FACM\bin\$Configuration\net48\FACM.exe"

$msbuild = Get-Command msbuild.exe -ErrorAction SilentlyContinue
if (-not $msbuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "未找到 MSBuild。请安装 Visual Studio 2022 Build Tools 与 .NET Framework 4.8 targeting pack。"
    }
    $msbuildPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if (-not $msbuildPath) { throw "未找到 MSBuild.exe。" }
    $msbuild = Get-Item $msbuildPath
}

if (Test-Path $artifactDir) { Remove-Item $artifactDir -Recurse -Force }
New-Item -ItemType Directory -Path $artifactDir | Out-Null

& $msbuild.Source $solution /restore /m /p:Configuration=$Configuration /p:Platform="Any CPU" /v:minimal
if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码 $LASTEXITCODE" }
if (-not (Test-Path $outputExe)) { throw "构建完成但未找到 $outputExe" }

Copy-Item $outputExe (Join-Path $artifactDir "FACM.exe") -Force
Get-FileHash (Join-Path $artifactDir "FACM.exe") -Algorithm SHA256 |
    Format-List |
    Out-File (Join-Path $artifactDir "SHA256.txt") -Encoding utf8

Write-Host "构建完成：$artifactDir"
