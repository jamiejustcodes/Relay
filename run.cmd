@echo off
title Relay — AI Desktop Assistant
cls
echo ========================================================
echo   Relay - AI Desktop Assistant for Windows
echo ========================================================
echo   Quick Capture: Ctrl + Space
echo   Ask with Prompt: Ctrl + Shift + Space
echo ========================================================
echo.
dotnet run --project src/Relay.UI/Relay.UI.csproj
pause
