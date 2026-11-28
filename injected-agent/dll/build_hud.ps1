<#
ggman_hud.dll 构建脚本（Zig 工具链，x64，无需 VS）

把 Zig 当 C 编译器用：zig cc -target x86_64-windows-gnu
自带 mingw-w64 头文件与 kernel32/user32/gdi32 导入库，产出标准 PE32+ DLL。

用法：
  powershell -ExecutionPolicy Bypass -File dll\build_hud.ps1
  powershell -ExecutionPolicy Bypass -File dll\build_hud.ps1 -Out ggman_hud_v5.dll
  powershell -ExecutionPolicy Bypass -File dll\build_hud.ps1 -Clean
  powershell -ExecutionPolicy Bypass -File dll\build_hud.ps1 -Run
#>
param(
    [string]$Out = 'ggman_hud.dll',
    [switch]$Clean,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$zig = Join-Path $root 'toolchain\zig-x86_64-windows-0.17.1\zig.exe'
$src = Join-Path $PSScriptRoot 'ggman_hud.c'
$build = Join-Path $PSScriptRoot 'build'
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $root 'toolchain\zigcache'
$env:ZIG_LOCAL_CACHE_DIR = Join-Path $root 'toolchain\zigcache-local'

if (-not (Test-Path $zig)) { throw "zig not found at $zig" }
New-Item -ItemType Directory -Force -Path $build | Out-Null
if ($Clean) { Remove-Item (Join-Path $build '*.dll') -Force -ErrorAction SilentlyContinue }

$outPath = Join-Path $build $Out
& $zig cc -target x86_64-windows-gnu -shared -O2 -Wall -o $outPath $src -luser32 -lgdi32
if ($LASTEXITCODE -ne 0) { throw "zig cc failed ($LASTEXITCODE)" }
$item = Get-Item $outPath
"built $($item.FullName)  ($([math]::Round($item.Length/1KB,1)) KB)"

if ($Run) {
    $py = 'C:\Users\Administrator\AppData\Local\Programs\Python\Python314\python.exe'
    $renderer = (Get-CimInstance Win32_Process -Filter "Name='LeagueClientUxRender.exe'" |
        Where-Object { $_.CommandLine -match '--type=renderer' } | Select-Object -First 1).ProcessId
    if (-not $renderer) { throw 'no LeagueClientUxRender.exe renderer running' }
    # 加载过的模块不能重载，所以每次用唯一文件名
    $unique = Join-Path $build ("ggman_hud_{0}_{1}.dll" -f $renderer, (Get-Date -Format 'HHmmss'))
    Copy-Item $outPath $unique -Force
    & $py (Join-Path $root 'tools\lol_inject_poc.py') --pid $renderer --dll $unique
}
