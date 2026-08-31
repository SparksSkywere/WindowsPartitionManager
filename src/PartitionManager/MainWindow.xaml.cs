using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using PartitionManager.Helpers;
using PartitionManager.Services;
using PartitionManager.ViewModels;
using PartitionManager.Views;

namespace PartitionManager;

public partial class MainWindow : Window
{
    private readonly ConfigService _config;
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel, ConfigService config)
    {
        InitializeComponent();
        Title = AppInfo.ProductName;
        AppIcon.ApplyTo(this);
        DataContext = viewModel;
        _viewModel = viewModel;
        _config = config;

        viewModel.PromptCreate = p =>
        {
            var w = new CreatePartitionWindow(p, DiskInventoryService.AvailableDriveLetters(_viewModel.WorkingLayout)) { Owner = this };
            return w.ShowDialog() == true ? w.Result : null;
        };
        viewModel.PromptResize = p =>
        {
            var w = new ResizePartitionWindow(p) { Owner = this };
            return w.ShowDialog() == true ? w.Result : null;
        };
        viewModel.PromptFormat = p =>
        {
            var w = new FormatPartitionWindow(p) { Owner = this };
            return w.ShowDialog() == true ? w.Result : null;
        };
        viewModel.PromptDriveLetter = p =>
        {
            var letters = DiskInventoryService.AvailableDriveLetters(_viewModel.WorkingLayout).ToList();
            if (p.DriveLetter is char current && !letters.Contains(current))
                letters.Insert(0, current);
            var w = new ChangeDriveLetterWindow(p, letters) { Owner = this };
            return w.ShowDialog() == true ? w.Result : null;
        };
        viewModel.PromptLabel = p =>
        {
            var w = new ChangeLabelWindow(p) { Owner = this };
            return w.ShowDialog() == true ? w.Result : null;
        };
        viewModel.PromptInitialize = d =>
        {
            var w = new InitializeDiskWindow(d) { Owner = this };
            return w.ShowDialog() == true ? w.Result : null;
        };
        viewModel.PromptApply = ops =>
        {
            var w = new ApplyOperationsWindow(ops) { Owner = this };
            return w.ShowDialog() == true;
        };
        viewModel.ShowPartitionProperties = p =>
        {
            var w = new PropertiesWindow(p) { Owner = this };
            w.ShowDialog();
        };
        viewModel.ShowDiskProperties = d =>
        {
            var w = new PropertiesWindow(d) { Owner = this };
            w.ShowDialog();
        };

        SourceInitialized += (_, _) =>
            WindowChromeHelper.ApplyTheme(this, ThemeManager.IsDarkEffective);

        Loaded += async (_, _) =>
        {
            ThemeManager.RefreshWindow(this);
            if (viewModel.LoadedCommand.CanExecute(null))
                await viewModel.LoadedCommand.ExecuteAsync(null);
        };
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        var vm = new SettingsViewModel(_config);
        var window = new SettingsWindow(vm) { Owner = this };
        if (window.ShowDialog() == true && _viewModel.RefreshCommand.CanExecute(null))
            _ = _viewModel.RefreshCommand.ExecuteAsync(null);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void Feedback_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.GitHubIssuesUrl);
    private void GitHub_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.GitHubUrl);
    private void Releases_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.GitHubReleasesUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show(url, "Open in browser", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void PartitionList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.PartitionPropertiesCommand.CanExecute(null))
            _viewModel.PartitionPropertiesCommand.Execute(null);
    }

    private void MapDisk_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DiskViewModel disk })
            _viewModel.SelectDisk(disk);
    }
}
