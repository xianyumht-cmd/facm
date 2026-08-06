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
    throw "缺少内置工具校验清单：$toolManifestPath"
}

$toolManifest = Get-Content $toolManifestPath -Raw -Encoding utf8 | ConvertFrom-Json
foreach ($entry in $toolManifest.files) {
    $toolPath = Join-Path $repoRoot ("tools\" + [string]$entry.name)
    if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
        throw "缺少内置工具：$($entry.name)"
    }
    $item = Get-Item -LiteralPath $toolPath
    if ($item.Length -ne [long]$entry.size) {
        throw "内置工具大小不一致：$($entry.name)"
    }
    $actualHash = (Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash
    if ($actualHash -ne [string]$entry.sha256) {
        throw "内置工具 SHA-256 不一致：$($entry.name)"
    }
    Write-Host "已校验：$($entry.name)"
}

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

& $msbuild.Source $solution /restore /m /p:Configuration=$Configuration /p:Platform="Any CPU" /p:ContinuousIntegrationBuild=true /v:minimal
if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码 $LASTEXITCODE" }
if (-not (Test-Path $outputExe -PathType Leaf)) { throw "构建完成但未找到 $outputExe" }

$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom((Resolve-Path $outputExe).Path)
if ($assembly.GetManifestResourceNames() -notcontains 'FACM.Resources.FACM.ToolBundle.dll') {
    throw "FACM.exe 中没有嵌入 FACM.ToolBundle.dll"
}
Write-Host "已校验嵌入工具资源 DLL"

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

if (Test-Path $packagePath) { Remove-Item $packagePath -Force }
Compress-Archive -Path "$artifactDir\*" -DestinationPath $packagePath -Force

Write-Host "构建完成：$artifactExe"
Write-Host "压缩包：$packagePath"
Write-Host "SHA-256：$($hash.Hash)"
