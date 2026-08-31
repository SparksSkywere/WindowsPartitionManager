using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PartitionManager.Helpers;
using PartitionManager.Models;
using PartitionManager.Services;
using PartitionManager.ViewModels;

namespace PartitionManager.Views;

public partial class CreatePartitionWindow : Window
{
    private readonly ulong _maxBytes;
    private bool _syncing;

    public CreatePartitionDialogResult? Result { get; private set; }

    public CreatePartitionWindow(PartitionViewModel unallocated, IReadOnlyList<char> freeLetters)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        _maxBytes = unallocated.Size;
        SummaryText.Text =
            $"Create a partition in {ByteSizeFormatter.Format(unallocated.Size)} of unallocated space on Disk {unallocated.DiskNumber}.";
        SizeSlider.Maximum = Math.Max(1, _maxBytes / (1024d * 1024d));
        SizeSlider.Value = SizeSlider.Maximum;
        SizeBox.Text = ((int)SizeSlider.Value).ToString(CultureInfo.InvariantCulture);
        SizeHint.Text = $"Maximum {ByteSizeFormatter.Format(_maxBytes)}";

        LetterBox.Items.Add("None");
        foreach (var c in freeLetters)
            LetterBox.Items.Add($"{c}:");
        LetterBox.SelectedIndex = LetterBox.Items.Count > 1 ? 1 : 0;

        foreach (var fs in new[] { "NTFS", "FAT32", "exFAT", "ReFS" })
            FileSystemBox.Items.Add(fs);
        FileSystemBox.SelectedIndex = 0;
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
        if (!double.TryParse(SizeBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var mb) || mb < 1)
        {
            MessageBox.Show("Enter a size of at least 1 MB.", AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var bytes = ByteSizeFormatter.AlignDown(ByteSizeFormatter.FromMegaBytes(mb), LayoutPreview.Alignment);
        if (bytes < LayoutPreview.Alignment)
            bytes = LayoutPreview.Alignment;
        if (bytes > _maxBytes)
            bytes = ByteSizeFormatter.AlignDown(_maxBytes, LayoutPreview.Alignment);

        char? letter = null;
        if (LetterBox.SelectedItem is string s && s.Length > 0 && char.IsLetter(s[0]) && s != "None")
            letter = s[0];

        Result = new CreatePartitionDialogResult
        {
            Size = bytes,
            DriveLetter = letter,
            Label = LabelBox.Text.Trim(),
            FileSystem = FileSystemBox.SelectedItem as string ?? "NTFS",
            FormatAfterCreate = FormatBox.IsChecked == true,
            QuickFormat = QuickBox.IsChecked == true
        };
        DialogResult = true;
        Close();
    }
}
