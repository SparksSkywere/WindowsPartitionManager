using System.Windows;
using PartitionManager.Models;
using PartitionManager.ViewModels;

namespace PartitionManager.Views;

public partial class ChangeDriveLetterWindow : Window
{
    public DriveLetterDialogResult? Result { get; private set; }

    public ChangeDriveLetterWindow(PartitionViewModel partition, IReadOnlyList<char> letters)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        SummaryText.Text = $"Assign a drive letter to {partition.DisplayName}.";
        LetterBox.Items.Add("None");
        foreach (var c in letters)
            LetterBox.Items.Add($"{c}:");
        if (partition.DriveLetter is char current)
        {
            var item = $"{current}:";
            if (!LetterBox.Items.Contains(item))
                LetterBox.Items.Insert(1, item);
            LetterBox.SelectedItem = item;
        }
        else
        {
            LetterBox.SelectedIndex = LetterBox.Items.Count > 1 ? 1 : 0;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        char? letter = null;
        if (LetterBox.SelectedItem is string s && s.Length > 0 && char.IsLetter(s[0]) && s != "None")
            letter = char.ToUpperInvariant(s[0]);
        Result = new DriveLetterDialogResult { DriveLetter = letter };
        DialogResult = true;
        Close();
    }
}
