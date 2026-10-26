@echo off
rem Double-click: finds the biggest job the machine will load (test jobs are laser-off and never started).
cd /d "%~dp0"
dotnet run -c Release -- %*
pause
