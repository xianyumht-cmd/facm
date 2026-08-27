@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 65001 >nul

set "ROOT=%~dp0"
set "SCRIPT=%ROOT%scripts\collect-facm4-real-machine-evidence.ps1"
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
set "CANDIDATE=%~1"
set "STAGE=%~2"

if not exist "%PS%" (
  echo [FACM 4.0] 未找到 Windows PowerShell 5.1: %PS%
  exit /b 2
)

if not exist "%SCRIPT%" (
  echo [FACM 4.0] 未找到采集脚本: %SCRIPT%
  exit /b 3
)

if "%STAGE%"=="" set "STAGE=General"

echo.
echo FACM 4.0 真机 Release Evidence 采集器
echo ======================================
echo 只读采集：不会申请管理员权限，不会更新/重启/删除，不会修改生产配置。
echo 自动采集结果不等于 Release PASS；需要真实交互的项目会保留 manual_required。
echo.
if not "%CANDIDATE%"=="" echo Candidate: %~nx1
echo Stage: %STAGE%
echo.

if "%CANDIDATE%"=="" (
  "%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -Stage "%STAGE%"
) else (
  "%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -CandidatePath "%CANDIDATE%" -Stage "%STAGE%"
)

set "RC=%ERRORLEVEL%"
echo.
if "%RC%"=="0" (
  echo [FACM 4.0] 采集完成。请保留生成的 Evidence ZIP，后续审核后才能更新 release evidence 状态。
) else (
  echo [FACM 4.0] 采集失败，退出码 %RC%。
)
exit /b %RC%
