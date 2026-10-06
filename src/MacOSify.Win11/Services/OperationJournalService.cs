using MacOSify.Win11.Models;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacOSify.Win11.Services;

public sealed class OperationJournalService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public OperationJournal? Current { get; private set; }

    public async Task<OperationJournal?> LoadAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureCreated();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Current = await ReadJournalAsync(AppPaths.Journal, cancellationToken);
            if (Current is null)
            {
                Current = await ReadJournalAsync(AppPaths.JournalBackup, cancellationToken);
                if (Current is not null)
                {
                    File.Copy(AppPaths.JournalBackup, AppPaths.Journal, overwrite: true);
                }
            }
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationJournal> BeginAsync(IEnumerable<ModuleId> selectedModules, CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await ReadJournalAsync(AppPaths.Journal, cancellationToken)
                ?? await ReadJournalAsync(AppPaths.JournalBackup, cancellationToken);
            if (existing is not null && existing.Status is not JournalStatus.RolledBack)
            {
                throw new InvalidOperationException(
                    "An applied or unfinished macOSify transaction already exists. Revert or recover it before starting another installation.");
            }

            Current = new OperationJournal
            {
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
                WindowsVersion = Environment.OSVersion.VersionString,
                Architecture = RuntimeInformation.OSArchitecture.ToString(),
                UserSid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown",
                SelectedModules = selectedModules.Select(module => module.ToString()).ToList()
            };

            await SaveCoreAsync(cancellationToken);
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddEntryAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        await MutateAsync(journal => journal.Entries.Add(entry), cancellationToken);
    }

    public async Task MarkEntryAsync(Guid entryId, JournalEntryStatus status, CancellationToken cancellationToken)
    {
        await MutateAsync(journal =>
        {
            var entry = journal.Entries.Single(candidate => candidate.Id == entryId);
            entry.Status = status;
        }, cancellationToken);
    }

    public async Task SetStatusAsync(
        JournalStatus status,
        CancellationToken cancellationToken,
        long? restorePointSequence = null)
    {
        await MutateAsync(journal =>
        {
            journal.Status = status;
            if (restorePointSequence.HasValue)
            {
                journal.RestorePointSequence = restorePointSequence;
            }

            if (status is JournalStatus.Applied or JournalStatus.RolledBack or JournalStatus.RecoveryRequired)
            {
                journal.CompletedAt = DateTimeOffset.UtcNow;
            }
        }, cancellationToken);
    }

    private async Task MutateAsync(Action<OperationJournal> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Current is null)
            {
                throw new InvalidOperationException("No operation journal is active.");
            }

            mutation(Current);
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        if (Current is null)
        {
            throw new InvalidOperationException("No operation journal is active.");
        }

        var temporaryPath = Path.Combine(AppPaths.State, $"journal-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16_384,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(AppPaths.Journal))
            {
                File.Replace(temporaryPath, AppPaths.Journal, AppPaths.JournalBackup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, AppPaths.Journal);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<OperationJournal?> ReadJournalAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<OperationJournal>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
