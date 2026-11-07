@echo off
rem Opens the rotary motor test window. Nothing moves until you press a button in it.
cd /d "%~dp0..\src"
start "" pythonw -m pcprobe.motor_test %*
