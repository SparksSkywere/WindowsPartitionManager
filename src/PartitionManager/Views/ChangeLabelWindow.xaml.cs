using System.Windows;
using PartitionManager.Models;
using PartitionManager.ViewModels;

namespace PartitionManager.Views;

public partial class ChangeLabelWindow : Window
{
    public LabelDialogResult? Result { get; private set; }

    public ChangeLabelWindow(PartitionViewModel partition)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        SummaryText.Text = $"Set the volume label for {partition.DisplayName}.";
        LabelBox.Text = partition.Model.Label;
        LabelBox.SelectAll();
        LabelBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = new LabelDialogResult { Label = LabelBox.Text.Trim() };
        DialogResult = true;
        Close();
    }
}
