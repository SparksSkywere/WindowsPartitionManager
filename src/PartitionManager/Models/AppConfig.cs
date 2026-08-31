using System.Text.Json.Serialization;

namespace PartitionManager.Models;

public sealed class AppConfig
{
    [JsonPropertyName("general")]
    public GeneralSettings General { get; set; } = new();

    [JsonPropertyName("display")]
    public DisplaySettings Display { get; set; } = new();

    [JsonPropertyName("safety")]
    public SafetySettings Safety { get; set; } = new();
}

public sealed class GeneralSettings
{
    /// <summary>
    /// UI theme id: "system" (default, follow Windows light/dark),
    /// or Chronolog era themes: win95, win98, win2000, winxp, winvista, win7, win8, win10, win11, win11-dark.
    /// </summary>
    public string Theme { get; set; } = "system";

    public bool RefreshOnLaunch { get; set; } = true;
}

public sealed class DisplaySettings
{
    public bool ShowRemovable { get; set; } = true;
    public bool ShowVirtual { get; set; }
    public bool ShowUnallocated { get; set; } = true;
    public bool ShowSystemReserved { get; set; } = true;
}

public sealed class SafetySettings
{
    public bool ConfirmDestructive { get; set; } = true;
    public bool ProtectSystemPartitions { get; set; } = true;
}
