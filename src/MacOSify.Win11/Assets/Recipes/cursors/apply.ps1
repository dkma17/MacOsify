$ErrorActionPreference = 'Stop'
$targetDir = "$env:LOCALAPPDATA\Microsoft\Windows\Cursors\macOS"
if (!(Test-Path $targetDir)) { New-Item -ItemType Directory -Force -Path $targetDir | Out-Null }
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$packDir = Join-Path $scriptDir 'pack'
Get-ChildItem -Path $packDir -Filter '*.cur' | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $targetDir $_.Name) -Force
}
Copy-Item (Join-Path $packDir 'Install.inf') (Join-Path $targetDir 'Install.inf') -Force -ErrorAction SilentlyContinue

$cursorReg = 'HKCU:\Control Panel\Cursors'
Set-ItemProperty -Path $cursorReg -Name '(Default)' -Value 'macOS Cursors'
Set-ItemProperty -Path $cursorReg -Name 'Arrow' -Value "$targetDir\macOS_Arrow.cur"
Set-ItemProperty -Path $cursorReg -Name 'Help' -Value "$targetDir\macOS_Help.cur"
Set-ItemProperty -Path $cursorReg -Name 'AppStarting' -Value "$targetDir\macOS_Working.cur"
Set-ItemProperty -Path $cursorReg -Name 'Wait' -Value "$targetDir\macOS_Busy.cur"
Set-ItemProperty -Path $cursorReg -Name 'Crosshair' -Value "$targetDir\macOS_Precision.cur"
Set-ItemProperty -Path $cursorReg -Name 'IBeam' -Value "$targetDir\macOS_Text.cur"
Set-ItemProperty -Path $cursorReg -Name 'NWPen' -Value "$targetDir\macOS_Handwriting.cur"
Set-ItemProperty -Path $cursorReg -Name 'No' -Value "$targetDir\macOS_Unavailable.cur"
Set-ItemProperty -Path $cursorReg -Name 'SizeNS' -Value "$targetDir\macOS_ResizeNS.cur"
Set-ItemProperty -Path $cursorReg -Name 'SizeWE' -Value "$targetDir\macOS_ResizeWE.cur"
Set-ItemProperty -Path $cursorReg -Name 'SizeNWSE' -Value "$targetDir\macOS_ResizeNWSE.cur"
Set-ItemProperty -Path $cursorReg -Name 'SizeNESW' -Value "$targetDir\macOS_ResizeNESW.cur"
Set-ItemProperty -Path $cursorReg -Name 'SizeAll' -Value "$targetDir\macOS_Move.cur"
Set-ItemProperty -Path $cursorReg -Name 'UpArrow' -Value "$targetDir\macOS_Alternate.cur"
Set-ItemProperty -Path $cursorReg -Name 'Hand' -Value "$targetDir\macOS_Link.cur"
Set-ItemProperty -Path $cursorReg -Name 'Scheme Source' -Value 1 -Type DWord

$schemesReg = 'HKCU:\Control Panel\Cursors\Schemes'
if (!(Test-Path $schemesReg)) { New-Item -ItemType Directory -Force -Path $schemesReg | Out-Null }
$schemeVal = "$targetDir\macOS_Arrow.cur,$targetDir\macOS_Help.cur,$targetDir\macOS_Working.cur,$targetDir\macOS_Busy.cur,$targetDir\macOS_Precision.cur,$targetDir\macOS_Text.cur,$targetDir\macOS_Handwriting.cur,$targetDir\macOS_Unavailable.cur,$targetDir\macOS_ResizeNS.cur,$targetDir\macOS_ResizeWE.cur,$targetDir\macOS_ResizeNWSE.cur,$targetDir\macOS_ResizeNESW.cur,$targetDir\macOS_Move.cur,$targetDir\macOS_Alternate.cur,$targetDir\macOS_Link.cur"
Set-ItemProperty -Path $schemesReg -Name 'macOS Cursors' -Value $schemeVal

$source = @'
using System;
using System.Runtime.InteropServices;
public static class CursorsHelper {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
}
'@
if (-not ([System.Management.Automation.PSTypeName]'CursorsHelper').Type) {
    Add-Type -TypeDefinition $source
}
[CursorsHelper]::SystemParametersInfo(0x0057, 0, [IntPtr]::Zero, 0x03) | Out-Null
Write-Output 'Applied macOS cursor scheme.'
