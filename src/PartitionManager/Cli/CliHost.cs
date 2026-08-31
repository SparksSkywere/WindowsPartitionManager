using PartitionManager.Helpers;
using PartitionManager.Services;

namespace PartitionManager.Cli;

public static class CliHost
{
    public static bool ShouldRunCli(string[] args)
    {
        if (args.Length == 0) return false;

        var set = new HashSet<string>(args, StringComparer.OrdinalIgnoreCase);
        return set.Contains("--no-ui") ||
               set.Contains("--list") ||
               set.Contains("--help") ||
               set.Contains("-h") ||
               set.Contains("/?");
    }

    public static async Task<int> RunAsync(
        string[] args,
        DiskInventoryService inventory,
        ConfigService config,
        LogService log)
    {
        var set = new HashSet<string>(args, StringComparer.OrdinalIgnoreCase);
        if (set.Contains("--help") || set.Contains("-h") || set.Contains("/?"))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            Console.WriteLine("Reading disks...");
            var layout = await inventory.QueryAsync(config.Config.Display).ConfigureAwait(false);
            Console.WriteLine($"Found {layout.Disks.Count} disk(s).");
            Console.WriteLine();

            foreach (var disk in layout.Disks)
            {
                Console.WriteLine(
                    $"Disk {disk.Number}  {disk.Model}  {ByteSizeFormatter.Format(disk.Size)}  {disk.StyleText}  {disk.BusType}  {disk.Status}");
                foreach (var seg in disk.Segments)
                {
                    var name = seg.IsUnallocated
                        ? "Unallocated"
                        : (seg.DriveLetter is char c ? $"{c}: " : "") +
                          (string.IsNullOrWhiteSpace(seg.Label) ? seg.Kind.ToString() : seg.Label);
                    Console.WriteLine(
                        $"  {name,-28} {seg.Kind,-16} {seg.FileSystem,-8} {ByteSizeFormatter.Format(seg.Size),10}");
                }

                Console.WriteLine();
            }

            return 0;
        }
        catch (Exception ex)
        {
            log.Error(ex.Message);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"""
            {AppInfo.ProductName} {AppInfo.Version}
            {AppInfo.Description}

            Usage:
              PartitionManager.exe
              PartitionManager.exe --list --no-ui
              PartitionManager.exe --help

            Options:
              --list --no-ui    Print disks and partitions to the console
              --no-elevate      Do not prompt for administrator elevation
              --help, -h, /?    Show this help
            """);
    }
}
