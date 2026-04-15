@echo off
set "ROOT=%~dp0"

if exist "%ROOT%PakRatModern.exe" (
    start "" "%ROOT%PakRatModern.exe" %*
    exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%pakrat_modern_gui.ps1" %*
