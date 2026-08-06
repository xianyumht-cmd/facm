[CmdletBinding()]
param(
    [switch]$SetupOnly,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = Split-Path -Parent $PSScriptRoot
$logRoot = Join-Path $env:TEMP "FACM-LocalBuild"
$logPath = Join-Path $logRoot ("setup-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
$rebootRequired = $false

function Write-Stage {
    param([string]$Text)
    Write-Host ""
    Write-Host ("=" * 68) -ForegroundColor DarkCyan
    Write-Host ("  " + $Text) -ForegroundColor Cyan
    Write-Host ("=" * 68) -ForegroundColor DarkCyan
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-VsWherePath {
    $path = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $path -PathType Leaf) { return $path }
    return $null
}

function Find-MSBuild {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $vswhere = Get-VsWherePath
    if (-not $vswhere) { return $null }

    $path = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2>$null |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($path)) { return $null }
    return [string]$path
}

function Test-Net48TargetingPack {
    $reference = Join-Path ${env:ProgramFiles(x86)} "Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll"
    return Test-Path -LiteralPath $reference -PathType Leaf
}

function Find-SignTool {
    $root = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { return $null }

    $path = Get-ChildItem -LiteralPath $root -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $path) { return $null }
    return $path.FullName
}

function Download-File {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Write-Host "正在下载：$Url"
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Destination

    if (-not (Test-Path -LiteralPath $Destination -PathType Leaf)) {
        throw "下载完成后没有找到文件：$Destination"
    }
    if ((Get-Item -LiteralPath $Destination).Length -lt 100KB) {
        throw "下载文件异常，大小不足 100 KB：$Destination"
    }
}

function Install-BuildTools {
    $tempDir = Join-Path $env:TEMP "FACM-VSBuildTools"
    $bootstrapper = Join-Path $tempDir "vs_BuildTools.exe"
    $installPath = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools"

    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    Download-File -Url "https://aka.ms/vs/17/release/vs_BuildTools.exe" -Destination $bootstrapper

    $arguments = @(
        '--installPath', ('"' + $installPath + '"'),
        '--add', 'Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools',
        '--includeRecommended',
        '--add', 'Microsoft.Net.Component.4.8.SDK',
        '--add', 'Microsoft.Net.Component.4.8.TargetingPack',
        '--add', 'Microsoft.VisualStudio.Component.Windows10SDK.19041',
        '--passive',
        '--wait',
        '--norestart',
        '--nocache'
    ) -join ' '

    Write-Host "正在安装或补齐 Visual Studio 2022 Build Tools。"
    Write-Host "安装窗口可能持续十几分钟，请不要关闭。"
    $process = Start-Process -FilePath $bootstrapper -ArgumentList $arguments -Wait -PassThru

    if ($process.ExitCode -eq 3010) {
        $script:rebootRequired = $true
        Write-Warning "安装成功，但 Windows 提示需要重启。"
    }
    elseif ($process.ExitCode -ne 0) {
        throw "Visual Studio Build Tools 安装失败，退出码：$($process.ExitCode)"
    }
}

if ($env:OS -ne 'Windows_NT') {
    throw "此脚本只能在 Windows 上运行。"
}
if (-not (Test-Administrator)) {
    throw "当前没有管理员权限。请双击仓库根目录的 FACM-本地一键配置并构建.bat。"
}
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'FACM.sln') -PathType Leaf)) {
    throw "没有找到 FACM.sln。请把脚本放在完整 FACM 仓库的 scripts 目录中运行。"
}

New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
Start-Transcript -Path $logPath -Force | Out-Null

try {
    Write-Stage "检查本地构建环境"
    Write-Host "仓库目录：$repoRoot"
    Write-Host "日志文件：$logPath"

    $msbuild = Find-MSBuild
    $net48Ready = Test-Net48TargetingPack
    $signTool = Find-SignTool

    Write-Host ("MSBuild：" + $(if ($msbuild) { $msbuild } else { "缺失" }))
    Write-Host (".NET Framework 4.8 目标包：" + $(if ($net48Ready) { "已安装" } else { "缺失" }))
    Write-Host ("Windows SDK SignTool：" + $(if ($signTool) { $signTool } else { "缺失" }))

    if (-not $msbuild -or -not $net48Ready -or -not $signTool) {
        Write-Stage "安装缺失组件"
        Install-BuildTools
    }
    else {
        Write-Host "构建环境已完整，无需重复安装。" -ForegroundColor Green
    }

    Write-Stage "验证安装结果"
    $msbuild = Find-MSBuild
    $net48Ready = Test-Net48TargetingPack
    $signTool = Find-SignTool

    if (-not $msbuild) { throw "安装后仍未找到 MSBuild.exe。" }
    if (-not $net48Ready) { throw "安装后仍未找到 .NET Framework 4.8 targeting pack。" }
    if (-not $signTool) { throw "安装后仍未找到 Windows SDK signtool.exe。" }

    Write-Host "MSBuild：$msbuild" -ForegroundColor Green
    Write-Host ".NET Framework 4.8：正常" -ForegroundColor Green
    Write-Host "SignTool：$signTool" -ForegroundColor Green

    if ($rebootRequired) {
        Write-Warning "系统要求重启。建议先重启电脑，再重新双击 BAT 构建。"
    }

    if ($SetupOnly) {
        Write-Stage "环境配置完成"
        Write-Host "已按要求只配置环境，没有执行构建。" -ForegroundColor Green
        exit 0
    }

    Write-Stage "构建 FACM"
    $buildScript = Join-Path $PSScriptRoot "build-release.ps1"
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $buildScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "FACM 构建脚本返回退出码：$LASTEXITCODE"
    }

    $artifactExe = Join-Path $repoRoot "artifacts\FACM.exe"
    $package = Join-Path $repoRoot "FACM-Windows-x64.zip"
    if (-not (Test-Path -LiteralPath $artifactExe -PathType Leaf)) {
        throw "构建结束但没有找到：$artifactExe"
    }
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "构建结束但没有找到：$package"
    }

    Write-Stage "全部完成"
    Write-Host "EXE：$artifactExe" -ForegroundColor Green
    Write-Host "压缩包：$package" -ForegroundColor Green
    Write-Host "日志：$logPath"

    Start-Process explorer.exe -ArgumentList ('"' + (Join-Path $repoRoot 'artifacts') + '"')
}
catch {
    Write-Host ""
    Write-Host "执行失败：$($_.Exception.Message)" -ForegroundColor Red
    Write-Host "完整日志：$logPath" -ForegroundColor Yellow
    exit 1
}
finally {
    try { Stop-Transcript | Out-Null } catch { }
}
