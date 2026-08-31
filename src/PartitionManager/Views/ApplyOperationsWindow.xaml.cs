using System.Windows;
using PartitionManager.Models;

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
        WarningText.Text = destructive > 0
            ? $"{destructive} destructive operation(s) will erase or convert data. Close other programs that use these volumes first."
            : "No destructive operations in this batch.";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
