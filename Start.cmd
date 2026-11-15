@echo off
setlocal
powershell.exe -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0WLAN-FT.ps1"
if errorlevel 1 (
    echo.
    echo Could not start WLAN-FT. Check the message above.
    pause
)
