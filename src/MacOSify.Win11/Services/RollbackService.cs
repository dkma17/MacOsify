using MacOSify.Win11.Models;

namespace MacOSify.Win11.Services;

public sealed class RollbackService(
    OperationJournalService journal,
    RegistryService registry,
    WingetService winget,
    VerifiedRecipeService recipes)
{
    public async Task RollbackAsync(
        IProgress<InstallUpdate> progress,
        CancellationToken cancellationToken = default,
        double startPercent = 0)
    {
        var activeJournal = journal.Current ?? await journal.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException("No macOSify operation journal was found.");

        await journal.SetStatusAsync(JournalStatus.RollingBack, cancellationToken);
        var candidates = activeJournal.Entries
            .Where(entry => entry.Status is JournalEntryStatus.Planned or JournalEntryStatus.Applied or JournalEntryStatus.RevertFailed)
            .Reverse()
            .ToList();
        var failures = new List<Exception>();

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = candidates[index];
            var percentage = candidates.Count == 0
                ? 100
                : startPercent + (double)index / candidates.Count * (100 - startPercent);
            progress.Report(new InstallUpdate(
                percentage,
                "Restoring Windows settings",
                $"Reverting: {entry.Description}",
                InstallLogLevel.Info,
                CanCancel: false));

            try
            {
                switch (entry.Kind)
                {
                    case JournalOperationKind.RegistryValue:
                        await registry.RevertAsync(entry, cancellationToken);
                        break;
                    case JournalOperationKind.WingetPackage:
                        await winget.UninstallIfOwnedAsync(entry, message =>
                            progress.Report(new InstallUpdate(percentage, "Restoring Windows settings", message, CanCancel: false)), cancellationToken);
                        break;
                    case JournalOperationKind.VerifiedRecipe:
                        await recipes.RevertAsync(entry, message =>
                            progress.Report(new InstallUpdate(percentage, "Restoring Windows settings", message, CanCancel: false)), cancellationToken);
                        break;
                    default:
                        throw new InvalidDataException($"Unknown journal operation kind: {entry.Kind}");
                }

                await journal.MarkEntryAsync(entry.Id, JournalEntryStatus.Reverted, cancellationToken);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                await journal.MarkEntryAsync(entry.Id, JournalEntryStatus.RevertFailed, CancellationToken.None);
                progress.Report(new InstallUpdate(
                    percentage,
                    "Rollback needs attention",
                    $"Could not revert {entry.Description}: {exception.Message}",
                    InstallLogLevel.Error,
                    CanCancel: false));
            }
        }

        if (failures.Count > 0)
        {
            await journal.SetStatusAsync(JournalStatus.RecoveryRequired, CancellationToken.None);
            throw new AggregateException("One or more changes could not be reverted. The journal was preserved for recovery.", failures);
        }

        await journal.SetStatusAsync(JournalStatus.RolledBack, CancellationToken.None);
        progress.Report(new InstallUpdate(100, "Windows defaults restored", "Rollback completed successfully.", InstallLogLevel.Success, false));
    }
}
