@echo off
cd /d "%~dp0"
where pwsh >nul 2>nul
if errorlevel 1 (
  echo PowerShell 7.4 or newer (7.6 LTS recommended) is required. Install it through your usual approved software channel.
  pause
  exit /b 1
)
pwsh -NoLogo -NoProfile -File "%~dp0Start-AMAG23.ps1" %*
if errorlevel 1 pause
