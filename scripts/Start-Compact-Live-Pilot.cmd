@echo off
setlocal
cd /d "%~dp0"
if not exist "GGman.exe" (
  echo Preview files are incomplete. Extract the ZIP first.
  pause
  exit /b 1
)
echo The live pilot uses the normal GGman account and game services.
echo Close the official GGman first. This pilot does not publish an update.
set "GGMAN_COMPACT_LIVE_PILOT=1"
start "" "%~dp0GGman.exe" --compact-live-pilot
