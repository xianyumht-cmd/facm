[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [Parameter(Mandatory = $true)]
    [string]$PfxPath,

    [string]$PfxPassword = "",

    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$ExePath = (Resolve-Path $ExePath).Path
$PfxPath = (Resolve-Path $PfxPath).Path

$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $signtool) { throw "未找到 signtool.exe，请安装 Windows 10/11 SDK。" }

$arguments = @("sign", "/fd", "SHA256", "/td", "SHA256", "/tr", $TimestampUrl, "/f", $PfxPath)
if ($PfxPassword.Length -gt 0) { $arguments += @("/p", $PfxPassword) }
$arguments += $ExePath

& $signtool.FullName @arguments
if ($LASTEXITCODE -ne 0) { throw "签名失败，退出码 $LASTEXITCODE" }

& $signtool.FullName verify /pa /all /v $ExePath
if ($LASTEXITCODE -ne 0) { throw "签名验证失败，退出码 $LASTEXITCODE" }

Write-Host "签名与验证完成：$ExePath"
