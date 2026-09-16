using System.Diagnostics;
using System.Management;

namespace PartitionManager.Services;

/// <summary>
/// Detects Windows device encryption / volume encryption (including the Windows volume).
/// </summary>
internal static class VolumeEncryption
{
    public const string WmiNamespace = @"\\.\root\cimv2\Security\MicrosoftVolumeEncryption";

    public sealed class State
    {
        public bool ProtectionOn { get; init; }
        public bool Decrypting { get; init; }
        public bool Encrypting { get; init; }
        public bool BlocksLayoutChange => ProtectionOn || Decrypting || Encrypting;
    }

    public static Dictionary<char, State> Query()
    {
        var map = new Dictionary<char, State>();
        try
        {
            var scope = new ManagementScope(WmiNamespace);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_EncryptableVolume"));
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    var letter = DriveLetterOf(mo);
                    if (letter is null)
                        continue;
                    map[letter.Value] = ReadState(mo);
                }
            }
        }
        catch
        {
            // namespace missing on some editions
        }

        return map;
    }

    public static bool IsProtected(char driveLetter)
    {
        var key = char.ToUpperInvariant(driveLetter);
        if (Query().TryGetValue(key, out var state) && state.BlocksLayoutChange)
            return true;
        return ManageBdeProtected(key);
    }

    public static void OpenSettings()
    {
        if (TryStart("ms-settings:deviceencryption", null))
            return;
        TryStart("control.exe", "/name Microsoft.BitLockerDriveEncryption");
    }

    public static void ApplyTo(IEnumerable<Models.SegmentModel> partitions)
    {
        Dictionary<char, State> map;
        try { map = Query(); }
        catch { return; }

        foreach (var part in partitions)
        {
            if (part.DriveLetter is not char c)
                continue;
            if (!map.TryGetValue(char.ToUpperInvariant(c), out var state) || !state.BlocksLayoutChange)
                continue;
            part.IsEncrypted = true;
            part.Status = state.Decrypting ? "Decrypting" : state.Encrypting ? "Encrypting" : "Encrypted";
        }
    }

    private static State ReadState(ManagementObject mo)
    {
        var protection = ToUInt(mo["ProtectionStatus"]);
        var conversion = TryConversionStatus(mo);
        return new State
        {
            ProtectionOn = protection == 1,
            Encrypting = conversion is 2 or 4,
            Decrypting = conversion is 3 or 5
        };
    }

    private static uint? TryConversionStatus(ManagementObject mo)
    {
        try
        {
            var result = mo.InvokeMethod("GetConversionStatus", null, null);
            if (result is null)
                return null;
            return ToUInt(result["ConversionStatus"]);
        }
        catch
        {
            return null;
        }
    }

    private static char? DriveLetterOf(ManagementBaseObject mo)
    {
        var raw = mo["DriveLetter"]?.ToString()?.Trim();
        if (!string.IsNullOrWhiteSpace(raw) && char.IsLetter(raw[0]))
            return char.ToUpperInvariant(raw[0]);
        return null;
    }

    private static uint ToUInt(object? value) =>
        value is null ? 0 : Convert.ToUInt32(value);

    private static bool ManageBdeProtected(char letter)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "manage-bde.exe",
                Arguments = "-status " + letter + ":",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
                return false;
            var text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(15_000);
            return text.Contains("Protection On", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryStart(string fileName, string? arguments)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = fileName, UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(arguments))
                psi.Arguments = arguments;
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
