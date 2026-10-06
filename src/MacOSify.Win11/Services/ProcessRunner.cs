using MacOSify.Win11.Models;
using System.Diagnostics;
using System.Text;

namespace MacOSify.Win11.Services;

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        Action<string>? onOutput,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, e) => CaptureLine(e.Data, standardOutput, onOutput);
        process.ErrorDataReceived += (_, e) => CaptureLine(e.Data, standardError, onOutput);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {Path.GetFileName(fileName)}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
        using var linkedSource = timeoutSource is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            await TryTerminateAsync(process);

            if (timeoutSource?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"{Path.GetFileName(fileName)} did not finish within {timeout}.");
            }

            throw;
        }

        return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }

    private static void CaptureLine(string? line, StringBuilder destination, Action<string>? onOutput)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var safeLine = new string(line.Where(character => character == '\t' || !char.IsControl(character)).Take(2_000).ToArray());
        const int maximumCapturedCharacters = 1_000_000;
        if (destination.Length < maximumCapturedCharacters)
        {
            var remaining = maximumCapturedCharacters - destination.Length;
            destination.AppendLine(safeLine.Length <= remaining ? safeLine : safeLine[..remaining]);
        }
        onOutput?.Invoke(safeLine);
    }

    private static async Task TryTerminateAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var waitLimit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(waitLimit.Token);
            }
        }
        catch
        {
            // The caller will recover from the durable journal on the next launch.
        }
    }
}
