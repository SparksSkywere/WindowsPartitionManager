using PartitionManager.Helpers;
using PartitionManager.Models;

namespace PartitionManager.Services;

/// <summary>Applies queued operations to a cloned layout so the disk map can preview them.</summary>
public static class LayoutPreview
{
    public const ulong Alignment = 1024UL * 1024UL;

    public static DiskLayout ApplyAll(DiskLayout live, IEnumerable<PendingOperation> operations)
    {
        var working = live.Clone();
        foreach (var op in operations)
            Apply(working, op);
        return working;
    }

    public static void Apply(DiskLayout layout, PendingOperation op)
    {
        switch (op.Kind)
        {
            case OperationKind.CreatePartition:
                ApplyCreate(layout, op);
                break;
            case OperationKind.DeletePartition:
                ApplyDelete(layout, op);
                break;
            case OperationKind.FormatPartition:
                ApplyFormat(layout, op);
                break;
            case OperationKind.ResizePartition:
                ApplyResize(layout, op);
                break;
            case OperationKind.ChangeDriveLetter:
                ApplyLetter(layout, op);
                break;
            case OperationKind.ChangeLabel:
                ApplyLabel(layout, op);
                break;
            case OperationKind.HidePartition:
            case OperationKind.UnhidePartition:
                ApplyHidden(layout, op, op.Kind == OperationKind.HidePartition);
                break;
            case OperationKind.SetActive:
                ApplyActive(layout, op);
                break;
            case OperationKind.InitializeDisk:
                ApplyInitialize(layout, op);
                break;
            case OperationKind.ConvertPartitionStyle:
                ApplyConvert(layout, op);
                break;
            case OperationKind.DeleteAllPartitions:
                ApplyDeleteAll(layout, op);
                break;
            case OperationKind.OfflineDisk:
            case OperationKind.OnlineDisk:
                ApplyOnline(layout, op, op.Kind == OperationKind.OnlineDisk);
                break;
            case OperationKind.CloneDisk:
                ApplyCloneDisk(layout, op);
                break;
            case OperationKind.ClonePartition:
                ApplyClonePartition(layout, op);
                break;
        }
    }

    private static void ApplyCreate(DiskLayout layout, PendingOperation op)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        var p = op.Create;
        if (disk is null || p is null)
            return;

        var gap = disk.Segments.FirstOrDefault(s => s.IsUnallocated && s.Offset == p.Offset)
                  ?? disk.Segments.FirstOrDefault(s => s.IsUnallocated &&
                                                       s.Offset <= p.Offset &&
                                                       s.Offset + s.Size >= p.Offset + p.Size);
        if (gap is null)
            return;

        var size = ByteSizeFormatter.AlignDown(p.Size, Alignment);
        if (size < Alignment)
            return;

        var created = new SegmentModel
        {
            Id = op.SegmentId ?? Guid.NewGuid(),
            DiskNumber = disk.Number,
            PartitionNumber = 0,
            Offset = gap.Offset,
            Size = size,
            DriveLetter = p.DriveLetter,
            Label = p.Label,
            FileSystem = p.FormatAfterCreate ? p.FileSystem : "",
            SizeRemaining = p.FormatAfterCreate ? size : 0,
            Kind = p.IsLogical ? SegmentKind.Logical : SegmentKind.Primary,
            Status = "Pending create",
            IsPending = true
        };

        var leftoverOffset = created.Offset + created.Size;
        var leftoverSize = gap.Offset + gap.Size > leftoverOffset
            ? gap.Offset + gap.Size - leftoverOffset
            : 0;

        var index = disk.Segments.IndexOf(gap);
        disk.Segments.RemoveAt(index);
        disk.Segments.Insert(index, created);
        if (leftoverSize >= DiskInventoryService.MinUnallocatedBytes)
        {
            disk.Segments.Insert(index + 1, new SegmentModel
            {
                DiskNumber = disk.Number,
                IsUnallocated = true,
                Offset = leftoverOffset,
                Size = leftoverSize,
                Kind = SegmentKind.Unallocated,
                Status = "Unallocated",
                SizeRemaining = leftoverSize
            });
        }

        disk.Segments = disk.Segments.OrderBy(s => s.Offset).ToList();
        MergeUnallocated(disk);
    }

    private static void ApplyDelete(DiskLayout layout, PendingOperation op)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        if (disk is null)
            return;

        var seg = FindTarget(disk, op);
        if (seg is null || seg.IsUnallocated)
            return;

        seg.IsUnallocated = true;
        seg.Kind = SegmentKind.Unallocated;
        seg.DriveLetter = null;
        seg.Label = "";
        seg.FileSystem = "";
        seg.SizeRemaining = seg.Size;
        seg.IsBoot = false;
        seg.IsSystem = false;
        seg.IsActive = false;
        seg.IsHidden = false;
        seg.Status = "Unallocated";
        seg.IsPending = true;
        seg.PartitionNumber = 0;
        MergeUnallocated(disk);
    }

    private static void ApplyFormat(DiskLayout layout, PendingOperation op)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        var f = op.Format;
        var seg = disk is null ? null : FindTarget(disk, op);
        if (seg is null || f is null)
            return;

        seg.FileSystem = f.FileSystem;
        if (!string.IsNullOrWhiteSpace(f.Label))
            seg.Label = f.Label;
        seg.SizeRemaining = seg.Size;
        seg.Status = "Pending format";
        seg.IsPending = true;
    }

    private static void ApplyResize(DiskLayout layout, PendingOperation op)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        var r = op.Resize;
        var seg = disk is null ? null : FindTarget(disk, op);
        if (seg is null || r is null || seg.IsUnallocated)
            return;

        var region = GetResizeRegion(disk!, seg);
        var newSize = ByteSizeFormatter.AlignDown(r.NewSize, Alignment);
        if (newSize < region.MinSize)
            newSize = region.MinSize;

        var newOffset = r.ChangeOffset
            ? ByteSizeFormatter.AlignDown(r.NewOffset, Alignment)
            : seg.Offset;
        if (newOffset < region.SpanStart)
            newOffset = region.SpanStart;
        if (newOffset + newSize > region.SpanEnd)
        {
            var fit = ByteSizeFormatter.AlignDown(region.SpanEnd - newOffset, Alignment);
            if (fit < region.MinSize)
                return;
            newSize = fit;
        }

        if (newOffset == seg.Offset && newSize == seg.Size)
            return;

        disk!.Segments.RemoveAll(s =>
            s.IsUnallocated &&
            s.Offset >= region.SpanStart &&
            s.Offset + s.Size <= region.SpanEnd);

        AddGap(disk, region.SpanStart, newOffset - region.SpanStart);
        seg.Offset = newOffset;
        seg.Size = newSize;
        if (seg.SizeRemaining > seg.Size)
            seg.SizeRemaining = seg.Size;
        AddGap(disk, newOffset + newSize, region.SpanEnd - (newOffset + newSize));

        seg.Status = r.ChangeOffset ? "Pending move" : "Pending resize";
        seg.IsPending = true;
        disk.Segments = disk.Segments.OrderBy(s => s.Offset).ToList();
        MergeUnallocated(disk);
    }

    private static void AddGap(DiskModel disk, ulong offset, ulong size)
    {
        if (size < Alignment)
            return;
        disk.Segments.Add(new SegmentModel
        {
            DiskNumber = disk.Number,
            IsUnallocated = true,
            Offset = offset,
            Size = size,
            Kind = SegmentKind.Unallocated,
            Status = "Unallocated",
            SizeRemaining = size,
            IsPending = true
        });
    }

    private static void ApplyLetter(DiskLayout layout, PendingOperation op)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        var seg = disk is null ? null : FindTarget(disk, op);
        if (seg is null)
            return;
        seg.DriveLetter = op.DriveLetter is char c && char.IsLetter(c) ? char.ToUpperInvariant(c) : null;
        seg.IsPending = true;
        seg.Status = "Pending letter";
    }

    private static void ApplyLabel(DiskLayout layout, PendingOperation op)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        var seg = disk is null ? null : FindTarget(disk, op);
        if (seg is null)
            return;
        seg.Label = op.Label ?? "";
        seg.IsPending = true;
        seg.Status = "Pending label";
    }

    private static void ApplyHidden(DiskLayout layout, PendingOperation op, bool hidden)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        var seg = disk is null ? null : FindTarget(disk, op);
        if (seg is null)
            return;
        seg.IsHidden = hidden;
        if (hidden)
            seg.DriveLetter = null;
        seg.Status = hidden ? "Hidden" : "Healthy";
        seg.IsPending = true;
    }

    private static void ApplyActive(DiskLayout layout, PendingOperation op)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        if (disk is null)
            return;
        foreach (var s in disk.Segments)
            s.IsActive = false;
        var seg = FindTarget(disk, op);
        if (seg is null)
            return;
        seg.IsActive = true;
        seg.IsPending = true;
        seg.Status = "Active";
    }

    private static void ApplyInitialize(DiskLayout layout, PendingOperation op)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        if (disk is null || op.TargetStyle is null)
            return;
        disk.PartitionStyle = op.TargetStyle.Value;
        disk.Status = "Pending initialize";
        if (disk.Segments.Count == 0)
        {
            disk.Segments.Add(new SegmentModel
            {
                DiskNumber = disk.Number,
                IsUnallocated = true,
                Offset = Alignment,
                Size = disk.Size > Alignment * 2 ? disk.Size - Alignment * 2 : disk.Size,
                Kind = SegmentKind.Unallocated,
                Status = "Unallocated",
                SizeRemaining = disk.Size,
                IsPending = true
            });
        }
    }

    private static void ApplyConvert(DiskLayout layout, PendingOperation op)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        if (disk is null || op.TargetStyle is null)
            return;
        if (disk.Segments.Any(s => !s.IsUnallocated))
            return;
        disk.PartitionStyle = op.TargetStyle.Value;
        disk.Status = "Pending convert";
    }

    private static void ApplyDeleteAll(DiskLayout layout, PendingOperation op)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        if (disk is null)
            return;
        disk.Segments =
        [
            new SegmentModel
            {
                DiskNumber = disk.Number,
                IsUnallocated = true,
                Offset = 0,
                Size = disk.Size,
                Kind = SegmentKind.Unallocated,
                Status = "Unallocated",
                SizeRemaining = disk.Size,
                IsPending = true
            }
        ];
    }

    private static void ApplyCloneDisk(DiskLayout layout, PendingOperation op)
    {
        var p = op.CloneDisk;
        var dest = p is null ? null : layout.FindDisk(p.DestDisk);
        if (p is null || dest is null || !CloneLayoutPlanner.TryPlanDisk(p, out var plan, out _))
            return;

        dest.PartitionStyle = p.Style;
        dest.Status = "Pending clone";
        dest.IsOffline = false;
        dest.Segments = [];
        foreach (var slice in plan)
            dest.Segments.Add(PendingSegment(dest.Number, slice));

        FillGaps(dest);
        dest.AllocatedSize = dest.Segments.Where(s => !s.IsUnallocated).Aggregate(0UL, (n, s) => n + s.Size);
    }

    private static void ApplyClonePartition(DiskLayout layout, PendingOperation op)
    {
        var p = op.ClonePartition;
        var dest = p is null ? null : layout.FindDisk(p.DestDisk);
        if (p is null || dest is null || !CloneLayoutPlanner.TryPlanPartition(p, out var planned, out _))
            return;

        var gap = dest.Segments.FirstOrDefault(s =>
                      s.IsUnallocated && s.Offset <= planned.DestOffset &&
                      s.Offset + s.Size >= planned.DestOffset + planned.DestSize)
                  ?? dest.Segments.FirstOrDefault(s => s.IsUnallocated && s.Offset == p.DestOffset);
        if (gap is null)
            return;

        var created = PendingSegment(dest.Number, planned);
        var index = dest.Segments.IndexOf(gap);
        dest.Segments.RemoveAt(index);

        if (planned.DestOffset > gap.Offset &&
            planned.DestOffset - gap.Offset >= DiskInventoryService.MinUnallocatedBytes)
        {
            dest.Segments.Insert(index++, new SegmentModel
            {
                DiskNumber = dest.Number,
                IsUnallocated = true,
                Offset = gap.Offset,
                Size = planned.DestOffset - gap.Offset,
                Kind = SegmentKind.Unallocated,
                Status = "Unallocated",
                SizeRemaining = planned.DestOffset - gap.Offset,
                IsPending = true
            });
        }

        dest.Segments.Insert(index++, created);
        var leftoverOffset = planned.DestOffset + planned.DestSize;
        var gapEnd = gap.Offset + gap.Size;
        if (gapEnd > leftoverOffset && gapEnd - leftoverOffset >= DiskInventoryService.MinUnallocatedBytes)
        {
            dest.Segments.Insert(index, new SegmentModel
            {
                DiskNumber = dest.Number,
                IsUnallocated = true,
                Offset = leftoverOffset,
                Size = gapEnd - leftoverOffset,
                Kind = SegmentKind.Unallocated,
                Status = "Unallocated",
                SizeRemaining = gapEnd - leftoverOffset,
                IsPending = true
            });
        }

        dest.Segments = dest.Segments.OrderBy(s => s.Offset).ToList();
        MergeUnallocated(dest);
    }

    private static SegmentModel PendingSegment(int diskNumber, PlannedSlice slice) => new()
    {
        DiskNumber = diskNumber,
        Offset = slice.DestOffset,
        Size = slice.DestSize,
        FileSystem = slice.Source.FileSystem,
        SizeRemaining = slice.DestSize,
        Kind = slice.Source.Kind,
        IsActive = slice.Source.IsActive,
        IsHidden = slice.Source.IsHidden,
        Status = "Pending clone",
        IsPending = true,
        GptType = slice.Source.GptType,
        MbrType = slice.Source.MbrType
    };

    private static void FillGaps(DiskModel disk)
    {
        var ordered = disk.Segments.OrderBy(s => s.Offset).ToList();
        var filled = new List<SegmentModel>();
        ulong cursor = 0;
        foreach (var seg in ordered)
        {
            if (seg.Offset > cursor + DiskInventoryService.MinUnallocatedBytes)
            {
                filled.Add(new SegmentModel
                {
                    DiskNumber = disk.Number,
                    IsUnallocated = true,
                    Offset = cursor,
                    Size = seg.Offset - cursor,
                    Kind = SegmentKind.Unallocated,
                    Status = "Unallocated",
                    SizeRemaining = seg.Offset - cursor,
                    IsPending = true
                });
            }

            filled.Add(seg);
            cursor = seg.Offset + seg.Size;
        }

        if (disk.Size > cursor + DiskInventoryService.MinUnallocatedBytes)
        {
            filled.Add(new SegmentModel
            {
                DiskNumber = disk.Number,
                IsUnallocated = true,
                Offset = cursor,
                Size = disk.Size - cursor,
                Kind = SegmentKind.Unallocated,
                Status = "Unallocated",
                SizeRemaining = disk.Size - cursor,
                IsPending = true
            });
        }

        disk.Segments = filled;
    }

    private static void ApplyOnline(DiskLayout layout, PendingOperation op, bool online)
    {
        var disk = layout.FindDisk(op.DiskNumber);
        if (disk is null)
            return;
        disk.IsOffline = !online;
        disk.Status = online ? "Online" : "Offline";
    }

    private static SegmentModel? FindTarget(DiskModel disk, PendingOperation op)
    {
        if (op.SegmentId is Guid id)
        {
            var byId = disk.Segments.FirstOrDefault(s => s.Id == id);
            if (byId is not null)
                return byId;
        }

        return disk.Segments.FirstOrDefault(s => !s.IsUnallocated && s.Offset == op.Offset)
               ?? disk.Segments.FirstOrDefault(s => s.Offset == op.Offset);
    }

    public static void MergeUnallocated(DiskModel disk)
    {
        var ordered = disk.Segments.OrderBy(s => s.Offset).ToList();
        var merged = new List<SegmentModel>();
        foreach (var seg in ordered)
        {
            if (merged.Count > 0 && merged[^1].IsUnallocated && seg.IsUnallocated)
            {
                merged[^1].Size += seg.Size;
                merged[^1].SizeRemaining = merged[^1].Size;
                continue;
            }

            merged.Add(seg);
        }

        disk.Segments = merged;
    }

    public readonly record struct ResizeRegion(
        ulong SpanStart,
        ulong SpanEnd,
        ulong MinSize,
        ulong Offset,
        ulong Size)
    {
        public ulong SpanLength => SpanEnd > SpanStart ? SpanEnd - SpanStart : 0;
    }

    public static ResizeRegion GetResizeRegion(DiskModel disk, SegmentModel segment)
    {
        var before = disk.Segments
            .Where(s => s.IsUnallocated && s.Offset + s.Size == segment.Offset)
            .Sum(s => (decimal)s.Size);
        var after = disk.Segments
            .Where(s => s.IsUnallocated && s.Offset == segment.Offset + segment.Size)
            .Sum(s => (decimal)s.Size);

        var spanStart = segment.Offset - (ulong)before;
        var spanEnd = segment.Offset + segment.Size + (ulong)after;
        var used = string.IsNullOrEmpty(segment.FileSystem) ? 0UL : segment.Used;
        var min = Math.Max(Alignment, ByteSizeFormatter.AlignUp(used == 0 ? Alignment : used, Alignment));
        if (spanEnd > spanStart && min > spanEnd - spanStart)
            min = Alignment;
        return new ResizeRegion(spanStart, spanEnd, min, segment.Offset, segment.Size);
    }

    public static (ulong Min, ulong Max) ResizeBounds(DiskModel disk, SegmentModel segment)
    {
        var region = GetResizeRegion(disk, segment);
        return (region.MinSize, region.SpanLength);
    }
}
