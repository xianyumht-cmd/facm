@echo off
setlocal
cd /d "%~dp0"
if not exist "D1-Endpoint.txt" (
 echo Missing D1-Endpoint.txt. Configure the HTTPS Worker URL first.
 pause
 exit /b 2
)
for /f "usebackq delims=" %%U in ("D1-Endpoint.txt") do if not defined GGMAN_D1_ENDPOINT set "GGMAN_D1_ENDPOINT=%%U"
if not defined GGMAN_D1_ENDPOINT (
 echo D1 endpoint is empty.
 pause
 exit /b 2
)
start "" "%~dp0GGman.exe"
