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
    Write-Host "Downloading: $Url"
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Destination

    if (-not (Test-Path -LiteralPath $Destination -PathType Leaf)) {
        throw "Downloaded file was not found: $Destination"
    }
    if ((Get-Item -LiteralPath $Destination).Length -lt 100KB) {
        throw "Downloaded file is unexpectedly small: $Destination"
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

    Write-Host "Installing or repairing Visual Studio 2022 Build Tools."
    Write-Host "Do not close the installer window."
    $process = Start-Process -FilePath $bootstrapper -ArgumentList $arguments -Wait -PassThru

    if ($process.ExitCode -eq 3010) {
        $script:rebootRequired = $true
        Write-Warning "Installation completed and Windows requested a restart."
    }
    elseif ($process.ExitCode -ne 0) {
        throw "Visual Studio Build Tools installation failed. Exit code: $($process.ExitCode)"
    }
}

if ($env:OS -ne 'Windows_NT') {
    throw "This script can only run on Windows."
}
if (-not (Test-Administrator)) {
    throw "Administrator rights are required. Run the BAT file from the repository root."
}
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'FACM.sln') -PathType Leaf)) {
    throw "FACM.sln was not found. Keep this script inside the repository scripts directory."
}

New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
Start-Transcript -Path $logPath -Force | Out-Null

try {
    Write-Stage "Check local build environment"
    Write-Host "Repository: $repoRoot"
    Write-Host "Log file: $logPath"

    $msbuild = Find-MSBuild
    $net48Ready = Test-Net48TargetingPack
    $signTool = Find-SignTool

    Write-Host ("MSBuild: " + $(if ($msbuild) { $msbuild } else { "missing" }))
    Write-Host (".NET Framework 4.8 targeting pack: " + $(if ($net48Ready) { "installed" } else { "missing" }))
    Write-Host ("Windows SDK SignTool: " + $(if ($signTool) { $signTool } else { "missing" }))

    if (-not $msbuild -or -not $net48Ready -or -not $signTool) {
        Write-Stage "Install missing components"
        Install-BuildTools
    }
    else {
        Write-Host "Build environment is ready." -ForegroundColor Green
    }

    Write-Stage "Verify installed components"
    $msbuild = Find-MSBuild
    $net48Ready = Test-Net48TargetingPack
    $signTool = Find-SignTool

    if (-not $msbuild) { throw "MSBuild.exe is still missing after installation." }
    if (-not $net48Ready) { throw ".NET Framework 4.8 targeting pack is still missing." }
    if (-not $signTool) { throw "Windows SDK signtool.exe is still missing." }

    Write-Host "MSBuild: $msbuild" -ForegroundColor Green
    Write-Host ".NET Framework 4.8: ready" -ForegroundColor Green
    Write-Host "SignTool: $signTool" -ForegroundColor Green

    if ($rebootRequired) {
        Write-Warning "Restart Windows, then run the BAT file again."
    }

    if ($SetupOnly) {
        Write-Stage "Environment setup completed"
        exit 0
    }

    Write-Stage "Build FACM"
    $buildScript = Join-Path $PSScriptRoot "build-release.ps1"
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $buildScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "FACM build script failed. Exit code: $LASTEXITCODE"
    }

    $artifactExe = Join-Path $repoRoot "artifacts\FACM.exe"
    $package = Join-Path $repoRoot "FACM-Windows-x64.zip"
    if (-not (Test-Path -LiteralPath $artifactExe -PathType Leaf)) {
        throw "Build finished but FACM.exe was not found: $artifactExe"
    }
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Build finished but package was not found: $package"
    }

    Write-Stage "Completed"
    Write-Host "EXE: $artifactExe" -ForegroundColor Green
    Write-Host "Package: $package" -ForegroundColor Green
    Write-Host "Log: $logPath"

    Start-Process explorer.exe -ArgumentList ('"' + (Join-Path $repoRoot 'artifacts') + '"')
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Log: $logPath" -ForegroundColor Yellow
    exit 1
}
finally {
    try { Stop-Transcript | Out-Null } catch { }
}
