using MacOSify.Win11.Models;

namespace MacOSify.Win11.Modules;

public sealed class CursorModule : ICustomizationModule
{
    public ModuleId Id => ModuleId.Cursors;
    public string DisplayName => "Cursors";

    public Task ApplyAsync(InstallContext context, CancellationToken cancellationToken) =>
        context.Recipes.ApplyAsync("cursors", context.ProcessOutput, cancellationToken);
}
