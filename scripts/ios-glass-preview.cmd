@echo off
setlocal
cd /d "%~dp0"
set "GGMAN_IOS_GLASS_PREVIEW=1"
start "" "%~dp0GGman-Glass-Preview.exe"
