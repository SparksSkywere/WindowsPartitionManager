using System.Windows;

namespace PartitionManager.Views;

public partial class EncryptionRequiredWindow : Window
{
    public EncryptionRequiredWindow(string volumeName)
    {
        InitializeComponent();
        DialogChrome.Init(this);
        MessageText.Text =
            "Turn off device encryption on " + volumeName + " first.\n" +
            "Wait until it finishes, then try again.";
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        PartitionManager.Services.VolumeEncryption.OpenSettings();
        DialogResult = false;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
