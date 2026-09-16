using System.Runtime.InteropServices;

namespace PartitionManager.Helpers;

/// <summary>Detects remote desktop sessions that cannot survive Windows Recovery.</summary>
internal static class SessionMode
{
    private const int SmRemoteSession = 0x1000;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public static bool IsRemoteDesktop()
    {
        try
        {
            if (GetSystemMetrics(SmRemoteSession) != 0)
                return true;

            var session = Environment.GetEnvironmentVariable("SESSIONNAME");
            if (!string.IsNullOrWhiteSpace(session) &&
                session.StartsWith("RDP-", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // treat as local if the check fails
        }

        return false;
    }
}
