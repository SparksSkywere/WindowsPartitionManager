using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using PartitionManager.Helpers;
using PartitionManager.Models;
using PartitionManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PartitionManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PendingQueueService _queue;
    private readonly PartitionOperationExecutor _executor;
    private readonly ConfigService _config;
    private readonly LogService _log;
    private CancellationTokenSource? _cts;
    private Guid? _selectedId;

    public ObservableCollection<DiskViewModel> Disks { get; } = [];
    public ObservableCollection<PartitionViewModel> Partitions { get; } = [];
    public ObservableCollection<string> LogEntries { get; } = [];
    public ObservableCollection<PendingOperation> PendingOperations => _queue.Operations;

    public ICollectionView PartitionsView { get; }

    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private string _progressPercentText = string.Empty;
    [ObservableProperty] private string _busyOverlayText = "Working…";
    [ObservableProperty] private string _summaryText = "No disks loaded";
    [ObservableProperty] private PartitionViewModel? _selectedPartition;
    [ObservableProperty] private DiskViewModel? _selectedDisk;
    [ObservableProperty] private bool _isElevated = ElevationHelper.IsElevated();
    [ObservableProperty] private bool _showEmptyState;
    [ObservableProperty] private string _emptyStateTitle = "No disks found";
    [ObservableProperty] private string _emptyStateDetail = "Refresh to scan physical disks.";
    [ObservableProperty] private int _pendingCount;

    public bool HasPending => PendingCount > 0;
    public bool IsNotBusy => !IsBusy;
    public string ElevationBanner =>
        IsElevated
            ? ""
            : "Not running as administrator — disk changes will fail. Restart elevated, or continue read-only.";

    public DiskLayout WorkingLayout => _queue.Working;

    public Func<PartitionViewModel, CreatePartitionDialogResult?>? PromptCreate { get; set; }
    public Func<PartitionViewModel, ResizePartitionDialogResult?>? PromptResize { get; set; }
    public Func<PartitionViewModel, FormatPartitionDialogResult?>? PromptFormat { get; set; }
    public Func<PartitionViewModel, DriveLetterDialogResult?>? PromptDriveLetter { get; set; }
    public Func<PartitionViewModel, LabelDialogResult?>? PromptLabel { get; set; }
    public Func<DiskViewModel, InitializeDiskDialogResult?>? PromptInitialize { get; set; }
    public Func<IReadOnlyList<PendingOperation>, bool>? PromptApply { get; set; }
    public Action<PartitionViewModel>? ShowPartitionProperties { get; set; }
    public Action<DiskViewModel>? ShowDiskProperties { get; set; }

    public MainViewModel(
        PendingQueueService queue,
        PartitionOperationExecutor executor,
        ConfigService config,
        LogService log)
    {
        _queue = queue;
        _executor = executor;
        _config = config;
        _log = log;

        PartitionsView = CollectionViewSource.GetDefaultView(Partitions);
        PartitionsView.Filter = FilterPartition;

        _log.MessageLogged += (_, line) => UiThread.Post(() =>
        {
            LogEntries.Add(line);
            while (LogEntries.Count > 500)
                LogEntries.RemoveAt(0);
        });
    }

    partial void OnSearchTextChanged(string value) => PartitionsView.Refresh();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        NotifyOps();
    }

    partial void OnPendingCountChanged(int value) => OnPropertyChanged(nameof(HasPending));

    partial void OnSelectedPartitionChanged(PartitionViewModel? value)
    {
        if (value is not null)
        {
            _selectedId = value.Id;
            if (!ReferenceEquals(SelectedDisk, value.Disk))
                SelectedDisk = value.Disk;
            foreach (var p in Partitions)
                p.IsSelected = p.Id == value.Id;
        }

        NotifyOps();
    }

    [RelayCommand]
    private async Task LoadedAsync()
    {
        if (_config.Config.General.RefreshOnLaunch)
            await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (HasPending)
        {
            var discard = MessageBox.Show(
                "Refreshing will discard pending operations. Continue?",
                AppInfo.ProductName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (discard != MessageBoxResult.Yes)
                return;
            _queue.DiscardAll();
        }

        await RunBusyAsync("Reading disks…", async ct =>
        {
            await _queue.RefreshAsync(_config.Config.Display, ct).ConfigureAwait(true);
            RebuildViews();
            StatusText = "Ready";
        }).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void CreatePartition()
    {
        if (SelectedPartition is not { IsUnallocated: true } seg)
            return;
        var result = PromptCreate?.Invoke(seg);
        if (result is null)
            return;

        var isLogical = SelectedDisk?.PartitionStyle == PartitionStyleKind.Mbr &&
                        SelectedDisk.Segments.Any(s =>
                            s.Kind == SegmentKind.Extended &&
                            s.Offset < seg.Offset &&
                            s.Offset + s.Size >= seg.Offset + result.Size);

        Queue(new PendingOperation
        {
            Kind = OperationKind.CreatePartition,
            DiskNumber = seg.DiskNumber,
            Offset = seg.Offset,
            Size = result.Size,
            Description =
                $"Create {ByteSizeFormatter.Format(result.Size)} {result.FileSystem} partition on Disk {seg.DiskNumber}" +
                (result.DriveLetter is char c ? $" ({c}:)" : ""),
            Create = new CreatePartitionParams
            {
                Offset = seg.Offset,
                Size = result.Size,
                DriveLetter = result.DriveLetter,
                Label = result.Label,
                FileSystem = result.FileSystem,
                FormatAfterCreate = result.FormatAfterCreate,
                QuickFormat = result.QuickFormat,
                IsLogical = isLogical
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DeletePartition()
    {
        if (SelectedPartition is not { } seg || seg.IsUnallocated)
            return;
        if (!ConfirmDestructive($"Delete {seg.DisplayName} on Disk {seg.DiskNumber}? The data will be unrecoverable after Apply."))
            return;

        Queue(new PendingOperation
        {
            Kind = OperationKind.DeletePartition,
            DiskNumber = seg.DiskNumber,
            SegmentId = seg.Id,
            Offset = seg.Offset,
            Size = seg.Size,
            Description = $"Delete {seg.DisplayName} on Disk {seg.DiskNumber}",
            IsDestructive = true
        });
    }

    [RelayCommand(CanExecute = nameof(CanFormat))]
    private void FormatPartition()
    {
        if (SelectedPartition is not { } seg || seg.IsUnallocated)
            return;
        var result = PromptFormat?.Invoke(seg);
        if (result is null)
            return;
        if (!ConfirmDestructive($"Format {seg.DisplayName} as {result.FileSystem}? All files on this volume will be erased after Apply."))
            return;

        Queue(new PendingOperation
        {
            Kind = OperationKind.FormatPartition,
            DiskNumber = seg.DiskNumber,
            SegmentId = seg.Id,
            Offset = seg.Offset,
            Size = seg.Size,
            DriveLetter = seg.DriveLetter,
            Description = $"Format {seg.DisplayName} as {result.FileSystem}",
            IsDestructive = true,
            Format = new FormatPartitionParams
            {
                FileSystem = result.FileSystem,
                Label = result.Label,
                QuickFormat = result.QuickFormat
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanResize))]
    private void ResizePartition()
    {
        if (SelectedPartition is not { } seg || seg.IsUnallocated)
            return;
        var result = PromptResize?.Invoke(seg);
        if (result is null || result.NewSize == seg.Size)
            return;

        var verb = result.NewSize > seg.Size ? "Extend" : "Shrink";
        Queue(new PendingOperation
        {
            Kind = OperationKind.ResizePartition,
            DiskNumber = seg.DiskNumber,
            SegmentId = seg.Id,
            Offset = seg.Offset,
            Size = seg.Size,
            Description = $"{verb} {seg.DisplayName} to {ByteSizeFormatter.Format(result.NewSize)}",
            Resize = new ResizePartitionParams { NewSize = result.NewSize }
        });
    }

    [RelayCommand(CanExecute = nameof(CanChangeLetter))]
    private void ChangeDriveLetter()
    {
        if (SelectedPartition is not { } seg || seg.IsUnallocated)
            return;
        var result = PromptDriveLetter?.Invoke(seg);
        if (result is null)
            return;

        var desc = result.DriveLetter is char c
            ? $"Change drive letter of {seg.DisplayName} to {c}:"
            : $"Remove drive letter from {seg.DisplayName}";
        Queue(new PendingOperation
        {
            Kind = OperationKind.ChangeDriveLetter,
            DiskNumber = seg.DiskNumber,
            SegmentId = seg.Id,
            Offset = seg.Offset,
            Size = seg.Size,
            DriveLetter = result.DriveLetter,
            Description = desc
        });
    }

    [RelayCommand(CanExecute = nameof(CanChangeLabel))]
    private void ChangeLabel()
    {
        if (SelectedPartition is not { } seg || seg.IsUnallocated)
            return;
        var result = PromptLabel?.Invoke(seg);
        if (result is null)
            return;

        Queue(new PendingOperation
        {
            Kind = OperationKind.ChangeLabel,
            DiskNumber = seg.DiskNumber,
            SegmentId = seg.Id,
            Offset = seg.Offset,
            Size = seg.Size,
            DriveLetter = seg.DriveLetter,
            Label = result.Label,
            Description = $"Set label of {seg.DisplayName} to \"{result.Label}\""
        });
    }

    [RelayCommand(CanExecute = nameof(CanHide))]
    private void HidePartition()
    {
        if (SelectedPartition is not { } seg || seg.IsUnallocated)
            return;
        var hide = !seg.Model.IsHidden;
        Queue(new PendingOperation
        {
            Kind = hide ? OperationKind.HidePartition : OperationKind.UnhidePartition,
            DiskNumber = seg.DiskNumber,
            SegmentId = seg.Id,
            Offset = seg.Offset,
            Size = seg.Size,
            Description = (hide ? "Hide " : "Unhide ") + seg.DisplayName
        });
    }

    [RelayCommand(CanExecute = nameof(CanSetActive))]
    private void SetActive()
    {
        if (SelectedPartition is not { } seg || seg.IsUnallocated)
            return;
        Queue(new PendingOperation
        {
            Kind = OperationKind.SetActive,
            DiskNumber = seg.DiskNumber,
            SegmentId = seg.Id,
            Offset = seg.Offset,
            Size = seg.Size,
            Description = $"Set {seg.DisplayName} active on Disk {seg.DiskNumber}"
        });
    }

    [RelayCommand(CanExecute = nameof(CanInitialize))]
    private void InitializeDisk()
    {
        var disk = SelectedDisk ?? SelectedPartition?.Disk;
        if (disk is null)
            return;
        var result = PromptInitialize?.Invoke(disk);
        if (result is null)
            return;
        Queue(new PendingOperation
        {
            Kind = OperationKind.InitializeDisk,
            DiskNumber = disk.Number,
            TargetStyle = result.Style,
            Description = $"Initialize Disk {disk.Number} as {result.Style}"
        });
    }

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private void ConvertStyle()
    {
        var disk = SelectedDisk ?? SelectedPartition?.Disk;
        if (disk is null)
            return;
        var target = disk.PartitionStyle == PartitionStyleKind.Gpt
            ? PartitionStyleKind.Mbr
            : PartitionStyleKind.Gpt;
        if (!ConfirmDestructive(
                $"Convert Disk {disk.Number} from {disk.StyleText} to {target}? The disk must have no partitions."))
            return;
        Queue(new PendingOperation
        {
            Kind = OperationKind.ConvertPartitionStyle,
            DiskNumber = disk.Number,
            TargetStyle = target,
            Description = $"Convert Disk {disk.Number} to {target}",
            IsDestructive = true
        });
    }

    [RelayCommand(CanExecute = nameof(CanDeleteAll))]
    private void DeleteAllPartitions()
    {
        var disk = SelectedDisk ?? SelectedPartition?.Disk;
        if (disk is null)
            return;
        if (!ConfirmDestructive(
                $"Delete ALL partitions on Disk {disk.Number} ({disk.DetailText})? Every volume on this disk will be destroyed after Apply."))
            return;
        Queue(new PendingOperation
        {
            Kind = OperationKind.DeleteAllPartitions,
            DiskNumber = disk.Number,
            Description = $"Delete all partitions on Disk {disk.Number}",
            IsDestructive = true
        });
    }

    [RelayCommand(CanExecute = nameof(CanToggleOnline))]
    private void ToggleOnline()
    {
        var disk = SelectedDisk ?? SelectedPartition?.Disk;
        if (disk is null)
            return;
        var online = disk.IsOffline;
        Queue(new PendingOperation
        {
            Kind = online ? OperationKind.OnlineDisk : OperationKind.OfflineDisk,
            DiskNumber = disk.Number,
            Description = (online ? "Bring online " : "Take offline ") + $"Disk {disk.Number}"
        });
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckPartitionAsync()
    {
        if (SelectedPartition?.DriveLetter is not char letter)
            return;
        var fix = MessageBox.Show(
            $"Check {letter}: for file system errors?\n\nYes = scan only (read-only)\nNo = scan and fix (chkdsk /f, may dismount)\nCancel = abort",
            AppInfo.ProductName,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (fix == MessageBoxResult.Cancel)
            return;

        await RunBusyAsync($"Checking {letter}:…", async ct =>
        {
            var result = await _executor.CheckPartitionAsync(letter, fix == MessageBoxResult.No, ct)
                .ConfigureAwait(true);
            StatusText = result.Success ? $"Check of {letter}: finished" : result.Message;
            if (!result.Success)
            {
                MessageBox.Show(result.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanShowPartitionProperties))]
    private void PartitionProperties()
    {
        if (SelectedPartition is { } p)
            ShowPartitionProperties?.Invoke(p);
    }

    [RelayCommand(CanExecute = nameof(CanShowDiskProperties))]
    private void DiskProperties()
    {
        var disk = SelectedDisk ?? SelectedPartition?.Disk;
        if (disk is not null)
            ShowDiskProperties?.Invoke(disk);
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (!_queue.HasPending)
            return;
        if (PromptApply?.Invoke(_queue.Operations.ToList()) != true)
            return;

        await RunBusyAsync("Applying operations…", async ct =>
        {
            var progress = new Progress<int>(v =>
            {
                ProgressValue = v;
                IsProgressIndeterminate = false;
                ProgressPercentText = $"{v}%";
            });
            var result = await _queue.ApplyAsync(progress, ct).ConfigureAwait(true);
            await _queue.RefreshAsync(_config.Config.Display, ct).ConfigureAwait(true);
            RebuildViews();
            if (result.Success)
            {
                StatusText = result.Message;
                _log.Success(result.Message);
            }
            else
            {
                StatusText = "Apply failed: " + result.Message;
                MessageBox.Show(result.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void UndoLast()
    {
        _queue.UndoLast();
        RebuildViews();
        StatusText = PendingCount == 0 ? "Pending operations cleared" : $"{PendingCount} pending";
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void DiscardPending()
    {
        if (!_queue.HasPending)
            return;
        if (MessageBox.Show(
                "Discard all pending operations?",
                AppInfo.ProductName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _queue.DiscardAll();
        RebuildViews();
        StatusText = "Pending operations discarded";
    }

    [RelayCommand]
    private void Cancel()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
    }

    [RelayCommand]
    private void ClearLog() => LogEntries.Clear();

    [RelayCommand]
    private void OpenConfigFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _config.AppDataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void SelectSegment(PartitionViewModel? partition)
    {
        if (partition is null) return;
        SelectPartition(partition);
    }

    [RelayCommand]
    private void PickDisk(DiskViewModel? disk)
    {
        if (disk is null) return;
        SelectDisk(disk);
    }

    public void SelectPartition(PartitionViewModel partition)
    {
        SelectedPartition = partition;
        SelectedDisk = partition.Disk;
    }

    public void SelectDisk(DiskViewModel disk)
    {
        SelectedDisk = disk;
        var first = disk.Segments.FirstOrDefault();
        if (first is not null)
            SelectedPartition = first;
        NotifyOps();
    }

    private void Queue(PendingOperation op)
    {
        _queue.Enqueue(op);
        RebuildViews();
        StatusText = $"{PendingCount} pending operation(s) — click Apply to commit";
    }

    private void RebuildViews()
    {
        var keep = _selectedId ?? SelectedPartition?.Id;
        Disks.Clear();
        Partitions.Clear();
        foreach (var disk in _queue.Working.Disks)
        {
            var dvm = new DiskViewModel(disk);
            Disks.Add(dvm);
            foreach (var seg in dvm.Segments)
                Partitions.Add(seg);
        }

        PendingCount = _queue.Count;
        var totalParts = Partitions.Count(p => !p.IsUnallocated);
        var unalloc = Partitions.Count(p => p.IsUnallocated);
        SummaryText = $"{Disks.Count} disk(s)  ·  {totalParts} partition(s)  ·  {unalloc} unallocated" +
                      (PendingCount > 0 ? $"  ·  {PendingCount} pending" : "");
        ShowEmptyState = Disks.Count == 0;

        PartitionViewModel? match = null;
        if (keep is Guid id)
            match = Partitions.FirstOrDefault(p => p.Id == id);
        match ??= Partitions.FirstOrDefault();
        SelectedPartition = match;
        SelectedDisk = match?.Disk ?? Disks.FirstOrDefault();
        PartitionsView.Refresh();
        NotifyOps();
        OnPropertyChanged(nameof(PendingOperations));
        OnPropertyChanged(nameof(HasPending));
    }

    private bool FilterPartition(object obj)
    {
        if (obj is not PartitionViewModel p)
            return false;
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;
        var q = SearchText.Trim();
        return p.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               p.FileSystemText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               p.TypeText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               p.DiskText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               p.LabelText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               p.DriveText.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private bool ConfirmDestructive(string message)
    {
        if (!_config.Config.Safety.ConfirmDestructive)
            return true;
        return MessageBox.Show(message, AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning) ==
               MessageBoxResult.Yes;
    }

    private async Task RunBusyAsync(string overlay, Func<CancellationToken, Task> work)
    {
        if (IsBusy)
            return;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        ProgressPercentText = "";
        BusyOverlayText = overlay;
        StatusText = overlay;
        NotifyOps();
        try
        {
            await work(_cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
            StatusText = ex.Message;
            MessageBox.Show(ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            ProgressPercentText = "";
            NotifyOps();
        }
    }

    private bool CanRefresh() => !IsBusy;
    private bool CanApply() => !IsBusy && HasPending;
    private bool CanUndo() => !IsBusy && HasPending;
    private bool CanCreate() => !IsBusy && SelectedPartition is { IsUnallocated: true } &&
                                SelectedPartition.Disk.IsInitialized && !SelectedPartition.Disk.IsOptical &&
                                !SelectedPartition.Disk.IsOffline;
    private bool CanDelete() => CanMutatePartition() && !SelectedPartition!.IsUnallocated &&
                                !IsProtected(SelectedPartition);
    private bool CanFormat() => CanMutatePartition() && !SelectedPartition!.IsUnallocated &&
                                SelectedPartition.Kind is not (SegmentKind.MicrosoftReserved or SegmentKind.Efi) &&
                                !IsProtected(SelectedPartition);
    private bool CanResize() => CanMutatePartition() && !SelectedPartition!.IsUnallocated &&
                                SelectedPartition.Kind is not (SegmentKind.MicrosoftReserved or SegmentKind.Efi);
    private bool CanChangeLetter() => CanMutatePartition() && !SelectedPartition!.IsUnallocated &&
                                      SelectedPartition.Kind is not SegmentKind.MicrosoftReserved;
    private bool CanChangeLabel() => CanMutatePartition() && !SelectedPartition!.IsUnallocated &&
                                     !string.IsNullOrEmpty(SelectedPartition.Model.FileSystem);
    private bool CanHide() => CanMutatePartition() && !SelectedPartition!.IsUnallocated &&
                              !SelectedPartition.IsProtected;
    private bool CanSetActive() => CanMutatePartition() && !SelectedPartition!.IsUnallocated &&
                                   SelectedPartition.Disk.PartitionStyle == PartitionStyleKind.Mbr &&
                                   SelectedPartition.Kind == SegmentKind.Primary;
    private bool CanInitialize() => !IsBusy && (SelectedDisk ?? SelectedPartition?.Disk) is { IsInitialized: false, IsOptical: false };
    private bool CanConvert() => !IsBusy &&
                                 (SelectedDisk ?? SelectedPartition?.Disk) is { IsInitialized: true, IsOptical: false } disk &&
                                 disk.Segments.All(s => s.IsUnallocated) &&
                                 !disk.IsBoot && !disk.IsSystem;
    private bool CanDeleteAll() => !IsBusy &&
                                   (SelectedDisk ?? SelectedPartition?.Disk) is { IsOptical: false } disk &&
                                   disk.Segments.Any(s => !s.IsUnallocated) &&
                                   !disk.IsBoot && !disk.IsSystem;
    private bool CanToggleOnline() => !IsBusy &&
                                      (SelectedDisk ?? SelectedPartition?.Disk) is { IsOptical: false } disk &&
                                      !disk.IsBoot && !disk.IsSystem;
    private bool CanCheck() => !IsBusy && SelectedPartition?.DriveLetter is not null;
    private bool CanShowPartitionProperties() => SelectedPartition is not null;
    private bool CanShowDiskProperties() => (SelectedDisk ?? SelectedPartition?.Disk) is not null;

    private bool CanMutatePartition() =>
        !IsBusy && SelectedPartition is not null &&
        !SelectedPartition.Disk.IsOptical &&
        !SelectedPartition.Disk.IsOffline &&
        SelectedPartition.Disk.IsInitialized;

    private bool IsProtected(PartitionViewModel p) =>
        _config.Config.Safety.ProtectSystemPartitions && p.IsProtected;

    public string HideHeader =>
        SelectedPartition?.Model.IsHidden == true ? "Unhide partition" : "Hide partition";

    public string OnlineHeader =>
        (SelectedDisk ?? SelectedPartition?.Disk)?.IsOffline == true ? "Online disk" : "Offline disk";

    public string ConvertHeader
    {
        get
        {
            var disk = SelectedDisk ?? SelectedPartition?.Disk;
            if (disk is null || !disk.IsInitialized)
                return "Convert MBR/GPT";
            return disk.PartitionStyle == PartitionStyleKind.Gpt
                ? "Convert to MBR"
                : "Convert to GPT";
        }
    }

    private void NotifyOps()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CreatePartitionCommand.NotifyCanExecuteChanged();
        DeletePartitionCommand.NotifyCanExecuteChanged();
        FormatPartitionCommand.NotifyCanExecuteChanged();
        ResizePartitionCommand.NotifyCanExecuteChanged();
        ChangeDriveLetterCommand.NotifyCanExecuteChanged();
        ChangeLabelCommand.NotifyCanExecuteChanged();
        HidePartitionCommand.NotifyCanExecuteChanged();
        SetActiveCommand.NotifyCanExecuteChanged();
        InitializeDiskCommand.NotifyCanExecuteChanged();
        ConvertStyleCommand.NotifyCanExecuteChanged();
        DeleteAllPartitionsCommand.NotifyCanExecuteChanged();
        ToggleOnlineCommand.NotifyCanExecuteChanged();
        CheckPartitionCommand.NotifyCanExecuteChanged();
        PartitionPropertiesCommand.NotifyCanExecuteChanged();
        DiskPropertiesCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        UndoLastCommand.NotifyCanExecuteChanged();
        DiscardPendingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HideHeader));
        OnPropertyChanged(nameof(OnlineHeader));
        OnPropertyChanged(nameof(ConvertHeader));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(IsNotBusy));
    }
}
