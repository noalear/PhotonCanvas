@echo off
setlocal
rem Double-click to publish a new version of PhotonCanvas from this computer: it builds the latest
rem master and puts it up as a GitHub release, which PhotonCanvas updates itself from. It uses none
rem of GitHub's build minutes. It runs tools\publish-update.ps1 as it is on master, so it's always
rem the newest one whatever this folder has checked out, and a copy of this file works from anywhere
rem (the Desktop, say): away from the project it looks for it in %USERPROFILE%\YTool Studio.
set "PROJECT=%~dp0."
if not exist "%PROJECT%\.git" set "PROJECT=%USERPROFILE%\YTool Studio"
if not exist "%PROJECT%\.git" (
  echo Can't find the PhotonCanvas project folder: put this file in it.
  pause
  exit /b 1
)
echo Checking GitHub for the latest version...
git -C "%PROJECT%" fetch -q origin master
if errorlevel 1 (
  echo Couldn't get the latest master from GitHub: nothing was published.
  pause
  exit /b 1
)
set "SCRIPT=%TEMP%\photoncanvas-publish-update.ps1"
git -C "%PROJECT%" show origin/master:tools/publish-update.ps1 > "%SCRIPT%"
if errorlevel 1 (
  echo Couldn't read tools/publish-update.ps1 from master: nothing was published.
  pause
  exit /b 1
)
where pwsh >nul 2>nul
if errorlevel 1 (set "PCPS=powershell") else (set "PCPS=pwsh")
%PCPS% -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -ProjectDir "%PROJECT%" %*
echo.
pause
