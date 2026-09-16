namespace PartitionManager.Models;

public enum OperationKind
{
    CreatePartition,
    DeletePartition,
    FormatPartition,
    ResizePartition,
    ChangeDriveLetter,
    ChangeLabel,
    HidePartition,
    UnhidePartition,
    SetActive,
    InitializeDisk,
    ConvertPartitionStyle,
    DeleteAllPartitions,
    OfflineDisk,
    OnlineDisk,
    CloneDisk,
    ClonePartition
}

public sealed class PendingOperation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public OperationKind Kind { get; init; }
    public int DiskNumber { get; init; }
    public Guid? SegmentId { get; init; }
    public ulong Offset { get; init; }
    public ulong Size { get; init; }
    public string Description { get; init; } = "";
    public bool IsDestructive { get; init; }
    public CreatePartitionParams? Create { get; init; }
    public FormatPartitionParams? Format { get; init; }
    public ResizePartitionParams? Resize { get; init; }
    public char? DriveLetter { get; init; }
    public string? Label { get; init; }
    public PartitionStyleKind? TargetStyle { get; init; }
    public CloneDiskParams? CloneDisk { get; init; }
    public ClonePartitionParams? ClonePartition { get; init; }
}

public sealed class CreatePartitionParams
{
    public ulong Offset { get; init; }
    public ulong Size { get; init; }
    public char? DriveLetter { get; init; }
    public string Label { get; init; } = "";
    public string FileSystem { get; init; } = "NTFS";
    public bool FormatAfterCreate { get; init; } = true;
    public bool QuickFormat { get; init; } = true;
    public uint ClusterSize { get; init; }
    public bool IsLogical { get; init; }
}

public sealed class FormatPartitionParams
{
    public string FileSystem { get; init; } = "NTFS";
    public string Label { get; init; } = "";
    public bool QuickFormat { get; init; } = true;
    public uint ClusterSize { get; init; }
}

public sealed class ResizePartitionParams
{
    public ulong NewSize { get; init; }
    public ulong NewOffset { get; init; }
    public bool ChangeOffset { get; init; }
    public string GptType { get; init; } = "";
    public ushort MbrType { get; init; }
    public bool IsActive { get; init; }
    public bool IsHidden { get; init; }
    public char? DriveLetter { get; init; }
    public string FileSystem { get; init; } = "";
    public SegmentKind Kind { get; init; }
    public bool IsBoot { get; init; }
}

public enum CloneCopyMode
{
    UsedData,
    AllSectors
}

public sealed class CloneSlice
{
    public ulong Offset { get; init; }
    public ulong Size { get; init; }
    public string GptType { get; init; } = "";
    public ushort MbrType { get; init; }
    public SegmentKind Kind { get; init; }
    public bool IsActive { get; init; }
    public bool IsHidden { get; init; }
    public char? DriveLetter { get; init; }
    public string FileSystem { get; init; } = "";
    public bool IsUnallocated { get; init; }
}

public sealed class CloneDiskParams
{
    public int SourceDisk { get; init; }
    public int DestDisk { get; init; }
    public CloneCopyMode Mode { get; init; }
    public bool AlignToMegabyte { get; init; } = true;
    public bool ExpandLastPartition { get; init; }
    public bool SourceIsBoot { get; init; }
    public PartitionStyleKind Style { get; init; }
    public ulong DestSize { get; init; }
    public IReadOnlyList<CloneSlice> SourceSlices { get; init; } = [];
}

public sealed class ClonePartitionParams
{
    public int SourceDisk { get; init; }
    public int DestDisk { get; init; }
    public CloneSlice Source { get; init; } = new();
    public ulong DestOffset { get; init; }
    public ulong DestRegionSize { get; init; }
    public CloneCopyMode Mode { get; init; }
    public bool AlignToMegabyte { get; init; } = true;
    public bool FillRegion { get; init; }
}

public sealed class OperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public uint ReturnCode { get; init; }
    public bool RestartRequired { get; init; }
    public bool UnattendedReady { get; init; }
}
