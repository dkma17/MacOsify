using MacOSify.Win11.Models;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace MacOSify.Win11.Modules;

public sealed class ThemeModule : ICustomizationModule
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM";
    private const string DesktopKey = @"HKEY_CURRENT_USER\Control Panel\Desktop";

    public ModuleId Id => ModuleId.Theme;
    public string DisplayName => "System Theme";

    public async Task ApplyAsync(InstallContext context, CancellationToken cancellationToken)
    {
        // 1. Light theme and transparency settings
        await context.Registry.SetValueAsync(
            PersonalizeKey, "AppsUseLightTheme", 1, RegistryValueKind.DWord,
            "Use light app surfaces", cancellationToken);
        await context.Registry.SetValueAsync(
            PersonalizeKey, "SystemUsesLightTheme", 1, RegistryValueKind.DWord,
            "Use light system surfaces", cancellationToken);
        await context.Registry.SetValueAsync(
            PersonalizeKey, "EnableTransparency", 1, RegistryValueKind.DWord,
            "Enable Windows transparency", cancellationToken);
        await context.Registry.SetValueAsync(
            DwmKey, "ColorPrevalence", 0, RegistryValueKind.DWord,
            "Use neutral window chrome", cancellationToken);

        // 2. macOS Aqua accent color
        await context.Registry.SetValueAsync(
            DwmKey, "AccentColor", unchecked((int)0xFFD18800), RegistryValueKind.DWord,
            "Set macOS Aqua accent color", cancellationToken);

        // 3. Set official macOS Sonoma/Sequoia style wallpaper
        var sourceWallpaper = Path.Combine(AppContext.BaseDirectory, "Assets", "Wallpaper", "macOS_Sonoma.jpg");
        if (File.Exists(sourceWallpaper))
        {
            try
            {
                var targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MacOSifyWin11", "Wallpaper");
                Directory.CreateDirectory(targetDir);
                var targetWallpaper = Path.Combine(targetDir, "macOS_Sonoma.jpg");
                File.Copy(sourceWallpaper, targetWallpaper, overwrite: true);

                // Record wallpaper path in registry
                await context.Registry.SetValueAsync(
                    DesktopKey, "Wallpaper", targetWallpaper, RegistryValueKind.String,
                    "Set macOS Sonoma wallpaper", cancellationToken);

                // Set wallpaper style to Fill (10) and Tile to 0
                await context.Registry.SetValueAsync(
                    DesktopKey, "WallpaperStyle", "10", RegistryValueKind.String,
                    "Set wallpaper style to Fill", cancellationToken);
                await context.Registry.SetValueAsync(
                    DesktopKey, "TileWallpaper", "0", RegistryValueKind.String,
                    "Disable tile wallpaper", cancellationToken);

                // Live wallpaper refresh via Win32 API
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, targetWallpaper, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                context.Log("Applied macOS Sonoma desktop wallpaper.");
            }
            catch (Exception ex)
            {
                context.Log($"Note: Wallpaper setting notice: {ex.Message}", InstallLogLevel.Warning);
            }
        }

        context.Log("Applied macOS appearance, Aqua accent, and wallpaper settings.", InstallLogLevel.Success);
    }

    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uAction, uint uParam, string lpvParam, uint fuWinIni);
}
