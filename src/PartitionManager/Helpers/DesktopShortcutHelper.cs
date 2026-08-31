using System.IO;
using System.Runtime.InteropServices;

namespace PartitionManager.Helpers;

/// <summary>Creates or removes the Partition Manager desktop shortcut.</summary>
public static class DesktopShortcutHelper
{
    public static string DesktopDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string ShortcutPath =>
        Path.Combine(DesktopDirectory, $"{AppInfo.ProductName}.lnk");

    public static bool ShortcutExists() => File.Exists(ShortcutPath);

    public static void CreateShortcut()
    {
        var target = Environment.ProcessPath
                     ?? Path.Combine(AppContext.BaseDirectory, AppInfo.ExeFileName);
        CreateShortcutFile(
            ShortcutPath,
            target,
            AppContext.BaseDirectory,
            AppInfo.Description,
            target);
    }

    public static bool RemoveShortcut()
    {
        var path = ShortcutPath;
        if (!File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }

    public static void CreateShortcutFile(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string description,
        string? iconPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("WScript.Shell COM is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)
                        ?? throw new InvalidOperationException("Could not create WScript.Shell.");
        var shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = description;
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            shortcut.IconLocation = iconPath;
        shortcut.Save();

        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }
}
