using System.Diagnostics;
using System.Text;

namespace PartitionManager.Helpers;

public sealed class ProcessResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
    public bool Success => ExitCode == 0 && !TimedOut;
    public string CombinedOutput =>
        string.IsNullOrWhiteSpace(StdErr) ? StdOut : $"{StdOut}\n{StdErr}".Trim();
}

public sealed class ProcessRunOptions
{
    public int TimeoutSeconds { get; init; } = 300;
    public string? WorkingDirectory { get; init; }
    public bool ShowWindow { get; init; }
}

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default,
        int timeoutSeconds = 300,
        string? workingDirectory = null)
        => await RunAsync(
            fileName,
            arguments,
            new ProcessRunOptions
            {
                TimeoutSeconds = timeoutSeconds,
                WorkingDirectory = workingDirectory
            },
            cancellationToken).ConfigureAwait(false);

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        ProcessRunOptions options,
        CancellationToken cancellationToken = default)
    {
        var argList = arguments.ToList();
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = !options.ShowWindow,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (!string.IsNullOrWhiteSpace(options.WorkingDirectory))
            psi.WorkingDirectory = options.WorkingDirectory;

        foreach (var arg in argList)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (stdout) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (stderr) stderr.AppendLine(e.Data);
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessResult
                {
                    ExitCode = -1,
                    StdErr = $"Failed to start process: {fileName}"
                };
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new ProcessResult
                {
                    ExitCode = -1,
                    TimedOut = true,
                    StdOut = stdout.ToString(),
                    StdErr = "Process timed out."
                };
            }

            await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StdOut = stdout.ToString(),
                StdErr = stderr.ToString()
            };
        }
        catch (Exception ex)
        {
            TryKill(process);
            return new ProcessResult
            {
                ExitCode = -1,
                StdErr = ex.Message
            };
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignored
        }
    }
}
