using System.Collections.ObjectModel;
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
        CancellationToken cancellationToken)
    {
        var snapshot = Operations.ToList();
        if (snapshot.Count == 0)
            return new OperationResult { Success = true, Message = "Nothing to apply." };

        for (var i = 0; i < snapshot.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((int)Math.Round((i / (double)snapshot.Count) * 100));
            var result = await _executor.ExecuteAsync(snapshot[i], cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                // Keep remaining operations; drop the ones that already ran.
                while (Operations.Count > 0 && Operations[0].Id != snapshot[i].Id)
                    Operations.RemoveAt(0);
                if (Operations.Count > 0 && Operations[0].Id == snapshot[i].Id)
                    Operations.RemoveAt(0);
                return result;
            }
        }

        Operations.Clear();
        progress?.Report(100);
        return new OperationResult { Success = true, Message = $"Applied {snapshot.Count} operation(s)." };
    }

    private void RebuildWorking() =>
        Working = LayoutPreview.ApplyAll(_live, Operations);
}
