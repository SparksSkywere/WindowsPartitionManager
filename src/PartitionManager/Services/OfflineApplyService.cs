using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PartitionManager.Helpers;
using PartitionManager.Models;

namespace PartitionManager.Services;

/// <summary>
/// Schedules a Windows-volume start-move for Windows Recovery, then restores Recovery files
/// the next time this program starts in full Windows.
/// </summary>
public sealed class OfflineApplyService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly LogService _log;

    public OfflineApplyService(LogService log)
    {
        _log = log;
    }

    public static string JobDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PartitionManager",
        "offline");

    public static string JobPath => Path.Combine(JobDirectory, "job.json");
    public static string ApplyCmdPath => Path.Combine(JobDirectory, "apply.cmd");
    public static string StatusPath => Path.Combine(JobDirectory, "status.txt");
    public static string RestoreFlagPath => Path.Combine(JobDirectory, "restore-recovery.flag");
    public const string WinPeLauncherName = "pm-offline.cmd";

    public void SaveJobs(IReadOnlyList<OfflineMoveJob> jobs)
    {
        Directory.CreateDirectory(JobDirectory);
        File.WriteAllText(JobPath, JsonSerializer.Serialize(jobs, JsonOptions));
        File.WriteAllText(ApplyCmdPath, BuildApplyCmd(), Encoding.ASCII);
        CopyAppFiles();
        _log.Info("Wrote offline move job to " + JobPath);
    }

    public async Task<OperationResult> PrepareRecoveryAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(JobDirectory);
        var wim = await FindWinReWimAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(wim))
            {
                WriteManualHint();
                return new OperationResult
                {
                    Success = false,
                    Message = "Windows Recovery was not found. " + ManualRecoveryMessage()
                };
            }

        var mount = Path.Combine(JobDirectory, "winre-mount");
        Directory.CreateDirectory(mount);
        var mounted = false;
        try
        {
            var mountResult = await ProcessRunner.RunAsync(
                "dism.exe",
                ["/mount-image", "/imagefile:" + wim, "/index:1", "/mountdir:" + mount],
                cancellationToken,
                timeoutSeconds: 300).ConfigureAwait(false);
            if (!mountResult.Success)
            {
                _log.Warn("Could not mount Windows Recovery image: " + mountResult.CombinedOutput);
                WriteManualHint();
                return new OperationResult
                {
                    Success = false,
                    Message = "Could not mount Windows Recovery. " + ManualRecoveryMessage()
                };
            }

            mounted = true;
            var system32 = Path.Combine(mount, "Windows", "System32");
            Directory.CreateDirectory(system32);
            File.WriteAllText(Path.Combine(system32, WinPeLauncherName), BuildWinPeLauncher(), Encoding.ASCII);
            var winpeshl = Path.Combine(system32, "winpeshl.ini");
            var backup = Path.Combine(JobDirectory, "winpeshl.ini.bak");
            if (File.Exists(winpeshl) && !File.Exists(backup))
                File.Copy(winpeshl, backup, overwrite: false);
            File.WriteAllText(
                winpeshl,
                "[LaunchApps]" + Environment.NewLine +
                "%SYSTEMROOT%\\System32\\" + WinPeLauncherName + Environment.NewLine,
                Encoding.ASCII);

            var injected = File.Exists(Path.Combine(system32, WinPeLauncherName)) &&
                           File.ReadAllText(winpeshl).Contains(WinPeLauncherName, StringComparison.OrdinalIgnoreCase);
            if (!injected)
            {
                WriteManualHint();
                return new OperationResult
                {
                    Success = false,
                    Message = "Could not update Windows Recovery. " + ManualRecoveryMessage()
                };
            }

            File.WriteAllText(RestoreFlagPath, "1");

            var unmount = await ProcessRunner.RunAsync(
                "dism.exe",
                ["/unmount-image", "/mountdir:" + mount, "/commit"],
                cancellationToken,
                timeoutSeconds: 300).ConfigureAwait(false);
            mounted = false;
            if (!unmount.Success)
            {
                _log.Error("Could not save Windows Recovery image: " + unmount.CombinedOutput);
                return new OperationResult
                {
                    Success = false,
                    Message = "Could not update Windows Recovery. " + ManualRecoveryMessage()
                };
            }

            var bootToRe = await ProcessRunner.RunAsync(
                "reagentc.exe",
                ["/boottore"],
                cancellationToken,
                timeoutSeconds: 60).ConfigureAwait(false);
            if (!bootToRe.Success)
            {
                _log.Warn("reagentc /boottore failed: " + bootToRe.CombinedOutput);
                return new OperationResult
                {
                    Success = false,
                    Message = "Could not set one-time Recovery boot. " + ManualRecoveryMessage()
                };
            }

            return new OperationResult
            {
                Success = true,
                UnattendedReady = true,
                Message = "Ready to restart and finish the Windows volume move."
            };
        }
        catch (Exception ex)
        {
            _log.Error("Prepare Windows Recovery failed: " + ex.Message);
            WriteManualHint();
            return new OperationResult
            {
                Success = false,
                Message = "Could not prepare Windows Recovery. " + ex.Message
            };
        }
        finally
        {
            if (mounted)
            {
                await ProcessRunner.RunAsync(
                    "dism.exe",
                    ["/unmount-image", "/mountdir:" + mount, "/discard"],
                    CancellationToken.None,
                    timeoutSeconds: 300).ConfigureAwait(false);
            }
        }
    }

    public async Task RestoreRecoveryAsync(CancellationToken cancellationToken = default)
    {
        var backup = Path.Combine(JobDirectory, "winpeshl.ini.bak");
        if (!File.Exists(RestoreFlagPath) && !File.Exists(backup))
            return;

        // Leave Recovery launch files in place until the scheduled job has run (or failed).
        if (File.Exists(JobPath) && !File.Exists(StatusPath))
            return;

        var wim = await FindWinReWimAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(wim) || !File.Exists(backup))
        {
            TryDelete(RestoreFlagPath);
            return;
        }

        var mount = Path.Combine(JobDirectory, "winre-mount");
        Directory.CreateDirectory(mount);
        var mounted = false;
        try
        {
            var mountResult = await ProcessRunner.RunAsync(
                "dism.exe",
                ["/mount-image", "/imagefile:" + wim, "/index:1", "/mountdir:" + mount],
                cancellationToken,
                timeoutSeconds: 300).ConfigureAwait(false);
            if (!mountResult.Success)
                return;

            mounted = true;
            var winpeshl = Path.Combine(mount, "Windows", "System32", "winpeshl.ini");
            File.Copy(backup, winpeshl, overwrite: true);
            TryDelete(Path.Combine(mount, "Windows", "System32", WinPeLauncherName));
            var unmount = await ProcessRunner.RunAsync(
                "dism.exe",
                ["/unmount-image", "/mountdir:" + mount, "/commit"],
                cancellationToken,
                timeoutSeconds: 300).ConfigureAwait(false);
            mounted = false;
            if (unmount.Success)
            {
                TryDelete(RestoreFlagPath);
                _log.Info("Restored Windows Recovery startup files.");
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Could not restore Windows Recovery files: " + ex.Message);
        }
        finally
        {
            if (mounted)
            {
                await ProcessRunner.RunAsync(
                    "dism.exe",
                    ["/unmount-image", "/mountdir:" + mount, "/discard"],
                    CancellationToken.None,
                    timeoutSeconds: 300).ConfigureAwait(false);
            }
        }
    }

    public async Task<int> RunJobAsync(
        string? jobPath,
        PartitionOperationExecutor executor,
        CancellationToken cancellationToken)
    {
        jobPath = string.IsNullOrWhiteSpace(jobPath) ? JobPath : jobPath;
        if (!File.Exists(jobPath))
        {
            Console.Error.WriteLine("No offline move job was found.");
            return 1;
        }

        List<OfflineMoveJob>? jobs;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jobPath));
            jobs = doc.RootElement.ValueKind == JsonValueKind.Array
                ? JsonSerializer.Deserialize<List<OfflineMoveJob>>(doc.RootElement.GetRawText(), JsonOptions)
                : [JsonSerializer.Deserialize<OfflineMoveJob>(doc.RootElement.GetRawText(), JsonOptions)!];
        }
        catch (Exception ex)
        {
            WriteStatus("failed: " + ex.Message);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        jobs = jobs?.Where(j => j is not null).ToList();
        if (jobs is null || jobs.Count == 0)
        {
            WriteStatus("failed: job file was empty.");
            return 1;
        }

        foreach (var job in jobs)
        {
            Console.WriteLine(job.Description);
            Console.WriteLine("Copying partition data. This can take a long time. Do not power off.");
            var progress = new Progress<int>(p => Console.Write("\r{0}%   ", p));
            var result = await executor.ExecuteAsync(job.ToOperation(), cancellationToken, progress)
                .ConfigureAwait(false);
            Console.WriteLine();
            if (!result.Success)
            {
                WriteStatus("failed: " + result.Message);
                Console.Error.WriteLine(result.Message);
                return 1;
            }
        }

        WriteStatus("ok: Windows volume move finished.");
        TryDelete(jobPath);
        Console.WriteLine("Move finished.");
        return 0;
    }

    public string? ReadStatus()
    {
        try
        {
            return File.Exists(StatusPath) ? File.ReadAllText(StatusPath).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    public void ClearStatus() => TryDelete(StatusPath);

    public static void RestartNow()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/r /t 3 /c \"Partition Manager is restarting into Windows Recovery to move the Windows volume.\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // Caller already showed the Recovery instructions.
        }
    }

    public static bool IsBootStartMove(PendingOperation op) =>
        op.Kind == OperationKind.ResizePartition &&
        op.Resize is { ChangeOffset: true } r &&
        WindowsVolume.IsOnlineSystemVolume(r.DriveLetter, r.IsBoot);

    private void CopyAppFiles()
    {
        try
        {
            var exe = ResolveExecutable();
            var src = Path.GetDirectoryName(exe);
            if (string.IsNullOrWhiteSpace(src) || !Directory.Exists(src))
                return;

            var dest = Path.Combine(JobDirectory, "app");
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.EnumerateFiles(src))
            {
                try
                {
                    File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
                }
                catch
                {
                    // skip locked files
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Could not copy the program next to the offline job: " + ex.Message);
        }
    }

    private static string ResolveExecutable()
    {
        var process = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(process) && File.Exists(process))
            return process;
        return Path.Combine(AppContext.BaseDirectory, AppInfo.ExeFileName);
    }

    private static string BuildApplyCmd() =>
        """
        @echo off
        setlocal
        set JOB=%~dp0job.json
        set APP=%~dp0app\PartitionManager.exe
        set DLL=%~dp0app\PartitionManager.dll
        if exist "%~dp0app\coreclr.dll" (
          "%APP%" --offline-apply --job "%JOB%"
          exit /b %ERRORLEVEL%
        )
        if exist "%APP%" (
          "%APP%" --offline-apply --job "%JOB%"
          if not errorlevel 1 exit /b 0
        )
        for %%d in (C D E F G H I J K L M N O P Q R S T U V W) do (
          if exist "%%d:\Program Files\dotnet\dotnet.exe" if exist "%DLL%" (
            "%%d:\Program Files\dotnet\dotnet.exe" "%DLL%" --offline-apply --job "%JOB%"
            exit /b %ERRORLEVEL%
          )
        )
        echo failed: could not start Partition Manager> "%~dp0status.txt"
        exit /b 1
        """.Replace("\n", "\r\n");

    private static string BuildWinPeLauncher() =>
        """
        @echo off
        wpeinit
        ping 127.0.0.1 -n 15 >nul
        for %%d in (C D E F G H I J K L M N O P Q R S T U V W) do (
          if exist "%%d:\ProgramData\PartitionManager\offline\apply.cmd" (
            call "%%d:\ProgramData\PartitionManager\offline\apply.cmd"
            goto :reboot
          )
        )
        :reboot
        wpeutil reboot
        """.Replace("\n", "\r\n");

    private void WriteManualHint() =>
        File.WriteAllText(Path.Combine(JobDirectory, "readme.txt"), ManualRecoveryMessage());

    private static string ManualRecoveryMessage() =>
        "In Windows Recovery open Command Prompt and run: " + ApplyCmdPath;

    private async Task<string?> FindWinReWimAsync(CancellationToken cancellationToken)
    {
        var info = await ProcessRunner.RunAsync("reagentc.exe", ["/info"], cancellationToken, timeoutSeconds: 60)
            .ConfigureAwait(false);
        var candidates = new List<string>();
        var match = Regex.Match(
            info.CombinedOutput,
            @"Windows RE location:\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (match.Success)
        {
            var loc = match.Groups[1].Value.Trim();
            candidates.Add(Path.Combine(loc, "Winre.wim"));
            candidates.Add(loc.TrimEnd('\\') + @"\Winre.wim");
        }

        candidates.Add(Path.Combine(Environment.SystemDirectory, "Recovery", "Winre.wim"));
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                    return path;
            }
            catch
            {
                // GLOBALROOT device paths can throw
            }
        }

        return candidates.FirstOrDefault();
    }

    private void WriteStatus(string text)
    {
        try
        {
            Directory.CreateDirectory(JobDirectory);
            File.WriteAllText(StatusPath, text);
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}
