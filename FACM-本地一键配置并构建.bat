@echo off
chcp 65001 >nul
setlocal

powershell.exe -NoLogo -NoProfile -Command "$p=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent()); if($p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){exit 0}else{exit 1}"
if not "%errorlevel%"=="0" (
    echo 正在申请管理员权限...
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
echo.
echo ============================================================
echo   FACM 本地环境配置与构建
echo ============================================================
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-local-build.ps1"
set "FACM_EXIT=%errorlevel%"

echo.
if "%FACM_EXIT%"=="0" (
    echo 已完成。产物位于 artifacts 文件夹和 FACM-Windows-x64.zip。
) else (
    echo 执行失败。请查看窗口中的错误和日志路径。
)
echo.
pause
exit /b %FACM_EXIT%
