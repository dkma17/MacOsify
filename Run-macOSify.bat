@echo off
title macOSify Win11 Launcher
cd /d "%~dp0"

echo ===================================================
echo   macOSify Win11 - Direct Launcher
echo ===================================================
echo.

:: Ensure files are unblocked for Windows Defender / SAC
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%~dp0artifacts\publish\win-x64' -Recurse | Unblock-File -ErrorAction SilentlyContinue"

echo Launching macOSify Win11 with Administrator privileges...
echo (Please click 'Yes' on the Windows UAC elevation prompt if asked)
echo.

:: Launch the self-contained executable with elevation prompt
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~dp0artifacts\publish\win-x64\MacOSify.Win11.exe' -Verb RunAs"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Trying direct execution...
    start "" "%~dp0artifacts\publish\win-x64\MacOSify.Win11.exe"
)

