using MacOSify.Win11.Models;
using MacOSify.Win11.Services;

namespace MacOSify.Win11.Modules;

public interface ICustomizationModule
{
    ModuleId Id { get; }
    string DisplayName { get; }
    Task ApplyAsync(InstallContext context, CancellationToken cancellationToken);
}

public sealed class InstallContext(
    RegistryService registry,
    WingetService winget,
    VerifiedRecipeService recipes,
    Action<string, InstallLogLevel> log)
{
    public RegistryService Registry { get; } = registry;
    public WingetService Winget { get; } = winget;
    public VerifiedRecipeService Recipes { get; } = recipes;

    public void Log(string message, InstallLogLevel level = InstallLogLevel.Info) => log(message, level);

    public Action<string> ProcessOutput => message => log(message, InstallLogLevel.Info);
}
