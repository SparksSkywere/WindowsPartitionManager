using System.Collections.ObjectModel;
using PartitionManager.Helpers;
using PartitionManager.Models;

namespace PartitionManager.Services;

/// <summary>
/// Holds the live inventory snapshot plus a working preview mutated by queued operations.
/// Apply executes the queue in order, then reloads from the live disk.
/// </summary>
public sealed class PendingQueueService
{
    private readonly DiskInventoryService _inventory;
    private readonly PartitionOperationExecutor _executor;
    private readonly LogService _log;
    private DiskLayout _live = new();

    public PendingQueueService(
        DiskInventoryService inventory,
        PartitionOperationExecutor executor,
        LogService log)
    {
        _inventory = inventory;
        _executor = executor;
        _log = log;
    }

    public DiskLayout Live => _live;
    public DiskLayout Working { get; private set; } = new();
    public ObservableCollection<PendingOperation> Operations { get; } = [];
    public int Count => Operations.Count;
    public bool HasPending => Operations.Count > 0;
    public bool HasDestructive => Operations.Any(o => o.IsDestructive);

    public async Task RefreshAsync(DisplaySettings display, CancellationToken cancellationToken = default)
    {
        _live = await _inventory.QueryAsync(display, cancellationToken).ConfigureAwait(false);
        RebuildWorking();
    }

    public void Enqueue(PendingOperation op)
    {
        Operations.Add(op);
        RebuildWorking();
        _log.Info("Queued: " + op.Description);
    }

    public void UndoLast()
    {
        if (Operations.Count == 0)
            return;
        var last = Operations[^1];
        Operations.RemoveAt(Operations.Count - 1);
        RebuildWorking();
        _log.Info("Undid: " + last.Description);
    }

    public void DiscardAll()
    {
        if (Operations.Count == 0)
            return;
        var n = Operations.Count;
        Operations.Clear();
        RebuildWorking();
        _log.Info($"Discarded {n} pending operation(s).");
    }

    public async Task<OperationResult> ApplyAsync(
        IProgress<int>? progress,
        CancellationToken cancellationToken,
        ICollection<OfflineMoveJob>? restartJobs = null)
    {
        var snapshot = Operations.ToList();
        if (snapshot.Count == 0)
            return new OperationResult { Success = true, Message = "Nothing to apply." };

        var bootMoves = snapshot.Where(OfflineApplyService.IsBootStartMove).ToList();
        if (bootMoves.Count > 0 && restartJobs is not null)
        {
            if (SessionMode.IsRemoteDesktop())
            {
                return new OperationResult
                {
                    Success = false,
                    Message = WindowsVolume.RemoteRecoveryBlockedMessage
                };
            }

            foreach (var op in bootMoves)
            {
                if (op.Resize?.DriveLetter is not char letter)
                    continue;
                if (!VolumeEncryption.IsProtected(letter))
                    continue;
                return new OperationResult
                {
                    Success = false,
                    Message = "Turn off device encryption on " + letter + ": first."
                };
            }
        }

        for (var i = 0; i < snapshot.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = i;
            progress?.Report((int)Math.Round((index / (double)snapshot.Count) * 100));
            var slice = new Progress<int>(p =>
            {
                var blended = (int)Math.Round(((index + p / 100.0) / snapshot.Count) * 100);
                progress?.Report(Math.Clamp(blended, 0, 99));
            });

            var op = snapshot[i];
            if (OfflineApplyService.IsBootStartMove(op) && restartJobs is not null)
            {
                var deferred = await DeferBootStartMoveAsync(op, slice, cancellationToken).ConfigureAwait(true);
                if (!deferred.Success)
                {
                    DropAppliedThrough(snapshot, i);
                    return deferred;
                }

                restartJobs.Add(OfflineMoveJob.From(WithCopySize(op)));
                continue;
            }

            var result = await _executor.ExecuteAsync(op, cancellationToken, slice).ConfigureAwait(true);
            if (!result.Success)
            {
                DropAppliedThrough(snapshot, i);
                return result;
            }
        }

        UiThread.Send(() => Operations.Clear());
        progress?.Report(100);
        if (restartJobs is { Count: > 0 })
        {
            return new OperationResult
            {
                Success = true,
                RestartRequired = true,
                Message = $"Applied {snapshot.Count} operation(s). Restart to finish the Windows volume move."
            };
        }

        return new OperationResult { Success = true, Message = $"Applied {snapshot.Count} operation(s)." };
    }

    private async Task<OperationResult> DeferBootStartMoveAsync(
        PendingOperation op,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var r = op.Resize ?? throw new InvalidOperationException("Resize parameters missing.");
        if (r.NewSize >= op.Size)
            return new OperationResult { Success = true, Message = "Windows volume shrink not required." };

        var shrink = new PendingOperation
        {
            Kind = OperationKind.ResizePartition,
            DiskNumber = op.DiskNumber,
            Offset = op.Offset,
            Size = op.Size,
            Description = "Shrink Windows volume from the end",
            Resize = new ResizePartitionParams
            {
                NewSize = r.NewSize,
                NewOffset = op.Offset,
                ChangeOffset = false,
                GptType = r.GptType,
                MbrType = r.MbrType,
                IsActive = r.IsActive,
                IsHidden = r.IsHidden,
                DriveLetter = r.DriveLetter,
                FileSystem = r.FileSystem,
                Kind = r.Kind,
                IsBoot = r.IsBoot
            }
        };

        _log.Info("Shrinking the Windows volume from the end before the Recovery move.");
        return await _executor.ExecuteAsync(shrink, cancellationToken, progress).ConfigureAwait(true);
    }

    private static PendingOperation WithCopySize(PendingOperation op)
    {
        var r = op.Resize;
        if (r is null || r.NewSize >= op.Size || op.Size == 0)
            return op;
        return new PendingOperation
        {
            Kind = op.Kind,
            DiskNumber = op.DiskNumber,
            SegmentId = op.SegmentId,
            Offset = op.Offset,
            Size = r.NewSize,
            Description = op.Description,
            IsDestructive = true,
            Resize = r
        };
    }

    private void DropAppliedThrough(List<PendingOperation> snapshot, int failedIndex)
    {
        UiThread.Send(() =>
        {
            while (Operations.Count > 0 && Operations[0].Id != snapshot[failedIndex].Id)
                Operations.RemoveAt(0);
            if (Operations.Count > 0 && Operations[0].Id == snapshot[failedIndex].Id)
                Operations.RemoveAt(0);
        });
    }

    private void RebuildWorking() =>
        Working = LayoutPreview.ApplyAll(_live, Operations);
}
