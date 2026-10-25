@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0GGman.exe" (
  echo GGman.exe not found. Extract the entire preview ZIP first.
  pause
  exit /b 1
)
set "GGMAN_GLASS_PREVIEW=1"
start "" "%~dp0GGman.exe" --glass-preview --compact
