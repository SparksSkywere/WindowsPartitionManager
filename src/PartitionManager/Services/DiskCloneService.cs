using Microsoft.Win32.SafeHandles;
using PartitionManager.Helpers;
using PartitionManager.Models;

namespace PartitionManager.Services;

/// <summary>Copies disks and partitions using raw I/O. Used-data mode skips free clusters.</summary>
public sealed class DiskCloneService
{
    private const int BlockSize = 4 * 1024 * 1024;
    private const int IoAlign = 4096;

    private readonly LogService _log;
    private readonly PartitionOperationExecutor _storage;

    public DiskCloneService(LogService log, PartitionOperationExecutor storage)
    {
        _log = log;
        _storage = storage;
    }

    public OperationResult CloneDisk(CloneDiskParams p, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (p.SourceDisk == p.DestDisk)
            return Fail("Source and destination must be different disks.");
        if (!CloneLayoutPlanner.TryPlanDisk(p, out var plan, out var error))
            return Fail(error);

        var lastEnd = CloneLayoutPlanner.LastAllocatedEnd(p.SourceSlices);
        var rawWhole = p.Mode == CloneCopyMode.AllSectors && !p.AlignToMegabyte && p.DestSize >= lastEnd;

        _log.Info($"Clone disk {p.SourceDisk} → {p.DestDisk} ({p.Mode}).");
        var online = _storage.SetDiskOnline(p.DestDisk, true);
        if (!online.Success)
            return online;

        if (rawWhole)
            return CloneDiskRaw(p, plan, lastEnd, progress, cancellationToken);

        return CloneDiskByPartition(p, plan, progress, cancellationToken);
    }

    public OperationResult ClonePartition(ClonePartitionParams p, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (!CloneLayoutPlanner.TryPlanPartition(p, out var planned, out var error))
            return Fail(error);

        _log.Info($"Clone partition on disk {p.SourceDisk} → disk {p.DestDisk}.");
        var created = _storage.CreatePartitionRaw(
            p.DestDisk,
            planned.DestOffset,
            planned.Source.Size,
            CloneLayoutPlanner.GptTypeFor(planned.Source),
            CloneLayoutPlanner.MbrTypeFor(planned.Source));
        if (!created.Success)
            return created;

        Thread.Sleep(800);
        TryQuietDest(p.DestDisk);

        var copied = CopySlice(p.SourceDisk, p.DestDisk, planned, p.Mode, progress, 0, planned.Source.Size, cancellationToken);
        if (!copied.Success)
        {
            _storage.SetDiskOnline(p.DestDisk, true);
            return copied;
        }

        var online = _storage.SetDiskOnline(p.DestDisk, true);
        if (!online.Success)
            return online;

        if (planned.DestSize > planned.Source.Size)
        {
            var grown = _storage.ResizePartition(p.DestDisk, planned.DestOffset, planned.DestSize);
            if (!grown.Success)
                _log.Info("Copy finished; extend of the destination partition failed: " + grown.Message);
        }

        ApplyFlags(p.DestDisk, planned);
        progress?.Report(100);
        return Ok("Partition clone finished.");
    }

    public OperationResult MovePartition(PendingOperation op, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var r = op.Resize ?? throw new InvalidOperationException("Resize parameters missing.");
        var disk = op.DiskNumber;
        var oldOffset = op.Offset;
        var oldSize = op.Size;
        var newOffset = r.NewOffset;
        var newSize = r.NewSize;
        if (oldSize == 0)
            oldSize = newSize;

        var copySize = Math.Min(oldSize, newSize);
        copySize = copySize / (ulong)IoAlign * (ulong)IoAlign;
        if (copySize == 0)
            return Fail("Partition is too small to move.");

        if (WindowsVolume.IsOnlineSystemVolume(r.DriveLetter, r.IsBoot))
            return Fail(WindowsVolume.MoveBlockedLiveMessage);

        _log.Info($"Move partition on disk {disk}: offset {oldOffset} → {newOffset}, size {oldSize} → {newSize}.");

        SafeFileHandle? volume = null;
        try
        {
            if (r.DriveLetter is char volumeLetter)
            {
                try
                {
                    volume = NativeDisk.OpenVolume(volumeLetter, write: true);
                    if (!NativeDisk.TryLock(volume))
                    {
                        NativeDisk.TryDismount(volume);
                        if (!NativeDisk.TryLock(volume))
                            return Fail($"Cannot lock {volumeLetter}:. Close programs using that drive, then Apply again.");
                    }

                    NativeDisk.TryDismount(volume);
                }
                catch (Exception ex)
                {
                    return Fail($"Cannot lock {volumeLetter}: {ex.Message.TrimEnd('.')}.");
                }
            }

            using var handle = NativeDisk.OpenPhysical(disk, write: true);
            CopyRange(
                handle,
                handle,
                sameDisk: true,
                (long)oldOffset,
                (long)newOffset,
                (long)copySize,
                progress,
                0,
                copySize,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Fail("Access denied while moving this partition.");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
        finally
        {
            if (volume is not null)
            {
                NativeDisk.TryUnlock(volume);
                volume.Dispose();
            }
        }

        var deleted = _storage.DeletePartitionAt(disk, oldOffset);
        if (!deleted.Success)
            return deleted;

        Thread.Sleep(500);

        var slice = new CloneSlice
        {
            Offset = oldOffset,
            Size = oldSize,
            GptType = r.GptType,
            MbrType = r.MbrType,
            Kind = r.Kind,
            IsActive = r.IsActive,
            IsHidden = r.IsHidden,
            DriveLetter = r.DriveLetter,
            FileSystem = r.FileSystem
        };
        var created = _storage.CreatePartitionRaw(
            disk,
            newOffset,
            copySize,
            CloneLayoutPlanner.GptTypeFor(slice),
            CloneLayoutPlanner.MbrTypeFor(slice));
        if (!created.Success)
            return created;

        Thread.Sleep(800);

        if (newSize > copySize)
        {
            var grown = _storage.ResizePartition(disk, newOffset, newSize);
            if (!grown.Success)
                _log.Info("Move finished; extend of the partition failed: " + grown.Message);
        }

        if (r.IsActive)
            _storage.SetActive(disk, newOffset);
        if (r.IsHidden)
            _storage.SetHidden(disk, newOffset, true);
        if (r.DriveLetter is char restoredLetter)
        {
            var lettered = _storage.AssignDriveLetter(disk, newOffset, restoredLetter);
            if (!lettered.Success)
                _log.Info("Move finished; drive letter could not be restored: " + lettered.Message);
        }

        progress?.Report(100);
        return Ok("Partition move finished.");
    }

    private OperationResult CloneDiskRaw(
        CloneDiskParams p,
        IReadOnlyList<PlannedSlice> plan,
        ulong lastEnd,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var offline = _storage.SetDiskOnline(p.DestDisk, false);
        if (!offline.Success)
            _log.Info("Could not take the destination offline; writing while online.");

        try
        {
            using var src = NativeDisk.OpenPhysical(p.SourceDisk, write: false);
            using var dest = NativeDisk.OpenPhysical(p.DestDisk, write: true);
            var copyBytes = AlignCopyLength(lastEnd, p.DestSize);
            CopyRange(src, dest, sameDisk: false, 0, 0, (long)copyBytes, progress, 0, copyBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            _storage.SetDiskOnline(p.DestDisk, true);
            return Fail(ex.Message);
        }

        var back = _storage.SetDiskOnline(p.DestDisk, true);
        if (!back.Success)
            return back;

        var expand = plan.FirstOrDefault(s => s.DestSize > s.Source.Size);
        if (expand.Source is not null && expand.DestSize > expand.Source.Size)
        {
            var grown = _storage.ResizePartition(p.DestDisk, expand.DestOffset, expand.DestSize);
            if (!grown.Success)
                _log.Info("Sector copy finished; extend of the last data partition failed: " + grown.Message);
        }

        progress?.Report(100);
        return Ok("Disk clone finished.");
    }

    private OperationResult CloneDiskByPartition(
        CloneDiskParams p,
        IReadOnlyList<PlannedSlice> plan,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var cleared = _storage.ClearDisk(p.DestDisk);
        if (!cleared.Success)
            _log.Info("Clear skipped: " + cleared.Message);

        var initialized = _storage.InitializeDisk(p.DestDisk, p.Style);
        if (!initialized.Success)
            return initialized;

        foreach (var slice in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var created = _storage.CreatePartitionRaw(
                p.DestDisk,
                slice.DestOffset,
                slice.Source.Size,
                CloneLayoutPlanner.GptTypeFor(slice.Source),
                CloneLayoutPlanner.MbrTypeFor(slice.Source));
            if (!created.Success)
                return created;
        }

        Thread.Sleep(800);
        TryQuietDest(p.DestDisk);

        var copyTotal = plan.Where(s => CloneLayoutPlanner.NeedsCopy(s.Source)).Sum(s => (decimal)s.Source.Size);
        var copyDone = 0UL;
        foreach (var slice in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CloneLayoutPlanner.NeedsCopy(slice.Source))
                continue;

            var copied = CopySlice(p.SourceDisk, p.DestDisk, slice, p.Mode, progress, copyDone, (ulong)copyTotal, cancellationToken);
            if (!copied.Success)
            {
                _storage.SetDiskOnline(p.DestDisk, true);
                return copied;
            }

            copyDone += slice.Source.Size;
        }

        var back = _storage.SetDiskOnline(p.DestDisk, true);
        if (!back.Success)
            return back;

        foreach (var slice in plan)
        {
            if (slice.DestSize > slice.Source.Size)
            {
                var grown = _storage.ResizePartition(p.DestDisk, slice.DestOffset, slice.DestSize);
                if (!grown.Success)
                    _log.Info("Copy finished; extend failed: " + grown.Message);
            }

            ApplyFlags(p.DestDisk, slice);
        }

        progress?.Report(100);
        return Ok("Disk clone finished.");
    }

    private OperationResult CopySlice(
        int sourceDisk,
        int destDisk,
        PlannedSlice slice,
        CloneCopyMode mode,
        IProgress<int>? progress,
        ulong already,
        ulong total,
        CancellationToken cancellationToken)
    {
        if (!CloneLayoutPlanner.NeedsCopy(slice.Source))
            return Ok("Skipped.");

        try
        {
            if (mode == CloneCopyMode.UsedData &&
                slice.Source.DriveLetter is char letter &&
                TryCopyUsed(sourceDisk, destDisk, slice, letter, progress, already, total, cancellationToken))
            {
                return Ok("Used data copied.");
            }

            using var pair = DiskPair.Open(sourceDisk, destDisk);
            CopyRange(
                pair.Source,
                pair.Dest,
                pair.SameDisk,
                (long)slice.Source.Offset,
                (long)slice.DestOffset,
                (long)slice.Source.Size,
                progress,
                already,
                total,
                cancellationToken);

            return Ok("Sectors copied.");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private bool TryCopyUsed(
        int sourceDisk,
        int destDisk,
        PlannedSlice slice,
        char letter,
        IProgress<int>? progress,
        ulong already,
        ulong total,
        CancellationToken cancellationToken)
    {
        if (!NativeDisk.TryClusterInfo(letter, out var cluster, out _) ||
            !NativeDisk.TryVolumeBitmap(letter, out var bits, out var bitCount))
            return false;

        using var pair = DiskPair.Open(sourceDisk, destDisk);
        foreach (var (rel, length) in NativeDisk.AllocatedRuns(bits, bitCount, cluster))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rel >= slice.Source.Size)
                break;
            var take = length;
            if (rel + take > slice.Source.Size)
                take = slice.Source.Size - rel;
            if (take == 0)
                continue;

            CopyRange(
                pair.Source,
                pair.Dest,
                pair.SameDisk,
                (long)(slice.Source.Offset + rel),
                (long)(slice.DestOffset + rel),
                (long)take,
                progress,
                already + rel,
                total,
                cancellationToken);
        }

        return true;
    }

    private void CopyRange(
        SafeFileHandle src,
        SafeFileHandle dest,
        bool sameDisk,
        long srcOffset,
        long destOffset,
        long count,
        IProgress<int>? progress,
        ulong already,
        ulong total,
        CancellationToken cancellationToken)
    {
        var aligned = count / IoAlign * IoAlign;
        if (aligned <= 0)
            return;

        if (sameDisk && destOffset > srcOffset && destOffset < srcOffset + aligned)
        {
            CopyRangeBackward(src, dest, srcOffset, destOffset, aligned, progress, already, total, cancellationToken);
            return;
        }

        using var a = new AlignedBuffer(BlockSize);
        using var b = new AlignedBuffer(BlockSize);
        var readBuf = a;
        var writeBuf = b;
        var remaining = aligned;
        var srcPos = srcOffset;
        var destPos = destOffset;
        var first = Math.Min(BlockSize, (int)remaining);
        NativeDisk.Read(src, srcPos, readBuf.Span[..first]);

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var n = (int)Math.Min(BlockSize, remaining);
            remaining -= n;
            srcPos += n;

            Task? nextRead = null;
            if (remaining > 0 && !sameDisk)
            {
                var nextN = (int)Math.Min(BlockSize, remaining);
                var nextPos = srcPos;
                var nextBuf = writeBuf;
                nextRead = Task.Run(() => NativeDisk.Read(src, nextPos, nextBuf.Span[..nextN]), cancellationToken);
            }

            NativeDisk.Write(dest, destPos, readBuf.Span[..n]);
            destPos += n;
            already += (ulong)n;
            Report(progress, already, total);

            if (nextRead is not null)
            {
                nextRead.GetAwaiter().GetResult();
                (readBuf, writeBuf) = (writeBuf, readBuf);
            }
            else if (remaining > 0)
            {
                var nextN = (int)Math.Min(BlockSize, remaining);
                NativeDisk.Read(src, srcPos, writeBuf.Span[..nextN]);
                (readBuf, writeBuf) = (writeBuf, readBuf);
            }
        }
    }

    private void CopyRangeBackward(
        SafeFileHandle src,
        SafeFileHandle dest,
        long srcOffset,
        long destOffset,
        long aligned,
        IProgress<int>? progress,
        ulong already,
        ulong total,
        CancellationToken cancellationToken)
    {
        using var buf = new AlignedBuffer(BlockSize);
        var remaining = aligned;
        var srcPos = srcOffset + aligned;
        var destPos = destOffset + aligned;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var n = (int)Math.Min(BlockSize, remaining);
            srcPos -= n;
            destPos -= n;
            NativeDisk.Read(src, srcPos, buf.Span[..n]);
            NativeDisk.Write(dest, destPos, buf.Span[..n]);
            remaining -= n;
            already += (ulong)n;
            Report(progress, already, total);
        }
    }

    private void TryQuietDest(int destDisk)
    {
        var offline = _storage.SetDiskOnline(destDisk, false);
        if (offline.Success)
            return;
        _log.Info("Destination stayed online during the copy.");
    }

    private void ApplyFlags(int destDisk, PlannedSlice slice)
    {
        if (slice.Source.IsActive)
            _storage.SetActive(destDisk, slice.DestOffset);
        if (slice.Source.IsHidden)
            _storage.SetHidden(destDisk, slice.DestOffset, true);
    }

    private static ulong AlignCopyLength(ulong count, ulong destSize)
    {
        var aligned = (count + (ulong)IoAlign - 1) / (ulong)IoAlign * (ulong)IoAlign;
        if (aligned > destSize)
            aligned = destSize / (ulong)IoAlign * (ulong)IoAlign;
        return aligned;
    }

    private static void Report(IProgress<int>? progress, ulong done, ulong total)
    {
        if (progress is null || total == 0)
            return;
        progress.Report((int)Math.Clamp(done * 100.0 / total, 0, 99));
    }

    private static OperationResult Ok(string message) => new() { Success = true, Message = message };
    private static OperationResult Fail(string message) => new() { Success = false, Message = message };

    private sealed class DiskPair : IDisposable
    {
        private readonly SafeFileHandle? _ownedDest;

        private DiskPair(SafeFileHandle source, SafeFileHandle dest, SafeFileHandle? ownedDest, bool sameDisk)
        {
            Source = source;
            Dest = dest;
            _ownedDest = ownedDest;
            SameDisk = sameDisk;
        }

        public SafeFileHandle Source { get; }
        public SafeFileHandle Dest { get; }
        public bool SameDisk { get; }

        public static DiskPair Open(int sourceDisk, int destDisk)
        {
            if (sourceDisk == destDisk)
            {
                var both = NativeDisk.OpenPhysical(destDisk, write: true);
                return new DiskPair(both, both, null, true);
            }

            var src = NativeDisk.OpenPhysical(sourceDisk, write: false);
            var dest = NativeDisk.OpenPhysical(destDisk, write: true);
            return new DiskPair(src, dest, dest, false);
        }

        public void Dispose()
        {
            Source.Dispose();
            _ownedDest?.Dispose();
        }
    }
}
