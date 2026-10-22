@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-Single-Owner.ps1"
if errorlevel 1 echo Cloud setup incomplete. See error above.
pause
