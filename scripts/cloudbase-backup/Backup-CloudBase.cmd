@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Backup-CloudBase.ps1"
if errorlevel 1 echo Backup did not complete. Check the message above.
pause
