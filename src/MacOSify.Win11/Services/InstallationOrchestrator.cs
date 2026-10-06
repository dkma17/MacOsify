using MacOSify.Win11.Models;
using MacOSify.Win11.Modules;

namespace MacOSify.Win11.Services;

public sealed class InstallationOrchestrator
{
    private readonly IReadOnlyDictionary<ModuleId, ICustomizationModule> _modules;
    private readonly SystemSafetyService _safety;
    private readonly OperationJournalService _journal;
    private readonly RollbackService _rollback;
    private readonly RegistryService _registry;
    private readonly WingetService _winget;
    private readonly VerifiedRecipeService _recipes;

    public InstallationOrchestrator(
        IEnumerable<ICustomizationModule> modules,
        SystemSafetyService safety,
        OperationJournalService journal,
        RollbackService rollback,
        RegistryService registry,
        WingetService winget,
        VerifiedRecipeService recipes)
    {
        _modules = modules.ToDictionary(module => module.Id);
        _safety = safety;
        _journal = journal;
        _rollback = rollback;
        _registry = registry;
        _winget = winget;
        _recipes = recipes;
    }

    public async Task InstallAsync(
        IReadOnlyCollection<ModuleId> selectedModules,
        IProgress<InstallUpdate> progress,
        CancellationToken cancellationToken)
    {
        using var processGate = new Semaphore(1, 1, @"Global\MacOSify.Win11.Install");
        if (!processGate.WaitOne(0))
        {
            throw new InvalidOperationException("Another macOSify installation or rollback is already running.");
        }

        var journalStarted = false;
        try
        {
            Report(progress, 2, "Checking this PC", "Running read-only safety and compatibility checks.");
            await _safety.ValidatePreflightAsync(selectedModules, cancellationToken);

            Report(progress, 7, "Preparing rollback", "Creating the durable operation journal.");
            await _journal.BeginAsync(selectedModules, cancellationToken);
            journalStarted = true;

            Report(progress, 10, "Protecting your PC", "No customization will run unless Windows creates a new restore point.", canCancel: false);
            var restorePointSequence = await _safety.CreateRestorePointAsync(
                message => Report(progress, 10, "Protecting your PC", message, canCancel: false),
                CancellationToken.None);
            await _journal.SetStatusAsync(JournalStatus.RestorePointCreated, CancellationToken.None, restorePointSequence);
            cancellationToken.ThrowIfCancellationRequested();
            await _journal.SetStatusAsync(JournalStatus.Applying, cancellationToken);

            var selectedImplementations = selectedModules.Select(id =>
                _modules.TryGetValue(id, out var module)
                    ? module
                    : throw new InvalidOperationException($"No implementation is registered for {id}.")).ToList();

            var completed = 0;
            var context = new InstallContext(
                _registry,
                _winget,
                _recipes,
                (message, level) => Report(progress, InstallPercent(), "Applying customizations", message, level));

            foreach (var module in selectedImplementations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, InstallPercent(), $"Installing {module.DisplayName}", $"Starting {module.DisplayName}.");
                await module.ApplyAsync(context, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                completed++;
                Report(progress, InstallPercent(), $"Installed {module.DisplayName}", $"{module.DisplayName} completed.", InstallLogLevel.Success);
            }

            await _journal.SetStatusAsync(JournalStatus.Applied, cancellationToken);
            Report(progress, 100, "Installation complete", "All selected reversible operations completed.", InstallLogLevel.Success, false);
            return;

            double InstallPercent() => 15 + (selectedImplementations.Count == 0 ? 0 : (double)completed / selectedImplementations.Count * 80);
        }
        catch (Exception originalException)
        {
            if (journalStarted)
            {
                Report(progress, 85, "A problem occurred", "Rolling back completed operations...", InstallLogLevel.Warning, false);
                try
                {
                    await _rollback.RollbackAsync(progress, CancellationToken.None, startPercent: 85);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "Installation failed and automatic rollback needs attention. Open the journal before making further changes.",
                        originalException,
                        rollbackException);
                }
            }

            throw;
        }
        finally
        {
            processGate.Release();
        }
    }

    public async Task RevertAsync(IProgress<InstallUpdate> progress, CancellationToken cancellationToken)
    {
        using var processGate = new Semaphore(1, 1, @"Global\MacOSify.Win11.Install");
        if (!processGate.WaitOne(0))
        {
            throw new InvalidOperationException("Another macOSify installation or rollback is already running.");
        }

        try
        {
            if (!_safety.IsAdministrator)
            {
                throw new UnauthorizedAccessException("Administrator approval is required to revert macOSify changes.");
            }

            var activeJournal = await _journal.LoadAsync(cancellationToken)
                ?? throw new InvalidOperationException("No macOSify operation journal was found.");
            _safety.ValidateInteractiveUser(activeJournal.UserSid);
            await _rollback.RollbackAsync(progress, cancellationToken);
        }
        finally
        {
            processGate.Release();
        }
    }

    private static void Report(
        IProgress<InstallUpdate> progress,
        double percent,
        string status,
        string message,
        InstallLogLevel level = InstallLogLevel.Info,
        bool canCancel = true) =>
        progress.Report(new InstallUpdate(Math.Clamp(percent, 0, 100), status, message, level, canCancel));
}
