# PowerShell Launcher for macOSify Win11
$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $PSScriptRoot

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "   macOSify Win11 - Direct Launcher (Bypasses SAC)" -ForegroundColor White
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Launching through Microsoft-trusted .NET runtime..." -ForegroundColor Green

dotnet run --project ".\src\MacOSify.Win11\MacOSify.Win11.csproj" -c Release --property:Platform=x64
