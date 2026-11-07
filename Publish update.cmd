@echo off
rem Double-click to publish a new version of PhotonCanvas from this computer: it builds the latest
rem master and puts it up as a GitHub release, which PhotonCanvas updates itself from. It uses none of
rem GitHub's build minutes. What it does: tools\publish-update.ps1.
where pwsh >nul 2>nul
if %errorlevel%==0 (set "PCPS=pwsh") else (set "PCPS=powershell")
%PCPS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish-update.ps1" %*
echo.
pause
