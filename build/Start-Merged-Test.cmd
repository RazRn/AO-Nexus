@echo off
cd /d "%~dp0merged-test" || exit /b 1
if not exist "AONexus-Test.exe" (
    echo Den samlede testbuild mangler. Byg via merged-source\build\Build-LocalMerged.ps1.
    pause
    exit /b 1
)
start "" "AONexus-Test.exe"
