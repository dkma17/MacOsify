using Microsoft.Win32;

namespace MacOSify.Win11.Models;

public enum ModuleId
{
    Dock,
    TopBar,
    Theme,
    Icons,
    Cursors,
    WindowEffects
}

public enum InstallLogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record InstallUpdate(
    double Percent,
    string Status,
    string Message,
    InstallLogLevel Level = InstallLogLevel.Info,
    bool CanCancel = true);

public enum JournalStatus
{
    Planned,
    RestorePointCreated,
    Applying,
    Applied,
    RollingBack,
    RolledBack,
    RecoveryRequired
}

public enum JournalOperationKind
{
    RegistryValue,
    WingetPackage,
    VerifiedRecipe
}

public enum JournalEntryStatus
{
    Planned,
    Applied,
    Reverted,
    RevertFailed
}

public sealed class OperationJournal
{
    public int FormatVersion { get; set; } = 1;
    public Guid TransactionId { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public string WindowsVersion { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string UserSid { get; set; } = string.Empty;
    public JournalStatus Status { get; set; } = JournalStatus.Planned;
    public long? RestorePointSequence { get; set; }
    public List<string> SelectedModules { get; set; } = [];
    public List<JournalEntry> Entries { get; set; } = [];
}

public sealed class JournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public JournalOperationKind Kind { get; set; }
    public JournalEntryStatus Status { get; set; } = JournalEntryStatus.Planned;
    public string Description { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? ValueName { get; set; }
    public bool RegistryKeyExisted { get; set; } = true;
    public bool OriginalValueExisted { get; set; }
    public RegistryValueKind? OriginalRegistryKind { get; set; }
    public string? OriginalValue { get; set; }
    public RegistryValueKind? AppliedRegistryKind { get; set; }
    public string? AppliedValue { get; set; }
    public string? ApplyPath { get; set; }
    public string? ApplySha256 { get; set; }
    public string? RevertPath { get; set; }
    public string? RevertSha256 { get; set; }
    public List<VerifiedRecipeAsset> RecoveryAssets { get; set; } = [];
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public sealed class VerifiedRecipe
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ApplyScript { get; set; } = string.Empty;
    public string ApplySha256 { get; set; } = string.Empty;
    public string RevertScript { get; set; } = string.Empty;
    public string RevertSha256 { get; set; } = string.Empty;
    public List<VerifiedRecipeAsset> Assets { get; set; } = [];
}

public sealed class VerifiedRecipeAsset
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}
