using MacOSify.Win11.Models;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MacOSify.Win11.Services;

public sealed class SystemSafetyService(ProcessRunner processRunner, WingetService winget, VerifiedRecipeService recipes)
{
    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public async Task ValidatePreflightAsync(IReadOnlyCollection<ModuleId> modules, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            throw new PlatformNotSupportedException("macOSify requires Windows 11 build 22000 or newer.");
        }

        if (!IsAdministrator)
        {
            throw new UnauthorizedAccessException("Administrator approval is required before macOSify can continue.");
        }

        ValidateInteractiveUser();

        if (modules.Count == 0)
        {
            throw new InvalidOperationException("Select at least one customization module.");
        }

        if (RuntimeInformation.OSArchitecture is not Architecture.X64 and not Architecture.Arm64)
        {
            throw new PlatformNotSupportedException("Only x64 and ARM64 Windows installations are supported.");
        }

        if (RuntimeInformation.OSArchitecture == Architecture.Arm64 &&
            modules.Contains(ModuleId.Dock))
        {
            throw new PlatformNotSupportedException(
                "The Dock and System Theme package paths have not been validated on ARM64. Disable those modules or use the x64 build on an x64 PC.");
        }

        var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        if (systemDrive.AvailableFreeSpace < 1_000_000_000)
        {
            throw new IOException("At least 1 GB of free space is required for downloads, backups, and rollback data.");
        }

        if (modules.Any(module => module is ModuleId.Dock or ModuleId.TopBar or ModuleId.WindowEffects))
        {
            await winget.EnsureAvailableAsync(cancellationToken);
        }

        if (modules.Contains(ModuleId.Dock))
        {
            await winget.EnsurePackageAvailableAsync("WinStep.Nexus", "25.9", cancellationToken);
        }
        if (modules.Contains(ModuleId.TopBar))
        {
            await winget.EnsurePackageAvailableAsync("Rainmeter.Rainmeter", "4.5.26.3894", cancellationToken);
        }
        if (modules.Contains(ModuleId.WindowEffects))
        {
            await winget.EnsurePackageAvailableAsync("MicaForEveryone.MicaForEveryone", "2.0.6.0", cancellationToken);
        }

        if (modules.Contains(ModuleId.Icons))
        {
            await recipes.ValidateAsync("icons", cancellationToken);
        }

        if (modules.Contains(ModuleId.Cursors))
        {
            await recipes.ValidateAsync("cursors", cancellationToken);
        }
    }

    public async Task<long?> CreateRestorePointAsync(Action<string> onOutput, CancellationToken cancellationToken)
    {
        var windowsPowerShell = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        const string command = """
            $ErrorActionPreference = 'SilentlyContinue'

            # Ensure required volume shadow services are enabled and started
            Get-Service -Name VSS, swprv | ForEach-Object {
                if ($_.StartType -eq 'Disabled') {
                    Set-Service -Name $_.Name -StartupType Manual
                }
                Start-Service -Name $_.Name
            }

            # Relax restore point frequency throttling so creation is not blocked
            $srKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
            if (Test-Path $srKey) {
                Set-ItemProperty -Path $srKey -Name 'SystemRestorePointCreationFrequency' -Value 0 -Type DWord -Force
                Set-ItemProperty -Path $srKey -Name 'DisableSR' -Value 0 -Type DWord -Force
            }

            # Enable System Protection on the system drive
            $sysDrive = $env:SystemDrive
            if (-not $sysDrive.EndsWith('\')) { $sysDrive = "$sysDrive\" }
            try {
                Enable-ComputerRestore -Drive $sysDrive -ErrorAction SilentlyContinue
            } catch {}

            $before = @((Get-ComputerRestorePoint -ErrorAction SilentlyContinue | ForEach-Object { [long]$_.SequenceNumber }))
            $created = $null
            try {
                Checkpoint-Computer -Description 'Pre-macOSify' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
                $created = Get-ComputerRestorePoint -ErrorAction SilentlyContinue |
                    Where-Object { $_.Description -eq 'Pre-macOSify' -and $before -notcontains [long]$_.SequenceNumber } |
                    Sort-Object SequenceNumber -Descending |
                    Select-Object -First 1
            } catch {
                Write-Output ('RESTORE_POINT_WARNING=' + $_.Exception.Message)
            }

            if ($null -ne $created) {
                Write-Output ('RESTORE_POINT_SEQUENCE=' + [long]$created.SequenceNumber)
            } else {
                Write-Output 'RESTORE_POINT_SKIPPED=1'
            }
            """;

        onOutput("Creating and verifying Windows restore point ‘Pre-macOSify’…");
        var result = await processRunner.RunAsync(
            windowsPowerShell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command],
            onOutput,
            cancellationToken,
            TimeSpan.FromMinutes(5));

        var lines = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        var sequenceLine = lines.FirstOrDefault(line => line.StartsWith("RESTORE_POINT_SEQUENCE=", StringComparison.Ordinal));
        if (sequenceLine is not null && long.TryParse(sequenceLine["RESTORE_POINT_SEQUENCE=".Length..], out var sequence))
        {
            onOutput($"Verified System Restore point #{sequence} created.");
            return sequence;
        }

        onOutput("Windows System Protection is not active on this drive; continuing with atomic rollback journal protection.");
        return null;
    }

    public void ValidateInteractiveUser(string? expectedJournalSid = null)
    {
        var interactiveShellSid = GetInteractiveShellSid();
        var currentSid = WindowsIdentity.GetCurrent().User;
        if (interactiveShellSid is null || currentSid is null || !interactiveShellSid.Equals(currentSid))
        {
            throw new UnauthorizedAccessException(
                "The administrator account approved by UAC is not the account that owns this desktop. " +
                "macOSify stopped to avoid changing the wrong user profile. Sign in with an administrator account and try again.");
        }

        if (!string.IsNullOrWhiteSpace(expectedJournalSid))
        {
            SecurityIdentifier journalSid;
            try
            {
                journalSid = new SecurityIdentifier(expectedJournalSid);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("The operation journal contains an invalid Windows user identity.", exception);
            }

            if (!journalSid.Equals(currentSid))
            {
                throw new UnauthorizedAccessException(
                    "This operation journal belongs to a different Windows user. macOSify will not replay per-user changes in this account.");
            }
        }
    }

    private static SecurityIdentifier? GetInteractiveShellSid()
    {
        var shellWindow = FindWindow("Shell_TrayWnd", null);
        if (shellWindow == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(shellWindow, out var processId);
        if (processId == 0)
        {
            return null;
        }

        var processHandle = OpenProcess(0x1000, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (!OpenProcessToken(processHandle, 0x0008, out var tokenHandle))
            {
                return null;
            }

            try
            {
                _ = GetTokenInformation(tokenHandle, 1, IntPtr.Zero, 0, out var requiredLength);
                if (requiredLength <= 0)
                {
                    return null;
                }

                var buffer = Marshal.AllocHGlobal(requiredLength);
                try
                {
                    if (!GetTokenInformation(tokenHandle, 1, buffer, requiredLength, out _))
                    {
                        return null;
                    }

                    return new SecurityIdentifier(Marshal.ReadIntPtr(buffer));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                _ = CloseHandle(tokenHandle);
            }
        }
        finally
        {
            _ = CloseHandle(processHandle);
        }
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
