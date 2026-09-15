using PartitionManager.Helpers;
using PartitionManager.Models;

namespace PartitionManager.Services;

public readonly record struct PlannedSlice(CloneSlice Source, ulong DestOffset, ulong DestSize);

/// <summary>Maps source partitions onto a destination disk or region.</summary>
public static class CloneLayoutPlanner
{
    public const ulong Alignment = 1024UL * 1024UL;

    public static CloneSlice ToSlice(SegmentModel s) => new()
    {
        Offset = s.Offset,
        Size = s.Size,
        GptType = s.GptType,
        MbrType = s.MbrType,
        Kind = s.Kind,
        IsActive = s.IsActive,
        IsHidden = s.IsHidden,
        DriveLetter = s.DriveLetter,
        FileSystem = s.FileSystem,
        IsUnallocated = s.IsUnallocated
    };

    public static bool TryPlanDisk(CloneDiskParams p, out IReadOnlyList<PlannedSlice> plan, out string error)
    {
        plan = [];
        error = "";
        var parts = p.SourceSlices
            .Where(s => !s.IsUnallocated && s.Kind != SegmentKind.Extended)
            .OrderBy(s => s.Offset)
            .ToList();
        if (parts.Count == 0)
        {
            error = "Source disk has no partitions to clone.";
            return false;
        }

        var usable = UsableDestEnd(p.DestSize, p.Style);
        var expandIndex = p.ExpandLastPartition ? LastDataIndex(parts) : -1;
        var slices = new List<PlannedSlice>(parts.Count);

        if (p.AlignToMegabyte)
        {
            ulong cursor = Alignment;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var offset = ByteSizeFormatter.AlignUp(cursor, Alignment);
                var size = PlanSize(part.Size, i == expandIndex, offset, usable);
                if (offset + size > p.DestSize || size < MinKeep(part))
                {
                    error = "Destination disk is too small for the source partitions.";
                    return false;
                }

                slices.Add(new PlannedSlice(part, offset, size));
                cursor = offset + size;
            }
        }
        else
        {
            foreach (var part in parts)
            {
                if (part.Offset + part.Size > usable && expandIndex < 0)
                {
                    error = "Destination disk is too small for the source layout.";
                    return false;
                }

                slices.Add(new PlannedSlice(part, part.Offset, part.Size));
            }

            if (expandIndex >= 0)
            {
                var last = slices[expandIndex];
                var size = PlanSize(last.Source.Size, true, last.DestOffset, usable);
                if (size < last.Source.Size)
                {
                    error = "Destination disk is too small for the source partitions.";
                    return false;
                }

                slices[expandIndex] = last with { DestSize = size };
            }
        }

        plan = slices;
        return true;
    }

    public static bool TryPlanPartition(ClonePartitionParams p, out PlannedSlice plan, out string error)
    {
        plan = default;
        error = "";
        if (p.Source.IsUnallocated || p.Source.Size == 0)
        {
            error = "Source is not a partition.";
            return false;
        }

        var offset = p.AlignToMegabyte
            ? ByteSizeFormatter.AlignUp(p.DestOffset, Alignment)
            : p.DestOffset;
        if (offset < p.DestOffset)
            offset = p.DestOffset;

        var regionEnd = p.DestOffset + p.DestRegionSize;
        if (offset >= regionEnd)
        {
            error = "Destination region is too small after alignment.";
            return false;
        }

        var available = regionEnd - offset;
        if (p.AlignToMegabyte)
            available = ByteSizeFormatter.AlignDown(available, Alignment);

        var size = p.FillRegion ? available : p.Source.Size;
        if (p.AlignToMegabyte)
            size = ByteSizeFormatter.AlignDown(size, Alignment);
        if (size < p.Source.Size || size == 0)
        {
            error = "Destination region is smaller than the source partition.";
            return false;
        }

        plan = new PlannedSlice(p.Source, offset, size);
        return true;
    }

    public static string GptTypeFor(CloneSlice s)
    {
        if (!string.IsNullOrWhiteSpace(s.GptType))
            return s.GptType;
        return s.Kind switch
        {
            SegmentKind.Efi => "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}",
            SegmentKind.MicrosoftReserved => "{e3c9e316-0b5c-4db8-817d-f92df00215ae}",
            SegmentKind.Recovery => "{de94bba4-06d1-4d40-a16a-bfd50179d6ac}",
            _ => "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}"
        };
    }

    public static ushort MbrTypeFor(CloneSlice s) => s.MbrType != 0 ? s.MbrType : (ushort)7;

    public static bool NeedsCopy(CloneSlice s) =>
        !s.IsUnallocated && s.Kind is not (SegmentKind.MicrosoftReserved or SegmentKind.Extended);

    public static bool IsDataPartition(CloneSlice s) =>
        s.Kind is SegmentKind.Primary or SegmentKind.Logical;

    public static ulong LastAllocatedEnd(IEnumerable<CloneSlice> slices)
    {
        ulong end = Alignment;
        foreach (var s in slices.Where(x => !x.IsUnallocated))
            end = Math.Max(end, s.Offset + s.Size);
        return end;
    }

    private static ulong UsableDestEnd(ulong destSize, PartitionStyleKind style)
    {
        var reserve = style == PartitionStyleKind.Gpt ? Alignment : 0UL;
        return destSize > reserve ? destSize - reserve : destSize;
    }

    private static int LastDataIndex(IReadOnlyList<CloneSlice> parts)
    {
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            if (IsDataPartition(parts[i]))
                return i;
        }

        return -1;
    }

    private static ulong PlanSize(ulong sourceSize, bool expand, ulong offset, ulong usable)
    {
        if (!expand)
            return sourceSize;
        if (usable <= offset)
            return sourceSize;
        var grown = ByteSizeFormatter.AlignDown(usable - offset, Alignment);
        return grown > sourceSize ? grown : sourceSize;
    }

    private static ulong MinKeep(CloneSlice part) =>
        part.Kind == SegmentKind.MicrosoftReserved ? part.Size : Math.Min(part.Size, Alignment);
}
