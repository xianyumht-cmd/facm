@echo off
setlocal
cd /d "%~dp0"
set "GGMAN_GLASS_PREVIEW=1"
start "" "%~dp0GGman.exe"
