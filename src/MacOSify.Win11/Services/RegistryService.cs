using MacOSify.Win11.Models;
using Microsoft.Win32;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MacOSify.Win11.Services;

public sealed class RegistryService(OperationJournalService journal)
{
    public async Task SetValueAsync(
        string keyPath,
        string valueName,
        object value,
        RegistryValueKind valueKind,
        string description,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (hive, subKey) = ParsePath(keyPath);
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        var key = baseKey.OpenSubKey(subKey, writable: true);
        var keyExisted = key is not null;
        var original = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        RegistryValueKind? originalKind = original is null ? null : key!.GetValueKind(valueName);
        var entry = new JournalEntry
        {
            Kind = JournalOperationKind.RegistryValue,
            Description = description,
            Target = keyPath,
            ValueName = valueName,
            RegistryKeyExisted = keyExisted,
            OriginalValueExisted = original is not null,
            OriginalRegistryKind = originalKind,
            OriginalValue = original is null ? null : EncodeRegistryValue(original, originalKind!.Value),
            AppliedRegistryKind = valueKind,
            AppliedValue = EncodeRegistryValue(value, valueKind)
        };

        try
        {
            await journal.AddEntryAsync(entry, cancellationToken);
            key ??= baseKey.CreateSubKey(subKey, writable: true)
                ?? throw new InvalidOperationException($"Could not create registry key {keyPath}.");
            key.SetValue(valueName, value, valueKind);
            await journal.MarkEntryAsync(entry.Id, JournalEntryStatus.Applied, cancellationToken);
            BroadcastSettingChange();
        }
        finally
        {
            key?.Dispose();
        }
    }

    public Task RevertAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (hive, subKey) = ParsePath(entry.Target);
        var valueName = entry.ValueName
            ?? throw new InvalidDataException($"Journal entry {entry.Id} does not contain a registry value name.");
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey, writable: true);
        if (key is null)
        {
            if (entry.OriginalValueExisted)
            {
                throw new InvalidOperationException($"Registry key {entry.Target} is missing, so its original value cannot be restored.");
            }

            return Task.CompletedTask;
        }

        var current = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var currentKind = current is null ? (RegistryValueKind?)null : key.GetValueKind(valueName);
        if (entry.AppliedRegistryKind is not null && entry.AppliedValue is not null &&
            !MatchesSnapshot(current, currentKind, valueExisted: true, entry.AppliedRegistryKind, entry.AppliedValue) &&
            !MatchesSnapshot(current, currentKind, entry.OriginalValueExisted, entry.OriginalRegistryKind, entry.OriginalValue))
        {
            throw new InvalidOperationException(
                $"Registry value {entry.Target}\\{valueName} changed after macOSify applied it. Rollback stopped instead of overwriting the newer value.");
        }

        if (!entry.OriginalValueExisted)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
        else
        {
            if (entry.OriginalRegistryKind is null || entry.OriginalValue is null)
            {
                throw new InvalidDataException($"Journal entry {entry.Id} does not contain a valid registry snapshot.");
            }

            key.SetValue(
                valueName,
                DecodeRegistryValue(entry.OriginalValue, entry.OriginalRegistryKind.Value),
                entry.OriginalRegistryKind.Value);
        }

        var deleteCreatedKey = !entry.RegistryKeyExisted &&
                               key.GetValueNames().Length == 0 &&
                               key.GetSubKeyNames().Length == 0;

        BroadcastSettingChange();
        if (deleteCreatedKey)
        {
            key.Dispose();
            baseKey.DeleteSubKey(subKey, throwOnMissingSubKey: false);
        }
        return Task.CompletedTask;
    }

    public byte[] ReadBinaryValue(string keyPath, string valueName)
    {
        var (hive, subKey) = ParsePath(keyPath);
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey, writable: false)
            ?? throw new InvalidOperationException($"Registry key {keyPath} does not exist on this Windows build.");

        return key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is byte[] value
            ? (byte[])value.Clone()
            : throw new InvalidOperationException($"Registry value {keyPath}\\{valueName} is missing or has an unexpected format.");
    }

    private static (RegistryHive Hive, string SubKey) ParsePath(string path)
    {
        const string currentUser = "HKEY_CURRENT_USER\\";
        const string localMachine = "HKEY_LOCAL_MACHINE\\";

        if (path.StartsWith(currentUser, StringComparison.OrdinalIgnoreCase))
        {
            return (RegistryHive.CurrentUser, path[currentUser.Length..]);
        }

        if (path.StartsWith(localMachine, StringComparison.OrdinalIgnoreCase))
        {
            return (RegistryHive.LocalMachine, path[localMachine.Length..]);
        }

        throw new ArgumentException("Only explicit HKEY_CURRENT_USER and HKEY_LOCAL_MACHINE paths are supported.", nameof(path));
    }

    private static string EncodeRegistryValue(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.Binary or RegistryValueKind.None => Convert.ToBase64String((byte[])value),
        RegistryValueKind.DWord => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.MultiString => JsonSerializer.Serialize((string[])value),
        RegistryValueKind.String or RegistryValueKind.ExpandString => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => throw new InvalidDataException($"Registry value kind {kind} is not supported by the journal format.")
    };

    private static object DecodeRegistryValue(string value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.Binary or RegistryValueKind.None => Convert.FromBase64String(value),
        RegistryValueKind.DWord => int.Parse(value, CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(value, CultureInfo.InvariantCulture),
        RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(value) ?? [],
        RegistryValueKind.String or RegistryValueKind.ExpandString => value,
        _ => throw new InvalidDataException($"Registry value kind {kind} is not supported by the journal format.")
    };

    private static bool MatchesSnapshot(
        object? current,
        RegistryValueKind? currentKind,
        bool valueExisted,
        RegistryValueKind? snapshotKind,
        string? snapshotValue)
    {
        if (!valueExisted)
        {
            return current is null;
        }

        return current is not null &&
               currentKind == snapshotKind &&
               snapshotKind is not null &&
               snapshotValue is not null &&
               EncodeRegistryValue(current, snapshotKind.Value).Equals(snapshotValue, StringComparison.Ordinal);
    }

    private static void BroadcastSettingChange()
    {
        _ = SendMessageTimeout(
            new IntPtr(0xffff),
            0x001A,
            IntPtr.Zero,
            "ImmersiveColorSet",
            0x0002,
            1_000,
            out _);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out IntPtr result);
}
