# PowerShell Launcher for macOSify Win11
$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $PSScriptRoot

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "   macOSify Win11 - Direct Launcher" -ForegroundColor White
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

$exePath = Join-Path $PSScriptRoot "artifacts\publish\win-x64\MacOSify.Win11.exe"

# Unblock files to bypass Smart App Control / Zone Identifier restrictions
Get-ChildItem -Path (Join-Path $PSScriptRoot "artifacts\publish\win-x64") -Recurse -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

Write-Host "Launching macOSify Win11 with Administrator privileges..." -ForegroundColor Green
Write-Host "(Please click 'Yes' on the Windows UAC prompt if asked)" -ForegroundColor Gray

Start-Process -FilePath $exePath -Verb RunAs

