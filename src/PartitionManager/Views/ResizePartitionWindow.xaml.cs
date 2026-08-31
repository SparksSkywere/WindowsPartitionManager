using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PartitionManager.Helpers;
using PartitionManager.Models;
using PartitionManager.Services;
using PartitionManager.ViewModels;

namespace PartitionManager.Views;

public partial class ResizePartitionWindow : Window
{
    private readonly ulong _min;
    private readonly ulong _max;
    private bool _syncing;

    public ResizePartitionDialogResult? Result { get; private set; }

    public ResizePartitionWindow(PartitionViewModel partition)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        (_min, _max) = LayoutPreview.ResizeBounds(partition.Disk.Model, partition.Model);
        var currentMb = Math.Max(1, partition.Size / (1024d * 1024d));
        SizeSlider.Minimum = Math.Max(1, _min / (1024d * 1024d));
        SizeSlider.Maximum = Math.Max(SizeSlider.Minimum, _max / (1024d * 1024d));
        SizeSlider.Value = Math.Clamp(currentMb, SizeSlider.Minimum, SizeSlider.Maximum);
        SizeBox.Text = ((int)Math.Round(SizeSlider.Value)).ToString(CultureInfo.InvariantCulture);
        SummaryText.Text = $"Resize {partition.DisplayName} ({partition.SizeText}).";
        RangeText.Text = $"Minimum {ByteSizeFormatter.Format(_min)}  ·  Maximum {ByteSizeFormatter.Format(_max)}";
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        _syncing = true;
        SizeBox.Text = ((int)Math.Round(SizeSlider.Value)).ToString(CultureInfo.InvariantCulture);
        _syncing = false;
    }

    private void SizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (!double.TryParse(SizeBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var mb))
            return;
        _syncing = true;
        SizeSlider.Value = Math.Clamp(mb, SizeSlider.Minimum, SizeSlider.Maximum);
        _syncing = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(SizeBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var mb))
            return;
        var bytes = ByteSizeFormatter.AlignDown(ByteSizeFormatter.FromMegaBytes(mb), LayoutPreview.Alignment);
        bytes = Math.Clamp(bytes, _min, _max);
        Result = new ResizePartitionDialogResult { NewSize = bytes };
        DialogResult = true;
        Close();
    }
}
