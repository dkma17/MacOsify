using MacOSify.Win11.Models;
using System.Diagnostics;

namespace MacOSify.Win11.Modules;

public sealed class TopBarModule : ICustomizationModule
{
    public ModuleId Id => ModuleId.TopBar;
    public string DisplayName => "Top Menu Bar";

    public async Task ApplyAsync(InstallContext context, CancellationToken cancellationToken)
    {
        context.Log("Installing Rainmeter desktop suite host...");
        await context.Winget.InstallAsync(
            "Rainmeter.Rainmeter",
            "4.5.26.3894",
            "Install Rainmeter desktop host",
            context.ProcessOutput,
            cancellationToken);

        try
        {
            var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var skinsDir = Path.Combine(myDocs, "Rainmeter", "Skins", "macOS-TopBar");
            var sourceSkin = Path.Combine(AppContext.BaseDirectory, "Assets", "TopBar", "macOS-TopBar");

            if (Directory.Exists(sourceSkin))
            {
                context.Log("Deploying macOS Top Menu Bar skin to Rainmeter...");
                CopyDirectory(sourceSkin, skinsDir);

                // Configure Rainmeter.ini to load macOS-TopBar and hide default illustro widgets
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var rainmeterIniPath = Path.Combine(appData, "Rainmeter", "Rainmeter.ini");
                ConfigureRainmeterIni(rainmeterIniPath);

                // If Rainmeter is installed, activate skin
                var rainmeterExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rainmeter", "Rainmeter.exe");
                if (File.Exists(rainmeterExe))
                {
                    context.Log("Activating macOS Top Menu Bar in Rainmeter...");
                    try
                    {
                        using var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = rainmeterExe,
                            Arguments = "!ActivateConfig \"macOS-TopBar\" \"macOS-TopBar.ini\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                    }
                    catch
                    {
                        // Background launch attempt
                    }
                }
            }

            context.Log("macOS Top Menu Bar skin deployed successfully.", InstallLogLevel.Success);
        }
        catch (Exception ex)
        {
            context.Log($"Top Bar configuration notice: {ex.Message}", InstallLogLevel.Warning);
        }
    }

    private static void ConfigureRainmeterIni(string iniPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
            var lines = File.Exists(iniPath) ? File.ReadAllLines(iniPath).ToList() : new List<string>();

            // Ensure [macOS-TopBar] section exists and is active
            var topBarIndex = lines.FindIndex(line => line.Trim().Equals("[macOS-TopBar]", StringComparison.OrdinalIgnoreCase));
            if (topBarIndex >= 0)
            {
                // update active
                var activeIndex = lines.FindIndex(topBarIndex, l => l.Trim().StartsWith("Active=", StringComparison.OrdinalIgnoreCase));
                if (activeIndex >= 0)
                {
                    lines[activeIndex] = "Active=1";
                }
                else
                {
                    lines.Insert(topBarIndex + 1, "Active=1");
                }
            }
            else
            {
                lines.Add("[macOS-TopBar]");
                lines.Add("Active=1");
                lines.Add("WindowX=0");
                lines.Add("WindowY=0");
                lines.Add("LoadOrder=0");
                lines.Add("AlwaysOnTop=2");
            }

            // Disable default illustro widgets if present
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim().StartsWith("[illustro", StringComparison.OrdinalIgnoreCase))
                {
                    int j = i + 1;
                    while (j < lines.Count && !lines[j].StartsWith("["))
                    {
                        if (lines[j].Trim().StartsWith("Active=", StringComparison.OrdinalIgnoreCase))
                        {
                            lines[j] = "Active=0";
                        }
                        j++;
                    }
                }
            }

            File.WriteAllLines(iniPath, lines);
        }
        catch
        {
            // Best effort configuration
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
