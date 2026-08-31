using System.Reflection;

namespace PartitionManager;

/// <summary>Product branding and version metadata for Partition Manager.</summary>
public static class AppInfo
{
    public const string ProductName = "Partition Manager";
    public const string ProductNameShort = "Partition Manager";
    public const string Company = "Skywere Industries";
    public const string Copyright = "Copyright © Skywere Industries";
    public const string Description =
        "Manage disks and partitions on Windows. Create, delete, format, and resize volumes, " +
        "change drive letters and labels, initialize MBR or GPT disks, and queue changes " +
        "until you click Apply — with a live disk map preview.";

    public const string ExeFileName = "PartitionManager.exe";
    public const string InstallFolderName = "PartitionManager";
    public const string UninstallRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PartitionManager";
    public const string AppDataFolderName = "PartitionManager";
    public const string CompanyFolderName = "Skywere Industries";

    /// <summary>Public repo for feedback, issues, and releases.</summary>
    public const string GitHubOwner = "SparksSkywere";
    public const string GitHubRepo = "PartitionManager";
    public const string GitHubUrl = "https://github.com/SparksSkywere/PartitionManager";
    public const string GitHubIssuesUrl = "https://github.com/SparksSkywere/PartitionManager/issues";
    public const string GitHubReleasesUrl = "https://github.com/SparksSkywere/PartitionManager/releases";

    public static string Version
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');
                return plus > 0 ? info[..plus] : info;
            }

            return asm.GetName().Version?.ToString(3) ?? "1.0.0";
        }
    }

    public static string AboutText =>
        $"{ProductName}\n" +
        $"Version {Version}\n\n" +
        $"{Description}\n\n" +
        $"{Copyright}\n" +
        $"Created by {Company}";
}
