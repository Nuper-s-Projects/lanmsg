@echo off
title LanMsg Setup
echo.
echo  LanMsg - LAN Messaging for Windows
echo  -----------------------------------
echo  This will ask for Administrator permission.
echo.
pause
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"\"%~dp0installer\install.ps1\"\"'"
if errorlevel 1 (
    echo.
    echo Setup failed or was cancelled.
    pause
    exit /b 1
)
echo.
echo Done! Look for the LanMsg icon in your system tray.
pause
