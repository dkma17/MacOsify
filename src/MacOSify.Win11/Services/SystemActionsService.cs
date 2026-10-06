using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacOSify.Win11.Services;

public sealed class SystemActionsService
{
    public async Task RestartExplorerAsync(CancellationToken cancellationToken)
    {
        var shellWindow = FindWindow("Shell_TrayWnd", null);
        if (shellWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException("The current Explorer shell window could not be found.");
        }

        _ = GetWindowThreadProcessId(shellWindow, out var processId);
        if (processId == 0)
        {
            throw new InvalidOperationException("The Explorer process could not be identified.");
        }

        using (var explorer = Process.GetProcessById((int)processId))
        {
            if (explorer.SessionId != Process.GetCurrentProcess().SessionId)
            {
                throw new InvalidOperationException("Refusing to restart Explorer in a different user session.");
            }

            explorer.Kill(entireProcessTree: false);
            await explorer.WaitForExitAsync(cancellationToken);
        }

        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = explorerPath,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Explorer did not restart.");
    }

    public void ScheduleReboot()
    {
        var shutdownPath = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = shutdownPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/r");
        startInfo.ArgumentList.Add("/t");
        startInfo.ArgumentList.Add("30");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("macOSify installation completed. Save your work; Windows will restart in 30 seconds.");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not accept the restart request.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Windows rejected the restart request (exit code {process.ExitCode}).");
        }
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
