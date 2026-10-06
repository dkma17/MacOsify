$ErrorActionPreference = 'Stop'
$cursorReg = 'HKCU:\Control Panel\Cursors'
Set-ItemProperty -Path $cursorReg -Name '(Default)' -Value 'Windows Default'
Set-ItemProperty -Path $cursorReg -Name 'Arrow' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'Help' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'AppStarting' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'Wait' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'Crosshair' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'IBeam' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'NWPen' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'No' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'SizeNS' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'SizeWE' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'SizeNWSE' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'SizeNESW' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'SizeAll' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'UpArrow' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'Hand' -Value ''
Set-ItemProperty -Path $cursorReg -Name 'Scheme Source' -Value 0 -Type DWord

$schemesReg = 'HKCU:\Control Panel\Cursors\Schemes'
if (Test-Path $schemesReg) {
    Remove-ItemProperty -Path $schemesReg -Name 'macOS Cursors' -ErrorAction SilentlyContinue
}

$source = @'
using System;
using System.Runtime.InteropServices;
public static class CursorsRevertHelper {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
}
'@
if (-not ([System.Management.Automation.PSTypeName]'CursorsRevertHelper').Type) {
    Add-Type -TypeDefinition $source
}
[CursorsRevertHelper]::SystemParametersInfo(0x0057, 0, [IntPtr]::Zero, 0x03) | Out-Null

$targetDir = "$env:LOCALAPPDATA\Microsoft\Windows\Cursors\macOS"
if (Test-Path $targetDir) {
    Remove-Item -Path $targetDir -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'Restored Windows Default cursor scheme.'
