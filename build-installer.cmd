@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1"
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Installer build failed.
    pause
    exit /b %ERRORLEVEL%
)
endlocal
