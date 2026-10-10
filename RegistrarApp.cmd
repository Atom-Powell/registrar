@echo off
rem Self-locates the same way Registrar.cmd does, so the app resolves config, schemas
rem and assets by walking up from runtime\ exactly as the CLI does. "start" is the one
rem difference: this launches a window rather than a console session, so the launcher
rem hands off and closes instead of sitting behind it for the whole session.
cd /d "%~dp0"
start "" Registrar.App.exe
