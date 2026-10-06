$ErrorActionPreference = 'Stop'
$shellIconsPaths = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons'
)
foreach ($regPath in $shellIconsPaths) {
    if (Test-Path $regPath) {
        Remove-ItemProperty -Path $regPath -Name '3' -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $regPath -Name '4' -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $regPath -Name '8' -ErrorAction SilentlyContinue
    }
}

$trashClsid = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\DefaultIcon'
if (Test-Path $trashClsid) {
    Set-ItemProperty -Path $trashClsid -Name '(Default)' -Value '%SystemRoot%\System32\imageres.dll,-54'
    Set-ItemProperty -Path $trashClsid -Name 'empty' -Value '%SystemRoot%\System32\imageres.dll,-54'
    Set-ItemProperty -Path $trashClsid -Name 'full' -Value '%SystemRoot%\System32\imageres.dll,-55'
}

$pcClsid = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\DefaultIcon'
if (Test-Path $pcClsid) {
    Set-ItemProperty -Path $pcClsid -Name '(Default)' -Value '%SystemRoot%\System32\imageres.dll,-109'
}

$source = @'
using System;
using System.Runtime.InteropServices;
public static class ShellIconRevertNotify {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
'@
if (-not ([System.Management.Automation.PSTypeName]'ShellIconRevertNotify').Type) {
    Add-Type -TypeDefinition $source
}
[ShellIconRevertNotify]::SHChangeNotify(0x08000000, 0x1000, [IntPtr]::Zero, [IntPtr]::Zero)

$targetDir = "$env:ProgramData\MacOSifyWin11\Icons"
if (Test-Path $targetDir) {
    Remove-Item -Path $targetDir -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output 'Restored Windows default system icons.'
