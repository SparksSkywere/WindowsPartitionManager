using System.Windows;
using PartitionManager.Models;
using PartitionManager.ViewModels;

namespace PartitionManager.Views;

public partial class FormatPartitionWindow : Window
{
    public FormatPartitionDialogResult? Result { get; private set; }

    public FormatPartitionWindow(PartitionViewModel partition)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        SummaryText.Text = $"Format {partition.DisplayName} ({partition.SizeText}).";
        foreach (var fs in new[] { "NTFS", "FAT32", "exFAT", "ReFS" })
            FileSystemBox.Items.Add(fs);
        var preferred = string.IsNullOrWhiteSpace(partition.Model.FileSystem)
            ? partition.Kind == SegmentKind.Efi ? "FAT32" : "NTFS"
            : partition.Model.FileSystem.ToUpperInvariant();
        if (preferred is "FAT" or "FAT16")
            preferred = "FAT32";
        FileSystemBox.SelectedItem = preferred;
        if (FileSystemBox.SelectedIndex < 0)
            FileSystemBox.SelectedIndex = 0;
        LabelBox.Text = partition.Model.Label;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = new FormatPartitionDialogResult
        {
            FileSystem = FileSystemBox.SelectedItem as string ?? "NTFS",
            Label = LabelBox.Text.Trim(),
            QuickFormat = QuickBox.IsChecked == true
        };
        DialogResult = true;
        Close();
    }
}
