@echo off
setlocal

powershell.exe -NoLogo -NoProfile -Command "$p=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent()); if($p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){exit 0}else{exit 1}"
if not "%errorlevel%"=="0" (
    echo Requesting administrator rights...
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
echo.
echo ============================================================
echo   FACM LOCAL SETUP AND BUILD
echo ============================================================
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-local-build.ps1"
set "FACM_EXIT=%errorlevel%"

echo.
if "%FACM_EXIT%"=="0" (
    echo Completed. Check the artifacts directory and FACM-Windows-x64.zip.
) else (
    echo Failed. Review the error and log path above.
)
echo.
pause
exit /b %FACM_EXIT%
