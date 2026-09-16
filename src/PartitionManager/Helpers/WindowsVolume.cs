using System.IO;
using Microsoft.Win32;

namespace PartitionManager.Helpers;

/// <summary>Detects the live Windows boot volume, which cannot be moved while the OS is running.</summary>
internal static class WindowsVolume
{
    public static char? SystemDriveLetter
    {
        get
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root))
                return null;
            return char.ToUpperInvariant(root[0]);
        }
    }

    /// <summary>True when running inside Windows Recovery / preinstallation (MiniNT).</summary>
    public static bool IsWindowsPe()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsOnlineSystemVolume(char? driveLetter, bool isBoot)
    {
        if (IsWindowsPe())
            return false;

        var sys = SystemDriveLetter;
        if (sys is char s && driveLetter is char letter && char.ToUpperInvariant(letter) == s)
            return true;

        return isBoot && sys is char boot &&
               (driveLetter is null || char.ToUpperInvariant(driveLetter.Value) == boot);
    }

    public static string MoveRestartHint =>
        "Moving the Windows volume needs a restart. Shrink or extend from the end can run now.";

    public static string MoveBlockedLiveMessage =>
        "Cannot move the Windows volume while it is in use. Shrink or extend from the end, or Apply and restart.";

    public static string RemoteRecoveryBlockedMessage =>
        "Cannot finish this move from remote desktop. Use this PC locally.";
}
