using MacOSify.Win11.Models;

namespace MacOSify.Win11.Modules;

public sealed class IconModule : ICustomizationModule
{
    public ModuleId Id => ModuleId.Icons;
    public string DisplayName => "Icons";

    public Task ApplyAsync(InstallContext context, CancellationToken cancellationToken) =>
        context.Recipes.ApplyAsync("icons", context.ProcessOutput, cancellationToken);
}
