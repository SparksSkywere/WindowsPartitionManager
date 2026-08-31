using System.Windows;
using PartitionManager.Models;
using PartitionManager.ViewModels;

namespace PartitionManager.Views;

public partial class InitializeDiskWindow : Window
{
    public InitializeDiskDialogResult? Result { get; private set; }

    public InitializeDiskWindow(DiskViewModel disk)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        SummaryText.Text = $"Initialize Disk {disk.Number} ({disk.DetailText}) before creating partitions.";
        if (disk.Model.Size > 2UL * 1024UL * 1024UL * 1024UL * 1024UL)
        {
            GptRadio.IsChecked = true;
            MbrRadio.IsEnabled = false;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = new InitializeDiskDialogResult
        {
            Style = GptRadio.IsChecked == true ? PartitionStyleKind.Gpt : PartitionStyleKind.Mbr
        };
        DialogResult = true;
        Close();
    }
}
