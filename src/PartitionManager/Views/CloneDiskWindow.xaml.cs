using System.Windows;
using System.Windows.Controls;
using PartitionManager.Helpers;
using PartitionManager.Models;
using PartitionManager.Services;
using PartitionManager.ViewModels;

namespace PartitionManager.Views;

public partial class CloneDiskWindow : Window
{
    private readonly IReadOnlyList<DiskPick> _sources;
    private readonly IReadOnlyList<DiskPick> _destinations;

    public CloneDiskDialogResult? Result { get; private set; }

    public CloneDiskWindow(DiskViewModel source, IReadOnlyList<DiskViewModel> disks)
    {
        InitializeComponent();
        DialogChrome.Init(this);

        _sources = disks
            .Where(d => !d.IsOptical && d.IsInitialized && d.Segments.Any(s => !s.IsUnallocated))
            .Select(d => new DiskPick(d))
            .ToList();
        _destinations = disks
            .Where(d => !d.IsOptical && !d.IsBoot && !d.IsSystem && !d.IsReadOnly)
            .Select(d => new DiskPick(d))
            .ToList();

        foreach (var item in _sources)
            SourceBox.Items.Add(item);
        foreach (var item in _destinations)
            DestBox.Items.Add(item);

        SourceBox.SelectedItem = _sources.FirstOrDefault(s => s.Disk.Number == source.Number)
                                 ?? _sources.FirstOrDefault();
        DestBox.SelectedItem = _destinations.FirstOrDefault(d => d.Disk.Number != source.Number);

        SummaryText.Text = "Copy every partition from the source disk onto the destination. " +
                           "The destination is overwritten when you Apply.";
        UpdateHints();
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdateHints();

    private void UpdateHints()
    {
        if (SourceBox.SelectedItem is not DiskPick source || DestBox.SelectedItem is not DiskPick dest)
        {
            HintText.Text = "Choose a source and a destination disk.";
            WarningText.Text = "";
            return;
        }

        HintText.Text =
            $"Source {ByteSizeFormatter.Format(source.Disk.Model.Size)}  →  " +
            $"destination {ByteSizeFormatter.Format(dest.Disk.Model.Size)}.";

        var warn = new List<string>
        {
            $"All partitions on Disk {dest.Disk.Number} will be erased."
        };
        if (source.Disk.IsBoot || source.Disk.IsSystem)
            warn.Add("The source is a system disk. Set firmware boot order yourself after the clone.");
        if (dest.Disk.Model.Size < source.Disk.Model.Size)
            warn.Add("The destination is smaller. Used-data mode and 1 MB alignment may still fit.");
        WarningText.Text = string.Join(" ", warn);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SourceBox.SelectedItem is not DiskPick source || DestBox.SelectedItem is not DiskPick dest)
        {
            MessageBox.Show("Choose a source and a destination disk.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (source.Disk.Number == dest.Disk.Number)
        {
            MessageBox.Show("Source and destination must be different disks.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (dest.Disk.IsBoot || dest.Disk.IsSystem)
        {
            MessageBox.Show("The destination cannot be the current system disk.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mode = UsedRadio.IsChecked == true ? CloneCopyMode.UsedData : CloneCopyMode.AllSectors;
        var probe = new CloneDiskParams
        {
            SourceDisk = source.Disk.Number,
            DestDisk = dest.Disk.Number,
            Mode = mode,
            AlignToMegabyte = AlignBox.IsChecked == true,
            ExpandLastPartition = ExpandBox.IsChecked == true,
            Style = source.Disk.PartitionStyle,
            DestSize = dest.Disk.Model.Size,
            SourceSlices = source.Disk.Model.Segments.Select(CloneLayoutPlanner.ToSlice).ToList()
        };
        if (!CloneLayoutPlanner.TryPlanDisk(probe, out _, out var error))
        {
            MessageBox.Show(error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new CloneDiskDialogResult
        {
            Source = source.Disk,
            Dest = dest.Disk,
            Mode = mode,
            AlignToMegabyte = AlignBox.IsChecked == true,
            ExpandLastPartition = ExpandBox.IsChecked == true
        };
        DialogResult = true;
        Close();
    }

    private sealed class DiskPick
    {
        public DiskPick(DiskViewModel disk)
        {
            Disk = disk;
            Display = $"Disk {disk.Number}  —  {disk.DetailText}";
        }

        public DiskViewModel Disk { get; }
        public string Display { get; }
    }
}
