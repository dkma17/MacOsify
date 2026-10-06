$ErrorActionPreference = 'Stop'
$targetDir = "$env:ProgramData\MacOSifyWin11\Icons"
if (!(Test-Path $targetDir)) { New-Item -ItemType Directory -Force -Path $targetDir | Out-Null }
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$packDir = Join-Path $scriptDir 'pack'
Get-ChildItem -Path $packDir -Filter '*.ico' | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $targetDir $_.Name) -Force
}

$shellIconsPaths = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons'
)
foreach ($regPath in $shellIconsPaths) {
    if (!(Test-Path $regPath)) { New-Item -ItemType Directory -Force -Path $regPath | Out-Null }
    Set-ItemProperty -Path $regPath -Name '3' -Value "$targetDir\Folder.ico"
    Set-ItemProperty -Path $regPath -Name '4' -Value "$targetDir\FolderOpen.ico"
    Set-ItemProperty -Path $regPath -Name '8' -Value "$targetDir\Drive.ico"
}

$trashClsid = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\DefaultIcon'
if (!(Test-Path $trashClsid)) { New-Item -ItemType Directory -Force -Path $trashClsid | Out-Null }
Set-ItemProperty -Path $trashClsid -Name '(Default)' -Value "$targetDir\TrashEmpty.ico"
Set-ItemProperty -Path $trashClsid -Name 'empty' -Value "$targetDir\TrashEmpty.ico"
Set-ItemProperty -Path $trashClsid -Name 'full' -Value "$targetDir\TrashFull.ico"

$pcClsid = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\DefaultIcon'
if (!(Test-Path $pcClsid)) { New-Item -ItemType Directory -Force -Path $pcClsid | Out-Null }
Set-ItemProperty -Path $pcClsid -Name '(Default)' -Value "$targetDir\Computer.ico"

$source = @'
using System;
using System.Runtime.InteropServices;
public static class ShellIconNotify {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
'@
if (-not ([System.Management.Automation.PSTypeName]'ShellIconNotify').Type) {
    Add-Type -TypeDefinition $source
}
[ShellIconNotify]::SHChangeNotify(0x08000000, 0x1000, [IntPtr]::Zero, [IntPtr]::Zero)
Write-Output 'Applied macOS system icons.'
