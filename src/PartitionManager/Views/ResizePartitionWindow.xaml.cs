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
    private readonly LayoutPreview.ResizeRegion _region;
    private readonly bool _windowsVolume;
    private bool _syncing;
    private ulong _before;
    private ulong _size;
    private ulong _after;

    public ResizePartitionDialogResult? Result { get; private set; }

    public ResizePartitionWindow(PartitionViewModel partition)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        _region = LayoutPreview.GetResizeRegion(partition.Disk.Model, partition.Model);
        _before = partition.Offset - _region.SpanStart;
        _windowsVolume = WindowsVolume.IsOnlineSystemVolume(partition.DriveLetter, partition.Model.IsBoot);
        _size = partition.Size;
        _after = _region.SpanEnd - (partition.Offset + partition.Size);
        Normalize(keepSize: true);

        var spanMb = ToMb(_region.SpanLength);
        BeforeSlider.Minimum = 0;
        BeforeSlider.Maximum = Math.Max(0, spanMb);
        SizeSlider.Minimum = Math.Max(1, ToMb(_region.MinSize));
        SizeSlider.Maximum = Math.Max(SizeSlider.Minimum, spanMb);
        AfterSlider.Minimum = 0;
        AfterSlider.Maximum = Math.Max(0, spanMb);

        SummaryText.Text = $"Resize {partition.DisplayName} ({partition.SizeText}).";
        RangeText.Text =
            $"Region {ByteSizeFormatter.Format(_region.SpanLength)}  ·  " +
            $"min {ByteSizeFormatter.Format(_region.MinSize)}";
        if (_windowsVolume)
            HintText.Text = WindowsVolume.MoveRestartHint;
        else
            HintText.Text = "Set Unallocated before to 0 (or Move to start) to slide the partition. Moving copies data.";

        PushUi();
    }

    private void MoveToStart_Click(object sender, RoutedEventArgs e)
    {
        _before = 0;
        Normalize(keepSize: true);
        PushUi();
    }

    private void BeforeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        FromSlider(FromMb(BeforeSlider.Value), _size, null);

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        FromSlider(_before, FromMb(SizeSlider.Value), null);

    private void AfterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        FromSlider(_before, null, FromMb(AfterSlider.Value));

    private void BeforeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || !TryParseMb(BeforeBox.Text, out var mb))
            return;
        FromSlider(FromMb(mb), _size, null);
    }

    private void SizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || !TryParseMb(SizeBox.Text, out var mb))
            return;
        FromSlider(_before, FromMb(mb), null);
    }

    private void AfterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || !TryParseMb(AfterBox.Text, out var mb))
            return;
        FromSlider(_before, null, FromMb(mb));
    }

    private void FromSlider(ulong before, ulong? size, ulong? after)
    {
        if (_syncing)
            return;
        _before = before;
        if (size is ulong s)
            _size = s;
        if (after is ulong a)
        {
            _after = a;
            Normalize(keepSize: false);
        }
        else
        {
            Normalize(keepSize: true);
        }

        PushUi();
    }

    private void Normalize(bool keepSize)
    {
        var span = _region.SpanLength;
        var min = Math.Min(_region.MinSize, span);
        _before = ByteSizeFormatter.AlignDown(_before, LayoutPreview.Alignment);
        _size = ByteSizeFormatter.AlignDown(_size, LayoutPreview.Alignment);
        _after = ByteSizeFormatter.AlignDown(_after, LayoutPreview.Alignment);

        if (_before > span)
            _before = span;

        if (keepSize)
        {
            if (_size < min)
                _size = min;
            if (_before + _size > span)
            {
                if (span >= min)
                    _size = ByteSizeFormatter.AlignDown(span - _before, LayoutPreview.Alignment);
                if (_size < min)
                {
                    _size = min;
                    _before = ByteSizeFormatter.AlignDown(span > min ? span - min : 0, LayoutPreview.Alignment);
                }
            }

            _after = span - _before - _size;
        }
        else
        {
            if (_after > span)
                _after = span;
            if (_before + _after > span)
                _after = span - _before;
            _size = span - _before - _after;
            if (_size < min)
            {
                _size = min;
                if (_before + _size > span)
                    _before = ByteSizeFormatter.AlignDown(span > min ? span - min : 0, LayoutPreview.Alignment);
                _after = span - _before - _size;
            }
        }
    }

    private void PushUi()
    {
        _syncing = true;
        BeforeSlider.Value = ToMb(_before);
        SizeSlider.Value = ToMb(_size);
        AfterSlider.Value = ToMb(_after);
        BeforeBox.Text = ((int)Math.Round(BeforeSlider.Value)).ToString(CultureInfo.InvariantCulture);
        SizeBox.Text = ((int)Math.Round(SizeSlider.Value)).ToString(CultureInfo.InvariantCulture);
        AfterBox.Text = ((int)Math.Round(AfterSlider.Value)).ToString(CultureInfo.InvariantCulture);
        _syncing = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Normalize(keepSize: true);
        Result = new ResizePartitionDialogResult
        {
            NewOffset = _region.SpanStart + _before,
            NewSize = _size
        };
        DialogResult = true;
        Close();
    }

    private static bool TryParseMb(string text, out double mb) =>
        double.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out mb);

    private static double ToMb(ulong bytes) => bytes / (1024d * 1024d);

    private static ulong FromMb(double mb) =>
        ByteSizeFormatter.AlignDown(ByteSizeFormatter.FromMegaBytes(Math.Max(0, mb)), LayoutPreview.Alignment);
}
