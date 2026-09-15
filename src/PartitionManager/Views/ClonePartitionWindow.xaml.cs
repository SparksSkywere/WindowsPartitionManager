using System.Windows;
using PartitionManager.Helpers;
using PartitionManager.Models;
using PartitionManager.Services;
using PartitionManager.ViewModels;

namespace PartitionManager.Views;

public partial class ClonePartitionWindow : Window
{
    private readonly PartitionViewModel _source;

    public ClonePartitionDialogResult? Result { get; private set; }

    public ClonePartitionWindow(PartitionViewModel source, IReadOnlyList<DiskViewModel> disks)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        _source = source;

        SummaryText.Text =
            $"Copy {source.DisplayName} ({source.SizeText}) into unallocated space. " +
            "The destination region is overwritten when you Apply.";

        foreach (var disk in disks.Where(d => !d.IsOptical && d.IsInitialized && !d.IsReadOnly && !d.IsOffline))
        {
            foreach (var gap in disk.Segments.Where(s => s.IsUnallocated && s.Size >= source.Size))
            {
                if (OverlapsSource(gap))
                    continue;
                DestBox.Items.Add(new RegionPick(disk, gap));
            }
        }

        if (DestBox.Items.Count > 0)
            DestBox.SelectedIndex = 0;

        WarningText.Text = DestBox.Items.Count == 0
            ? "No unallocated region is large enough for this partition."
            : "Existing partitions on the destination disk are left in place.";
    }

    private bool OverlapsSource(PartitionViewModel gap) =>
        gap.DiskNumber == _source.DiskNumber &&
        gap.Offset < _source.Offset + _source.Size &&
        _source.Offset < gap.Offset + gap.Size;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DestBox.SelectedItem is not RegionPick dest)
        {
            MessageBox.Show("Choose a destination region.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var probe = new ClonePartitionParams
        {
            SourceDisk = _source.DiskNumber,
            DestDisk = dest.DiskNumber,
            Source = CloneLayoutPlanner.ToSlice(_source.Model),
            DestOffset = dest.Offset,
            DestRegionSize = dest.Size,
            Mode = UsedRadio.IsChecked == true ? CloneCopyMode.UsedData : CloneCopyMode.AllSectors,
            AlignToMegabyte = AlignBox.IsChecked == true,
            FillRegion = FillBox.IsChecked == true
        };
        if (!CloneLayoutPlanner.TryPlanPartition(probe, out _, out var error))
        {
            MessageBox.Show(error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new ClonePartitionDialogResult
        {
            Source = _source,
            DestDisk = dest.DiskNumber,
            DestOffset = dest.Offset,
            DestRegionSize = dest.Size,
            Mode = probe.Mode,
            AlignToMegabyte = probe.AlignToMegabyte,
            FillRegion = probe.FillRegion
        };
        DialogResult = true;
        Close();
    }

    private sealed class RegionPick
    {
        public RegionPick(DiskViewModel disk, PartitionViewModel gap)
        {
            DiskNumber = disk.Number;
            Offset = gap.Offset;
            Size = gap.Size;
            Display = $"Disk {disk.Number}  —  {ByteSizeFormatter.Format(gap.Size)} free at {ByteSizeFormatter.Format(gap.Offset)}";
        }

        public int DiskNumber { get; }
        public ulong Offset { get; }
        public ulong Size { get; }
        public string Display { get; }
    }
}
