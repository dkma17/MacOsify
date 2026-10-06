using MacOSify.Win11.Models;
using Microsoft.Win32;

namespace MacOSify.Win11.Modules;

public sealed class DockModule : ICustomizationModule
{
    private const string TaskbarSettingsKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3";
    private const string TaskbarAdvancedKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public ModuleId Id => ModuleId.Dock;
    public string DisplayName => "Taskbar & Dock";

    public async Task ApplyAsync(InstallContext context, CancellationToken cancellationToken)
    {
        context.Log("Saving the complete taskbar state before enabling auto-hide.");
        var settings = context.Registry.ReadBinaryValue(TaskbarSettingsKey, "Settings");
        if (settings.Length <= 8 || settings[8] is not 2 and not 3)
        {
            throw new InvalidDataException("This Windows build uses an unknown taskbar-settings format; the Dock module was not applied.");
        }

        settings[8] = 3;
        await context.Registry.SetValueAsync(
            TaskbarSettingsKey,
            "Settings",
            settings,
            RegistryValueKind.Binary,
            "Enable Windows taskbar auto-hide",
            cancellationToken);

        await context.Registry.SetValueAsync(
            TaskbarAdvancedKey,
            "TaskbarAl",
            1,
            RegistryValueKind.DWord,
            "Center taskbar alignment",
            cancellationToken);

        context.Log("Installing archived Nexus 25.9 through Windows Package Manager.");
        await context.Winget.InstallAsync(
            "WinStep.Nexus",
            "25.9",
            "Install Winstep Nexus Dock",
            context.ProcessOutput,
            cancellationToken);

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var publicDocs = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
            var nexusUserTheme = Path.Combine(appData, "WinStep", "NeXuS", "Themes", "macOS");
            var nexusPublicTheme = Path.Combine(publicDocs, "WinStep", "NeXuS", "Themes", "macOS");
            var sourceTheme = Path.Combine(AppContext.BaseDirectory, "Assets", "Dock", "macOS_Theme");

            if (Directory.Exists(sourceTheme))
            {
                context.Log("Deploying pre-made macOS Dock profile and theme to Nexus.");
                CopyDirectory(sourceTheme, nexusUserTheme);
                CopyDirectory(sourceTheme, nexusPublicTheme);
            }
        }
        catch (Exception ex)
        {
            context.Log($"Note: Nexus profile deployment: {ex.Message}", InstallLogLevel.Warning);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }
}
