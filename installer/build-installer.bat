@echo off
echo === TGD Soundboard Installer Build ===
echo.

REM Run the PowerShell build script
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Build failed! See errors above.
    pause
    exit /b 1
)

echo.
pause
