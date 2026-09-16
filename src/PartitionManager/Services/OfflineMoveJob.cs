using PartitionManager.Models;

namespace PartitionManager.Services;

/// <summary>Persisted start-move for the Windows volume, applied in Windows Recovery.</summary>
public sealed class OfflineMoveJob
{
    public int DiskNumber { get; set; }
    public ulong OldOffset { get; set; }
    public ulong OldSize { get; set; }
    public ulong NewOffset { get; set; }
    public ulong NewSize { get; set; }
    public string GptType { get; set; } = "";
    public ushort MbrType { get; set; }
    public bool IsActive { get; set; }
    public bool IsHidden { get; set; }
    public char? DriveLetter { get; set; }
    public string FileSystem { get; set; } = "";
    public SegmentKind Kind { get; set; }
    public bool IsBoot { get; set; }
    public string Description { get; set; } = "";

    public static OfflineMoveJob From(PendingOperation op)
    {
        var r = op.Resize ?? new ResizePartitionParams();
        var size = r.NewSize < op.Size || op.Size == 0 ? r.NewSize : op.Size;
        if (size == 0)
            size = r.NewSize;
        return new OfflineMoveJob
        {
            DiskNumber = op.DiskNumber,
            OldOffset = op.Offset,
            OldSize = size,
            NewOffset = r.NewOffset,
            NewSize = r.NewSize,
            GptType = r.GptType,
            MbrType = r.MbrType,
            IsActive = r.IsActive,
            IsHidden = r.IsHidden,
            DriveLetter = r.DriveLetter,
            FileSystem = r.FileSystem,
            Kind = r.Kind,
            IsBoot = r.IsBoot,
            Description = op.Description
        };
    }

    public PendingOperation ToOperation() => new()
    {
        Kind = OperationKind.ResizePartition,
        DiskNumber = DiskNumber,
        Offset = OldOffset,
        Size = OldSize,
        Description = Description,
        IsDestructive = true,
        Resize = new ResizePartitionParams
        {
            NewSize = NewSize,
            NewOffset = NewOffset,
            ChangeOffset = true,
            GptType = GptType,
            MbrType = MbrType,
            IsActive = IsActive,
            IsHidden = IsHidden,
            DriveLetter = DriveLetter,
            FileSystem = FileSystem,
            Kind = Kind,
            IsBoot = IsBoot
        }
    };
}
