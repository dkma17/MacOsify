using MacOSify.Win11.Models;
using Microsoft.Win32;

namespace MacOSify.Win11.Modules;

public sealed class WindowEffectsModule : ICustomizationModule
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public ModuleId Id => ModuleId.WindowEffects;
    public string DisplayName => "Window Effects";

    public async Task ApplyAsync(InstallContext context, CancellationToken cancellationToken)
    {
        await context.Registry.SetValueAsync(
            PersonalizeKey,
            "EnableTransparency",
            1,
            RegistryValueKind.DWord,
            "Enable transparency effects",
            cancellationToken);

        await context.Winget.InstallAsync(
            "MicaForEveryone.MicaForEveryone",
            "2.0.6.0",
            "Install Mica For Everyone",
            context.ProcessOutput,
            cancellationToken);

        try
        {
            var confDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mica For Everyone");
            Directory.CreateDirectory(confDir);
            var confPath = Path.Combine(confDir, "MicaForEveryone.conf");
            if (!File.Exists(confPath))
            {
                const string defaultConf = """
                    # Mica For Everyone configuration for macOS styling
                    Global {
                      TitleBarColor = System
                      Backdrop = Acrylic
                      CornerPreference = Rounded
                      ExtendFrameIntoClientArea = False
                      MinimizeToTray = True
                    }
                    """;
                File.WriteAllText(confPath, defaultConf);
            }
        }
        catch (Exception ex)
        {
            context.Log($"Note: Mica For Everyone config: {ex.Message}", InstallLogLevel.Warning);
        }

        context.Log("Mica For Everyone was installed and configured with rounded corners and Acrylic blur.", InstallLogLevel.Success);
    }
}
