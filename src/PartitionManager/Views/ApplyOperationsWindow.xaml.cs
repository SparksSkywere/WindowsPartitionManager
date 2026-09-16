using System.Windows;
using PartitionManager.Helpers;
using PartitionManager.Models;
using PartitionManager.Services;

namespace PartitionManager.Views;

public partial class ApplyOperationsWindow : Window
{
    public ApplyOperationsWindow(IReadOnlyList<PendingOperation> operations)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        foreach (var op in operations)
            OpList.Items.Add((op.IsDestructive ? "⚠ " : "") + op.Description);

        var destructive = operations.Count(o => o.IsDestructive);
        var recovery = operations.Any(OfflineApplyService.IsBootStartMove);
        if (recovery && SessionMode.IsRemoteDesktop())
            WarningText.Text = "A Windows volume move needs a local session. Cancel or remove that operation first.";
        else if (recovery)
            WarningText.Text = "Moving the Windows volume will restart this PC to finish.";
        else if (destructive > 0)
            WarningText.Text = $"{destructive} operation(s) can erase or change data. Close programs using these volumes first.";
        else
            WarningText.Text = "No destructive operations in this batch.";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
