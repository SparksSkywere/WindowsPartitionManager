using System.Management;
using PartitionManager.Helpers;
using PartitionManager.Models;

namespace PartitionManager.Services;

/// <summary>
/// Reads disks, partitions, and volumes from the Windows Storage WMI namespace
/// (<c>root\Microsoft\Windows\Storage</c>), with a Win32 fallback.
/// </summary>
public sealed class DiskInventoryService
{
    public const ulong MinUnallocatedBytes = 8UL * 1024UL * 1024UL; // hide GPT header slivers
    public const string StorageNamespace = @"\\.\root\Microsoft\Windows\Storage";

    private static readonly string[] BusTypeNames =
    [
        "Unknown", "SCSI", "ATAPI", "ATA", "IEEE 1394", "SSA", "Fibre Channel",
        "USB", "RAID", "iSCSI", "SAS", "SATA", "SD", "MMC", "Virtual",
        "File-backed virtual", "Storage Spaces", "NVMe"
    ];

    private static readonly Dictionary<string, SegmentKind> GptKindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}"] = SegmentKind.Efi,
        ["{e3c9e316-0b5c-4db8-817d-f92df00215ae}"] = SegmentKind.MicrosoftReserved,
        ["{de94bba4-06d1-4d40-a16a-bfd50179d6ac}"] = SegmentKind.Recovery,
        ["{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}"] = SegmentKind.Primary,
        ["{e75caf8f-f680-4ced-8f3d-1d0b10b76c0d}"] = SegmentKind.Primary, // Linux filesystem
        ["{0fc63daf-8483-4772-8e79-3d69d8477de4}"] = SegmentKind.Primary,
        ["{21686148-6449-6e6f-744e-656564454649}"] = SegmentKind.Oem, // BIOS boot
    };

    private readonly LogService _log;

    public DiskInventoryService(LogService log)
    {
        _log = log;
    }

    public Task<DiskLayout> QueryAsync(DisplaySettings display, CancellationToken cancellationToken = default) =>
        Task.Run(() => Query(display), cancellationToken);

    public DiskLayout Query(DisplaySettings display)
    {
        try
        {
            return QueryStorage(display);
        }
        catch (Exception ex)
        {
            _log.Warn("Storage namespace query failed: " + ex.Message + " — falling back to Win32.");
            return QueryWin32Fallback(display);
        }
    }

    private DiskLayout QueryStorage(DisplaySettings display)
    {
        var scope = new ManagementScope(StorageNamespace);
        scope.Connect();

        var disks = new List<DiskModel>();
        using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_Disk")))
        {
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    var disk = ReadDisk(mo);
                    if (!display.ShowRemovable && disk.IsRemovable)
                        continue;
                    if (!display.ShowVirtual && disk.IsVirtual)
                        continue;
                    disks.Add(disk);
                }
            }
        }

        var partitions = new List<SegmentModel>();
        using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_Partition")))
        {
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                    partitions.Add(ReadPartition(mo));
            }
        }

        var volumes = ReadVolumes(scope);
        AttachVolumes(partitions, volumes);

        foreach (var disk in disks)
        {
            var parts = partitions
                .Where(p => p.DiskNumber == disk.Number)
                .OrderBy(p => p.Offset)
                .ToList();

            ClassifyMbrKinds(disk, parts);
            disk.Segments = BuildSegments(disk, parts, display);
        }

        if (display.ShowRemovable)
            disks.AddRange(QueryOpticalDrives());

        var layout = new DiskLayout
        {
            Disks = disks.OrderBy(d => d.IsOptical).ThenBy(d => d.Number).ToList()
        };

        _log.Info($"Inventory: {layout.Disks.Count} disk(s), {layout.Disks.Sum(d => d.Segments.Count(s => !s.IsUnallocated))} partition(s).");
        return layout;
    }

    private static DiskModel ReadDisk(ManagementBaseObject mo)
    {
        var bus = GetUInt16(mo, "BusType");
        var style = GetUInt16(mo, "PartitionStyle");
        var model = GetString(mo, "Model");
        var friendly = GetString(mo, "FriendlyName");
        var number = GetInt(mo, "Number");

        return new DiskModel
        {
            Number = number,
            FriendlyName = string.IsNullOrWhiteSpace(friendly) ? $"Disk {number}" : friendly,
            Model = string.IsNullOrWhiteSpace(model) ? friendly : model,
            SerialNumber = GetString(mo, "SerialNumber"),
            BusType = BusName(bus),
            Size = GetUInt64(mo, "Size"),
            AllocatedSize = GetUInt64(mo, "AllocatedSize"),
            PartitionStyle = style switch
            {
                1 => PartitionStyleKind.Mbr,
                2 => PartitionStyleKind.Gpt,
                _ => PartitionStyleKind.Unknown
            },
            IsBoot = GetBool(mo, "IsBoot"),
            IsSystem = GetBool(mo, "IsSystem"),
            IsOffline = GetBool(mo, "IsOffline"),
            IsReadOnly = GetBool(mo, "IsReadOnly"),
            IsRemovable = bus == 7 || bus == 12 || bus == 13,
            IsVirtual = bus is 14 or 15,
            Health = HealthName(GetUInt16(mo, "HealthStatus")),
            Status = GetBool(mo, "IsOffline") ? "Offline" : "Online",
            LogicalSectorSize = GetUInt32(mo, "LogicalSectorSize") == 0 ? 512 : GetUInt32(mo, "LogicalSectorSize"),
            PhysicalSectorSize = GetUInt32(mo, "PhysicalSectorSize"),
            Location = GetString(mo, "Location"),
            UniqueId = GetString(mo, "UniqueId"),
            FirmwareVersion = GetString(mo, "FirmwareVersion")
        };
    }

    private static SegmentModel ReadPartition(ManagementBaseObject mo)
    {
        var gpt = GetString(mo, "GptType");
        var mbr = GetUInt16(mo, "MbrType");
        var kind = ClassifyGpt(gpt, mbr);
        var letter = GetLetter(mo["DriveLetter"]);
        var access = GetStringArray(mo, "AccessPaths");
        var isHidden = GetBool(mo, "IsHidden");
        var isBoot = GetBool(mo, "IsBoot");
        var isSystem = GetBool(mo, "IsSystem");
        var isOffline = GetBool(mo, "IsOffline");

        var status = "Healthy";
        if (isHidden) status = "Hidden";
        if (isOffline) status = "Offline";
        if (isBoot) status = "Boot";
        if (isSystem && !isBoot) status = "System";

        return new SegmentModel
        {
            DiskNumber = GetInt(mo, "DiskNumber"),
            PartitionNumber = GetInt(mo, "PartitionNumber"),
            Offset = GetUInt64(mo, "Offset"),
            Size = GetUInt64(mo, "Size"),
            DriveLetter = letter,
            Kind = kind,
            IsBoot = isBoot,
            IsSystem = isSystem,
            IsActive = GetBool(mo, "IsActive"),
            IsHidden = isHidden,
            IsReadOnly = GetBool(mo, "IsReadOnly"),
            IsOffline = isOffline,
            Status = status,
            GptType = gpt,
            MbrType = mbr,
            AccessPaths = string.Join("; ", access),
            VolumePath = access.FirstOrDefault(a => a.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)) ?? ""
        };
    }

    private static List<VolumeInfo> ReadVolumes(ManagementScope scope)
    {
        var list = new List<VolumeInfo>();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_Volume"));
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                list.Add(new VolumeInfo(
                    GetString(mo, "Path"),
                    GetLetter(mo["DriveLetter"]),
                    GetString(mo, "FileSystemLabel"),
                    GetString(mo, "FileSystem"),
                    GetUInt64(mo, "Size"),
                    GetUInt64(mo, "SizeRemaining"),
                    GetUInt16(mo, "DriveType")));
            }
        }

        return list;
    }

    private static void AttachVolumes(List<SegmentModel> partitions, List<VolumeInfo> volumes)
    {
        foreach (var part in partitions)
        {
            VolumeInfo? match = null;
            if (part.DriveLetter is char letter)
                match = volumes.FirstOrDefault(v => v.DriveLetter == letter);

            if (match is null && !string.IsNullOrWhiteSpace(part.VolumePath))
            {
                match = volumes.FirstOrDefault(v =>
                    !string.IsNullOrWhiteSpace(v.Path) &&
                    part.VolumePath.StartsWith(v.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            }

            if (match is null && !string.IsNullOrWhiteSpace(part.AccessPaths))
            {
                match = volumes.FirstOrDefault(v =>
                    !string.IsNullOrWhiteSpace(v.Path) &&
                    part.AccessPaths.Contains(v.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            }

            if (match is null)
                continue;

            part.Label = match.Label;
            part.FileSystem = match.FileSystem;
            part.SizeRemaining = match.SizeRemaining;
            if (part.DriveLetter is null)
                part.DriveLetter = match.DriveLetter;
            if (string.IsNullOrWhiteSpace(part.VolumePath))
                part.VolumePath = match.Path;
        }
    }

    private static void ClassifyMbrKinds(DiskModel disk, List<SegmentModel> parts)
    {
        if (disk.PartitionStyle != PartitionStyleKind.Mbr)
            return;

        foreach (var p in parts)
        {
            if (p.Kind is SegmentKind.Efi or SegmentKind.Recovery or SegmentKind.MicrosoftReserved)
                continue;

            p.Kind = p.MbrType switch
            {
                5 or 15 => SegmentKind.Extended,
                _ => IsLogicalMbr(parts, p) ? SegmentKind.Logical : SegmentKind.Primary
            };
        }
    }

    private static bool IsLogicalMbr(List<SegmentModel> parts, SegmentModel candidate)
    {
        foreach (var ext in parts.Where(p => p.MbrType is 5 or 15))
        {
            var start = ext.Offset;
            var end = ext.Offset + ext.Size;
            if (candidate.Offset > start && candidate.Offset + candidate.Size <= end)
                return true;
        }

        return false;
    }

    private static List<SegmentModel> BuildSegments(DiskModel disk, List<SegmentModel> parts, DisplaySettings display)
    {
        var result = new List<SegmentModel>();
        ulong cursor = 0;
        var diskEnd = disk.Size;

        foreach (var part in parts)
        {
            if (!display.ShowSystemReserved &&
                part.Kind is SegmentKind.MicrosoftReserved or SegmentKind.Efi)
                continue;

            if (part.Offset > cursor + MinUnallocatedBytes && display.ShowUnallocated)
            {
                result.Add(Unallocated(disk.Number, cursor, part.Offset - cursor));
            }

            result.Add(part);
            var end = part.Offset + part.Size;
            if (end > cursor)
                cursor = end;
        }

        if (display.ShowUnallocated && diskEnd > cursor + MinUnallocatedBytes)
            result.Add(Unallocated(disk.Number, cursor, diskEnd - cursor));

        if (result.Count == 0 && display.ShowUnallocated && disk.Size > 0 &&
            disk.PartitionStyle == PartitionStyleKind.Unknown)
        {
            result.Add(Unallocated(disk.Number, 0, disk.Size));
        }

        return result.OrderBy(s => s.Offset).ToList();
    }

    private static SegmentModel Unallocated(int diskNumber, ulong offset, ulong size) =>
        new()
        {
            DiskNumber = diskNumber,
            PartitionNumber = 0,
            IsUnallocated = true,
            Offset = offset,
            Size = size,
            Kind = SegmentKind.Unallocated,
            Status = "Unallocated",
            SizeRemaining = size
        };

    private List<DiskModel> QueryOpticalDrives()
    {
        var disks = new List<DiskModel>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT DeviceID, Caption, Size, VolumeName, FileSystem, DriveType FROM Win32_LogicalDisk WHERE DriveType = 5");
            var n = 100;
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    var letter = GetString(mo, "DeviceID").TrimEnd(':').FirstOrDefault();
                    var size = GetUInt64(mo, "Size");
                    var caption = GetString(mo, "Caption");
                    var disk = new DiskModel
                    {
                        Number = n++,
                        FriendlyName = string.IsNullOrWhiteSpace(caption) ? "Optical drive" : caption,
                        Model = "Optical",
                        BusType = "ATAPI",
                        Size = size,
                        PartitionStyle = PartitionStyleKind.Unknown,
                        IsOptical = true,
                        IsRemovable = true,
                        Status = "Ready",
                        Health = "Healthy"
                    };
                    disk.Segments.Add(new SegmentModel
                    {
                        DiskNumber = disk.Number,
                        IsUnallocated = false,
                        Size = size,
                        DriveLetter = char.IsLetter(letter) ? letter : null,
                        Label = GetString(mo, "VolumeName"),
                        FileSystem = GetString(mo, "FileSystem"),
                        Kind = SegmentKind.Primary,
                        Status = "Optical",
                        SizeRemaining = 0
                    });
                    disks.Add(disk);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Optical drive query failed: " + ex.Message);
        }

        return disks;
    }

    private DiskLayout QueryWin32Fallback(DisplaySettings display)
    {
        var layout = new DiskLayout();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT Index, Caption, Size, InterfaceType, SerialNumber, Status, Partitions FROM Win32_DiskDrive");
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    var number = GetInt(mo, "Index");
                    var iface = GetString(mo, "InterfaceType");
                    var disk = new DiskModel
                    {
                        Number = number,
                        FriendlyName = GetString(mo, "Caption"),
                        Model = GetString(mo, "Caption"),
                        SerialNumber = GetString(mo, "SerialNumber"),
                        BusType = iface,
                        Size = GetUInt64(mo, "Size"),
                        PartitionStyle = PartitionStyleKind.Unknown,
                        IsRemovable = iface.Contains("USB", StringComparison.OrdinalIgnoreCase),
                        Status = GetString(mo, "Status")
                    };
                    if (!display.ShowRemovable && disk.IsRemovable)
                        continue;
                    layout.Disks.Add(disk);
                }
            }

            using var partSearcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT DiskIndex, Index, Size, StartingOffset, Type, Bootable, PrimaryPartition FROM Win32_DiskPartition");
            var parts = new List<SegmentModel>();
            foreach (ManagementObject mo in partSearcher.Get())
            {
                using (mo)
                {
                    parts.Add(new SegmentModel
                    {
                        DiskNumber = GetInt(mo, "DiskIndex"),
                        PartitionNumber = GetInt(mo, "Index"),
                        Size = GetUInt64(mo, "Size"),
                        Offset = GetUInt64(mo, "StartingOffset"),
                        Kind = GetBool(mo, "PrimaryPartition") ? SegmentKind.Primary : SegmentKind.Logical,
                        IsActive = GetBool(mo, "Bootable"),
                        Status = GetString(mo, "Type")
                    });
                }
            }

            foreach (var disk in layout.Disks)
            {
                var diskParts = parts.Where(p => p.DiskNumber == disk.Number).OrderBy(p => p.Offset).ToList();
                disk.Segments = BuildSegments(disk, diskParts, display);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Win32 disk query failed: " + ex.Message);
        }

        return layout;
    }

    public static IReadOnlyList<char> UsedDriveLetters(DiskLayout layout) =>
        layout.Disks.SelectMany(d => d.Segments)
            .Where(s => s.DriveLetter is not null)
            .Select(s => s.DriveLetter!.Value)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

    public static IReadOnlyList<char> AvailableDriveLetters(DiskLayout layout)
    {
        var used = new HashSet<char>(UsedDriveLetters(layout));
        var list = new List<char>();
        for (var c = 'A'; c <= 'Z'; c++)
        {
            if (!used.Contains(c))
                list.Add(c);
        }

        return list;
    }

    private static SegmentKind ClassifyGpt(string gpt, ushort mbr)
    {
        if (!string.IsNullOrWhiteSpace(gpt) && GptKindMap.TryGetValue(gpt.Trim(), out var kind))
            return kind;

        return mbr switch
        {
            5 or 15 => SegmentKind.Extended,
            0x12 or 0x27 => SegmentKind.Oem,
            _ => SegmentKind.Primary
        };
    }

    private static string BusName(ushort value) =>
        value < BusTypeNames.Length ? BusTypeNames[value] : $"Bus {value}";

    private static string HealthName(ushort value) => value switch
    {
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        _ => "Unknown"
    };

    private sealed record VolumeInfo(
        string Path,
        char? DriveLetter,
        string Label,
        string FileSystem,
        ulong Size,
        ulong SizeRemaining,
        ushort DriveType);

    internal static string GetString(ManagementBaseObject mo, string name)
    {
        try
        {
            return mo[name]?.ToString()?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    internal static string[] GetStringArray(ManagementBaseObject mo, string name)
    {
        try
        {
            return mo[name] is string[] arr ? arr : [];
        }
        catch
        {
            return [];
        }
    }

    internal static int GetInt(ManagementBaseObject mo, string name)
    {
        try
        {
            var v = mo[name];
            return v is null ? 0 : Convert.ToInt32(v);
        }
        catch
        {
            return 0;
        }
    }

    internal static ushort GetUInt16(ManagementBaseObject mo, string name)
    {
        try
        {
            var v = mo[name];
            return v is null ? (ushort)0 : Convert.ToUInt16(v);
        }
        catch
        {
            return 0;
        }
    }

    internal static uint GetUInt32(ManagementBaseObject mo, string name)
    {
        try
        {
            var v = mo[name];
            return v is null ? 0u : Convert.ToUInt32(v);
        }
        catch
        {
            return 0;
        }
    }

    internal static ulong GetUInt64(ManagementBaseObject mo, string name)
    {
        try
        {
            var v = mo[name];
            return v is null ? 0UL : Convert.ToUInt64(v);
        }
        catch
        {
            return 0;
        }
    }

    internal static bool GetBool(ManagementBaseObject mo, string name)
    {
        try
        {
            var v = mo[name];
            return v is not null && Convert.ToBoolean(v);
        }
        catch
        {
            return false;
        }
    }

    internal static char? GetLetter(object? value)
    {
        if (value is char c && char.IsLetter(c))
            return char.ToUpperInvariant(c);
        if (value is string s && s.Length > 0 && char.IsLetter(s[0]))
            return char.ToUpperInvariant(s[0]);
        return null;
    }
}
