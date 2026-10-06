using MacOSify.Win11.Models;
using System.Text.Json;

namespace MacOSify.Win11.Services;

public sealed class WingetService(ProcessRunner processRunner, OperationJournalService journal)
{
    private static readonly TimeSpan PackageTimeout = TimeSpan.FromMinutes(15);

    public async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await processRunner.RunAsync(
                "winget.exe",
                ["--version"],
                null,
                cancellationToken,
                TimeSpan.FromSeconds(20));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException("WinGet returned an error.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Windows Package Manager (WinGet) is required. Install or update App Installer from Microsoft Store, then try again.",
                exception);
        }
    }

    public async Task InstallAsync(
        string packageId,
        string pinnedVersion,
        string description,
        Action<string> onOutput,
        CancellationToken cancellationToken)
    {
        var installedVersion = await GetInstalledVersionAsync(packageId, cancellationToken);
        var alreadyInstalled = installedVersion is not null;
        if (alreadyInstalled && !installedVersion!.Equals(pinnedVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{packageId} version {installedVersion} is already installed. This build requires {pinnedVersion} and will not upgrade or downgrade a package it does not own.");
        }
        var entry = new JournalEntry
        {
            Kind = JournalOperationKind.WingetPackage,
            Description = description,
            Target = packageId,
            OriginalValueExisted = alreadyInstalled,
            OriginalValue = installedVersion ?? "not-installed"
        };

        await journal.AddEntryAsync(entry, cancellationToken);

        if (!alreadyInstalled)
        {
            var arguments = new[]
            {
                "install", "--id", packageId, "--exact", "--version", pinnedVersion,
                "--source", "winget", "--silent", "--disable-interactivity",
                "--accept-package-agreements", "--accept-source-agreements"
            };

            var result = await processRunner.RunAsync(
                "winget.exe",
                arguments,
                onOutput,
                CancellationToken.None,
                PackageTimeout);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"WinGet could not install {packageId} (exit code 0x{result.ExitCode:X8}). {result.StandardError.Trim()}");
            }

            var verifiedVersion = await GetInstalledVersionAsync(packageId, CancellationToken.None);
            if (verifiedVersion is null)
            {
                throw new InvalidOperationException($"WinGet returned success, but {packageId} was not present in the installed-package inventory.");
            }
        }
        else
        {
            onOutput($"{packageId} was already installed; it will not be removed during rollback.");
        }

        await journal.MarkEntryAsync(entry.Id, JournalEntryStatus.Applied, CancellationToken.None);
    }

    public async Task EnsurePackageAvailableAsync(
        string packageId,
        string pinnedVersion,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "winget.exe",
            [
                "show", "--id", packageId, "--exact", "--version", pinnedVersion,
                "--source", "winget", "--disable-interactivity", "--accept-source-agreements"
            ],
            null,
            cancellationToken,
            TimeSpan.FromMinutes(2));

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"The pinned package {packageId} {pinnedVersion} is unavailable from the configured WinGet source. No system changes were made.");
        }
    }

    public async Task UninstallIfOwnedAsync(
        JournalEntry entry,
        Action<string> onOutput,
        CancellationToken cancellationToken)
    {
        if (entry.OriginalValueExisted)
        {
            onOutput($"Keeping pre-existing package {entry.Target}.");
            return;
        }

        if (await GetInstalledVersionAsync(entry.Target, cancellationToken) is null)
        {
            return;
        }

        var result = await processRunner.RunAsync(
            "winget.exe",
            [
                "uninstall", "--id", entry.Target, "--exact",
                "--silent", "--disable-interactivity"
            ],
            onOutput,
            CancellationToken.None,
            PackageTimeout);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"WinGet could not uninstall {entry.Target} (exit code 0x{result.ExitCode:X8}).");
        }
    }

    private async Task<string?> GetInstalledVersionAsync(string packageId, CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        var inventoryPath = Path.Combine(AppPaths.Cache, $"winget-inventory-{Guid.NewGuid():N}.json");
        try
        {
            var result = await processRunner.RunAsync(
                "winget.exe",
                [
                    "export", "--output", inventoryPath, "--source", "winget", "--include-versions",
                    "--disable-interactivity", "--accept-source-agreements", "--ignore-warnings"
                ],
                null,
                cancellationToken,
                TimeSpan.FromMinutes(2));

            if (!result.Succeeded || !File.Exists(inventoryPath))
            {
                throw new InvalidOperationException(
                    "WinGet could not produce a trustworthy installed-package inventory. Package ownership was not changed.");
            }

            await using var stream = File.OpenRead(inventoryPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("Sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("WinGet returned an inventory with an unknown schema.");
            }

            foreach (var source in sources.EnumerateArray())
            {
                if (!source.TryGetProperty("Packages", out var packages) || packages.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var package in packages.EnumerateArray())
                {
                    if (!package.TryGetProperty("PackageIdentifier", out var identifier) ||
                        !string.Equals(identifier.GetString(), packageId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!package.TryGetProperty("Version", out var version) || string.IsNullOrWhiteSpace(version.GetString()))
                    {
                        throw new InvalidDataException($"WinGet did not report an installed version for {packageId}.");
                    }

                    return version.GetString();
                }
            }

            return null;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("WinGet returned malformed installed-package inventory data.", exception);
        }
        finally
        {
            if (File.Exists(inventoryPath))
            {
                File.Delete(inventoryPath);
            }
        }
    }
}
