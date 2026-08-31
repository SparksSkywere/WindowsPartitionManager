namespace PartitionManager.Models;

public enum PartitionStyleKind
{
    Unknown = 0,
    Mbr = 1,
    Gpt = 2
}

public enum SegmentKind
{
    Unallocated,
    Primary,
    Logical,
    Extended,
    Efi,
    Recovery,
    MicrosoftReserved,
    Oem,
    Unknown
}

public sealed class DiskLayout
{
    public List<DiskModel> Disks { get; set; } = [];

    public DiskLayout Clone()
    {
        return new DiskLayout
        {
            Disks = Disks.Select(d => d.Clone()).ToList()
        };
    }

    public DiskModel? FindDisk(int number) =>
        Disks.FirstOrDefault(d => d.Number == number);

    public SegmentModel? FindSegment(Guid id) =>
        Disks.SelectMany(d => d.Segments).FirstOrDefault(s => s.Id == id);
}

public sealed class DiskModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Number { get; set; }
    public string FriendlyName { get; set; } = "";
    public string Model { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string BusType { get; set; } = "";
    public ulong Size { get; set; }
    public ulong AllocatedSize { get; set; }
    public PartitionStyleKind PartitionStyle { get; set; }
    public bool IsBoot { get; set; }
    public bool IsSystem { get; set; }
    public bool IsOffline { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsRemovable { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsOptical { get; set; }
    public string Health { get; set; } = "Healthy";
    public string Status { get; set; } = "Online";
    public uint LogicalSectorSize { get; set; } = 512;
    public uint PhysicalSectorSize { get; set; } = 512;
    public string Location { get; set; } = "";
    public string UniqueId { get; set; } = "";
    public string FirmwareVersion { get; set; } = "";
    public List<SegmentModel> Segments { get; set; } = [];

    public string StyleText => PartitionStyle switch
    {
        PartitionStyleKind.Mbr => "MBR",
        PartitionStyleKind.Gpt => "GPT",
        _ => "Uninitialized"
    };

    public DiskModel Clone()
    {
        return new DiskModel
        {
            Id = Id,
            Number = Number,
            FriendlyName = FriendlyName,
            Model = Model,
            SerialNumber = SerialNumber,
            BusType = BusType,
            Size = Size,
            AllocatedSize = AllocatedSize,
            PartitionStyle = PartitionStyle,
            IsBoot = IsBoot,
            IsSystem = IsSystem,
            IsOffline = IsOffline,
            IsReadOnly = IsReadOnly,
            IsRemovable = IsRemovable,
            IsVirtual = IsVirtual,
            IsOptical = IsOptical,
            Health = Health,
            Status = Status,
            LogicalSectorSize = LogicalSectorSize,
            PhysicalSectorSize = PhysicalSectorSize,
            Location = Location,
            UniqueId = UniqueId,
            FirmwareVersion = FirmwareVersion,
            Segments = Segments.Select(s => s.Clone()).ToList()
        };
    }
}

public sealed class SegmentModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int DiskNumber { get; set; }
    public int PartitionNumber { get; set; }
    public bool IsUnallocated { get; set; }
    public ulong Offset { get; set; }
    public ulong Size { get; set; }
    public char? DriveLetter { get; set; }
    public string Label { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public ulong SizeRemaining { get; set; }
    public SegmentKind Kind { get; set; }
    public bool IsBoot { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public bool IsHidden { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsOffline { get; set; }
    public bool IsPending { get; set; }
    public string Status { get; set; } = "";
    public string GptType { get; set; } = "";
    public ushort MbrType { get; set; }
    public string VolumePath { get; set; } = "";
    public string AccessPaths { get; set; } = "";

    public ulong Used => Size >= SizeRemaining ? Size - SizeRemaining : 0;

    public double UsedPercent =>
        Size == 0 || IsUnallocated || string.IsNullOrEmpty(FileSystem)
            ? 0
            : Math.Clamp(Used * 100.0 / Size, 0, 100);

    public bool IsProtected => IsBoot || IsSystem;

    public SegmentModel Clone()
    {
        return new SegmentModel
        {
            Id = Id,
            DiskNumber = DiskNumber,
            PartitionNumber = PartitionNumber,
            IsUnallocated = IsUnallocated,
            Offset = Offset,
            Size = Size,
            DriveLetter = DriveLetter,
            Label = Label,
            FileSystem = FileSystem,
            SizeRemaining = SizeRemaining,
            Kind = Kind,
            IsBoot = IsBoot,
            IsSystem = IsSystem,
            IsActive = IsActive,
            IsHidden = IsHidden,
            IsReadOnly = IsReadOnly,
            IsOffline = IsOffline,
            IsPending = IsPending,
            Status = Status,
            GptType = GptType,
            MbrType = MbrType,
            VolumePath = VolumePath,
            AccessPaths = AccessPaths
        };
    }
}
