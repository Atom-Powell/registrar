@echo off
cd /d "%~dp0"
registrar.exe menu
if errorlevel 1 pause
