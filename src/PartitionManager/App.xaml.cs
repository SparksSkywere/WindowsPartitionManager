using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using PartitionManager.Cli;
using PartitionManager.Helpers;
using PartitionManager.Services;
using PartitionManager.ViewModels;

namespace PartitionManager;

public partial class App : Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private const int AttachParentProcess = -1;
    private static int _uiErrorCount;
    private static string? _lastUiError;
    private LogService? _log;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                _log?.Error("Fatal: " + ex.Message);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log?.Error("Background task: " + args.Exception.GetBaseException().Message);
            args.SetObserved();
        };

        if (!CliHost.ShouldRunCli(e.Args) && ElevationHelper.TryRelaunchElevated(e.Args))
        {
            Shutdown(0);
            return;
        }

        var config = new ConfigService();
        ThemeManager.Apply(config.Config.General.Theme);

        _log = new LogService();
        if (ElevationHelper.IsElevated())
            _log.Info("Running with administrator privileges.");
        else if (CliHost.ShouldRunCli(e.Args))
            _log.Info("CLI mode without administrator privileges.");
        else
            _log.Info("Running without administrator privileges (UAC declined or unavailable). Partition changes require elevation.");

        var inventory = new DiskInventoryService(_log);
        var executor = new PartitionOperationExecutor(_log);
        var queue = new PendingQueueService(inventory, executor, _log);

        if (CliHost.ShouldRunCli(e.Args))
        {
            EnsureConsole();
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var code = await CliHost.RunAsync(e.Args, inventory, config, _log).ConfigureAwait(true);
            Shutdown(code);
            return;
        }

        var mainVm = new MainViewModel(queue, executor, config, _log);
        var window = new MainWindow(mainVm, config);
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        var message = args.Exception.GetBaseException().Message;
        var count = Interlocked.Increment(ref _uiErrorCount);
        if (count <= 8 || !string.Equals(message, _lastUiError, StringComparison.Ordinal))
            _log?.Error("UI: " + message);
        _lastUiError = message;

        if (count == 1)
        {
            MessageBox.Show(
                message + "\n\nFurther errors will be written to the activity log only.",
                "Partition Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        args.Handled = true;
    }

    private static void EnsureConsole()
    {
        if (!AttachConsole(AttachParentProcess))
            AllocConsole();

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        Console.SetIn(new StreamReader(Console.OpenStandardInput()));
    }
}
