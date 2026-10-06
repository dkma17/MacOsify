using MacOSify.Win11.Models;
using System.Text.Json;

namespace MacOSify.Win11.Services;

public sealed class VerifiedRecipeService(ProcessRunner processRunner, OperationJournalService journal)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task ValidateAsync(string recipeId, CancellationToken cancellationToken)
    {
        var (recipe, recipeFolder) = await LoadAsync(recipeId, cancellationToken);
        var applyPath = ResolveRecipeFile(recipeFolder, recipe.ApplyScript);
        var revertPath = ResolveRecipeFile(recipeFolder, recipe.RevertScript);
        await SecureDownloadService.VerifyFileAsync(applyPath, recipe.ApplySha256, cancellationToken);
        await SecureDownloadService.VerifyFileAsync(revertPath, recipe.RevertSha256, cancellationToken);
        await ValidateAssetsAsync(recipe, recipeFolder, cancellationToken);
    }

    public async Task ApplyAsync(
        string recipeId,
        Action<string> onOutput,
        CancellationToken cancellationToken)
    {
        var (recipe, recipeFolder) = await LoadAsync(recipeId, cancellationToken);
        var applyPath = ResolveRecipeFile(recipeFolder, recipe.ApplyScript);
        var revertPath = ResolveRecipeFile(recipeFolder, recipe.RevertScript);
        await SecureDownloadService.VerifyFileAsync(applyPath, recipe.ApplySha256, cancellationToken);
        await SecureDownloadService.VerifyFileAsync(revertPath, recipe.RevertSha256, cancellationToken);
        await ValidateAssetsAsync(recipe, recipeFolder, cancellationToken);

        var transaction = journal.Current
            ?? throw new InvalidOperationException("A transaction journal must be active before a verified recipe can run.");
        var recoveryFolder = Path.Combine(AppPaths.Recovery, transaction.TransactionId.ToString("N"), recipe.Id);
        Directory.CreateDirectory(recoveryFolder);
        var recoveryRevertPath = ResolveRecoveryDestination(recoveryFolder, recipe.RevertScript);
        Directory.CreateDirectory(Path.GetDirectoryName(recoveryRevertPath)!);
        File.Copy(revertPath, recoveryRevertPath, overwrite: false);
        await SecureDownloadService.VerifyFileAsync(recoveryRevertPath, recipe.RevertSha256, cancellationToken);

        var recoveryAssets = new List<VerifiedRecipeAsset>();
        foreach (var asset in recipe.Assets)
        {
            var sourcePath = ResolveRecipeFile(recipeFolder, asset.Path);
            var recoveryPath = ResolveRecoveryDestination(recoveryFolder, asset.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(recoveryPath)!);
            File.Copy(sourcePath, recoveryPath, overwrite: false);
            await SecureDownloadService.VerifyFileAsync(recoveryPath, asset.Sha256, cancellationToken);
            recoveryAssets.Add(new VerifiedRecipeAsset { Path = recoveryPath, Sha256 = asset.Sha256 });
        }

        var entry = new JournalEntry
        {
            Kind = JournalOperationKind.VerifiedRecipe,
            Description = $"Apply verified {recipe.DisplayName} recipe",
            Target = recipe.Id,
            ApplyPath = applyPath,
            ApplySha256 = recipe.ApplySha256,
            RevertPath = recoveryRevertPath,
            RevertSha256 = recipe.RevertSha256,
            RecoveryAssets = recoveryAssets
        };
        await journal.AddEntryAsync(entry, cancellationToken);

        var result = await RunPowerShellFileAsync(applyPath, onOutput, CancellationToken.None);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"The verified {recipe.DisplayName} recipe failed (exit code 0x{result.ExitCode:X8}). {result.StandardError.Trim()}");
        }

        await journal.MarkEntryAsync(entry.Id, JournalEntryStatus.Applied, CancellationToken.None);
    }

    public async Task RevertAsync(JournalEntry entry, Action<string> onOutput, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.RevertPath) || string.IsNullOrWhiteSpace(entry.RevertSha256))
        {
            throw new InvalidDataException($"Recipe journal entry {entry.Id} has no verified revert script.");
        }

        var fullPath = Path.GetFullPath(entry.RevertPath);
        var recoveryRoot = Path.GetFullPath(AppPaths.Recovery) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(recoveryRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The recorded recipe path is outside the transaction recovery directory.");
        }

        await SecureDownloadService.VerifyFileAsync(fullPath, entry.RevertSha256, cancellationToken);
        foreach (var asset in entry.RecoveryAssets)
        {
            var assetPath = Path.GetFullPath(asset.Path);
            if (!assetPath.StartsWith(recoveryRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A recorded recipe asset is outside the transaction recovery directory.");
            }
            await SecureDownloadService.VerifyFileAsync(assetPath, asset.Sha256, cancellationToken);
        }
        var result = await RunPowerShellFileAsync(fullPath, onOutput, CancellationToken.None);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"The revert recipe for {entry.Target} failed (exit code 0x{result.ExitCode:X8}).");
        }
    }

    private async Task<(VerifiedRecipe Recipe, string Folder)> LoadAsync(string recipeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipeId) || recipeId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Recipe IDs may contain only ASCII letters, digits, and hyphens.", nameof(recipeId));
        }

        var recipeFolder = Path.Combine(AppPaths.Recipes, recipeId);
        var manifestPath = Path.Combine(recipeFolder, "recipe.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                $"The {recipeId} module needs a licensed local asset recipe. Add a checksum-pinned recipe under Assets\\Recipes\\{recipeId} before selecting it.",
                manifestPath);
        }

        await using var stream = File.OpenRead(manifestPath);
        var recipe = await JsonSerializer.DeserializeAsync<VerifiedRecipe>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Recipe manifest {manifestPath} is empty.");

        if (!recipe.Id.Equals(recipeId, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(recipe.DisplayName))
        {
            throw new InvalidDataException($"Recipe manifest {manifestPath} has an invalid ID or display name.");
        }

        return (recipe, recipeFolder);
    }

    private static string ResolveRecipeFile(string recipeFolder, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Recipe script paths must be non-empty relative paths.");
        }

        var root = Path.GetFullPath(recipeFolder) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(recipeFolder, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            throw new InvalidDataException("A recipe script is missing or resolves outside its recipe directory.");
        }

        return fullPath;
    }

    private static string ResolveRecoveryDestination(string recoveryFolder, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Recovery artifact paths must be non-empty relative paths.");
        }

        var root = Path.GetFullPath(recoveryFolder) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(recoveryFolder, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A recovery artifact resolves outside the transaction recovery directory.");
        }

        return fullPath;
    }

    private static async Task ValidateAssetsAsync(
        VerifiedRecipe recipe,
        string recipeFolder,
        CancellationToken cancellationToken)
    {
        foreach (var asset in recipe.Assets)
        {
            var path = ResolveRecipeFile(recipeFolder, asset.Path);
            await SecureDownloadService.VerifyFileAsync(path, asset.Sha256, cancellationToken);
        }
    }

    private Task<ProcessResult> RunPowerShellFileAsync(
        string scriptPath,
        Action<string> onOutput,
        CancellationToken cancellationToken)
    {
        var windowsPowerShell = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        return processRunner.RunAsync(
            windowsPowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
            onOutput,
            cancellationToken,
            TimeSpan.FromMinutes(15),
            Path.GetDirectoryName(scriptPath));
    }
}
