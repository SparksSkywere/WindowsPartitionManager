using System.IO;
using PartitionManager.Helpers;
using PartitionManager.Services;

namespace PartitionManager;

/// <summary>
/// Entry point. Offline Recovery apply must not construct the WPF application
/// (that can hang on a message box with no input).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--offline-apply", StringComparison.OrdinalIgnoreCase)))
            return RunOffline(args);

        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }

    private static int RunOffline(string[] args)
    {
        try
        {
            var log = new LogService();
            var inventory = new DiskInventoryService(log);
            var executor = new PartitionOperationExecutor(log);
            var config = new ConfigService();
            return Cli.CliHost.RunAsync(args, inventory, executor, config, log)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(OfflineApplyService.JobDirectory);
                File.WriteAllText(OfflineApplyService.StatusPath, "failed: " + ex.Message);
            }
            catch
            {
                // ignore
            }

            try { Console.Error.WriteLine(ex.Message); } catch { /* no console */ }
            return 1;
        }
    }
}
