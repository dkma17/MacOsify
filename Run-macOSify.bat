@echo off
title Launching macOSify Win11...
cd /d "%~dp0"
echo ===================================================
echo   macOSify Win11 - Direct Launcher (Bypasses SAC)
echo ===================================================
echo.
echo Launching through trusted .NET runtime...
echo.
dotnet run --project ".\src\MacOSify.Win11\MacOSify.Win11.csproj" -c Release --property:Platform=x64
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Fallback: Trying published executable...
    start "" ".\artifacts\publish\win-x64\MacOSify.Win11.exe"
)
pause
