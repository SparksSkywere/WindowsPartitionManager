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
    OnlineDisk
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
}

public sealed class OperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public uint ReturnCode { get; init; }
}
