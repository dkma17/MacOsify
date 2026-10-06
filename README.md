# macOSify Win11

`macOSify Win11` is an unpackaged WinUI 3 installer for applying a curated,
macOS-inspired Windows 11 desktop while preserving a durable path back to the
user's original settings.

## Implemented workflow

- Native WinUI 3 four-step setup experience with Mica, custom window chrome,
  responsive module cards, real progress, cancellation, and activity logging.
- Administrator elevation through the application manifest.
- Fail-closed creation and verification of a new `Pre-macOSify` System Restore
  point before any selected customization is applied.
- Atomic JSON operation journal in `%ProgramData%\MacOSifyWin11\State`.
- Exact registry value snapshots, including original type/data and whether each
  value existed.
- WinGet package ownership tracking: rollback only removes a package when
  macOSify installed it.
- Automatic reverse-order rollback after an error or cancellation, plus the
  one-click **Revert to Windows Default** workflow.
- HTTPS-only, size-limited, SHA-256-pinned download service and traversal-safe
  ZIP extraction for future catalog artifacts.
- Cancellable, timeout-bound process execution without interpolated shell
  commands.
- Explicit Restart Explorer and 30-second reboot actions.

## Modules

| Module | Automated behavior |
| --- | --- |
| Taskbar & Dock | Snapshots taskbar settings, enables auto-hide/centering, installs archived `WinStep.Nexus` 25.9 through WinGet, and deploys the pre-made macOS Sonoma theme profile into Nexus. |
| Top Bar | Installs pinned `Rainmeter.Rainmeter` through WinGet, automatically deploys and activates the bundled `macOS-TopBar` suite with live clock, menus, and system status indicators. |
| System Theme | Applies reversible Windows light-mode surfaces, Apple Aqua accent color palette (`#0088D1`), transparency, and applies the bundled 5K macOS Sonoma desktop wallpaper. |
| Icons | Applies a verified, checksum-pinned system icon pack for Folders, Open Folders, Drives, and Recycle Bin with immediate shell refresh and reversible rollback. |
| Cursors | Applies a verified, checksum-pinned macOS cursor scheme (15 pointers including crisp black arrows and animated beachball wait indicators) with live P/Invoke activation. |
| Window Effects | Enables reversible transparency, installs `MicaForEveryone.MicaForEveryone` through WinGet, and provisions rounded window corners and Acrylic/Mica backdrop rules. |

Pinned third-party packages:

| Package | Version | License | Source |
| --- | --- | --- | --- |
| Rainmeter | 4.5.26.3894 | GPL-2.0 | [Official release](https://github.com/rainmeter/rainmeter/releases/tag/v4.5.26.3894) |
| Mica For Everyone | 2.0.6.0 | MIT | [Official repository](https://github.com/MicaForEveryone/MicaForEveryone) |
| Nexus | 25.9 (archived WinGet version) | Proprietary; personal use only unless commercially licensed | [Official site](https://www.winstep.net/nexus.asp) |

## Requirements

- Windows 11 build 22000 or newer
- x64 or ARM64 Windows (third-party package availability can narrow this per
  module)
- .NET 8 SDK or newer for development
- Windows Package Manager (WinGet / App Installer)
- System Protection supported on the Windows system drive (automatically enabled if available; falls back cleanly to atomic journal rollback if disabled by policy)

## Installation & Packages

Ready-to-use distribution packages are generated in the `artifacts/installer` directory:

| Package | Format | Path / Filename | Description |
| --- | --- | --- | --- |
| **Windows Setup Installer** | `.exe` (Inno Setup) | [`artifacts/installer/macOSify-Win11-Setup-v1.0.0.exe`](artifacts/installer/macOSify-Win11-Setup-v1.0.0.exe) | Single-file setup installer with automatic UAC elevation, Start Menu & Desktop shortcuts, and full uninstaller registration. |
| **Portable Package** | `.zip` | [`artifacts/installer/macOSify-Win11-v1.0.0-Portable-x64.zip`](artifacts/installer/macOSify-Win11-v1.0.0-Portable-x64.zip) | Standalone portable archive. Unpack and launch `MacOSify.Win11.exe` directly without setup. |

### Cryptographic Checksums (SHA-256)
- **`macOSify-Win11-Setup-v1.0.0.exe`**: `6DAF566338D381E32E26D13927BC3551C0517FE0DB99B1718340813BC0CD7A6F`
- **`macOSify-Win11-v1.0.0-Portable-x64.zip`**: `42F221FE9ED853FA9650A8548D9478E49795E023568C45BD5957EF89014B9ACF`

## Build and run from source

Open a new terminal after installing the .NET SDK:

```powershell
dotnet restore .\MacOSify.Win11.sln
dotnet build .\MacOSify.Win11.sln -c Debug -p:Platform=x64
dotnet run --project .\src\MacOSify.Win11\MacOSify.Win11.csproj -c Debug --property:Platform=x64
```

The executable requests UAC elevation at launch. For Visual Studio, open
`MacOSify.Win11.sln`, choose `x64`, and press **F5**. Visual Studio itself may
need to be restarted as Administrator when debugging an application whose
manifest requires elevation.

## Supplying licensed icon or cursor assets

See [`src/MacOSify.Win11/Assets/Recipes/README.md`](src/MacOSify.Win11/Assets/Recipes/README.md).
The UI keeps those switches disabled until a local recipe manifest is present.
Preflight then verifies both scripts and every declared payload against their
full SHA-256 values before creating the journal, restore point, or system changes.

## Safety and testing

This application intentionally refuses to continue if elevation, WinGet,
pinned-package availability, recipe verification, disk space, or creation of a new
restore point fails. It never disables Defender, SmartScreen, UAC, or PowerShell
execution policy globally.

Theme patchers and shell customizers can still be destabilizing. Test system
modifications in a disposable Windows 11 VM snapshot before using a development
build on a primary PC. Sign release binaries before distribution so the UAC
prompt can identify the publisher.
