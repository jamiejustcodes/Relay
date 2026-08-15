@echo off
title Relay — AI Desktop Assistant
cls
echo ========================================================
echo   Relay - AI Desktop Assistant for Windows
echo ========================================================
echo   Status: Launching Relay Dashboard...
echo   Global Hotkey: Ctrl + Space
echo ========================================================
echo.
dotnet run --project src/ScreenLens.UI/ScreenLens.UI.csproj
pause
