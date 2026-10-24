@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Compact-Browser.ps1"
if errorlevel 1 (
  echo.
  echo Unable to start the compact preview. See the error above.
  pause
)
