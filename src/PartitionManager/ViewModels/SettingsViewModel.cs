using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using PartitionManager.Helpers;
using PartitionManager.Services;
using PartitionManager.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PartitionManager.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private bool _loading;

    [ObservableProperty] private bool _refreshOnLaunch;
    [ObservableProperty] private bool _showRemovable;
    [ObservableProperty] private bool _showVirtual;
    [ObservableProperty] private bool _showUnallocated;
    [ObservableProperty] private bool _showSystemReserved;
    [ObservableProperty] private bool _confirmDestructive;
    [ObservableProperty] private bool _protectSystemPartitions;
    [ObservableProperty] private string _configPath = string.Empty;
    [ObservableProperty] private string _shortcutStatus = string.Empty;
    [ObservableProperty] private ThemeOption? _selectedTheme;

    public ObservableCollection<ThemeOption> ThemeOptions { get; } = new(ThemeCatalog.PickerOptions);

    public SettingsViewModel(ConfigService config)
    {
        _config = config;
        LoadFromConfig();
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (_loading || value is null) return;
        ThemeManager.Apply(value.Id);
    }

    public void RevertThemePreview() =>
        ThemeManager.Apply(_config.Config.General.Theme);

    [RelayCommand]
    private void Save()
    {
        var c = _config.Config;
        c.General.Theme = SelectedTheme?.Id ?? ThemeCatalog.SystemId;
        c.General.RefreshOnLaunch = RefreshOnLaunch;
        c.Display.ShowRemovable = ShowRemovable;
        c.Display.ShowVirtual = ShowVirtual;
        c.Display.ShowUnallocated = ShowUnallocated;
        c.Display.ShowSystemReserved = ShowSystemReserved;
        c.Safety.ConfirmDestructive = ConfirmDestructive;
        c.Safety.ProtectSystemPartitions = ProtectSystemPartitions;
        _config.Save();
        ThemeManager.Apply(c.General.Theme);
    }

    [RelayCommand]
    private void OpenFeedback() => OpenUrl(AppInfo.GitHubIssuesUrl);

    [RelayCommand]
    private void CreateDesktopShortcut()
    {
        try
        {
            DesktopShortcutHelper.CreateShortcut();
            UpdateShortcutStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RemoveDesktopShortcut()
    {
        try
        {
            DesktopShortcutHelper.RemoveShortcut();
            UpdateShortcutStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadFromConfig()
    {
        _loading = true;
        var c = _config.Config;
        RefreshOnLaunch = c.General.RefreshOnLaunch;
        ShowRemovable = c.Display.ShowRemovable;
        ShowVirtual = c.Display.ShowVirtual;
        ShowUnallocated = c.Display.ShowUnallocated;
        ShowSystemReserved = c.Display.ShowSystemReserved;
        ConfirmDestructive = c.Safety.ConfirmDestructive;
        ProtectSystemPartitions = c.Safety.ProtectSystemPartitions;
        ConfigPath = _config.ConfigPath;
        var id = ThemeCatalog.NormalizeId(c.General.Theme);
        SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Id == id) ?? ThemeOptions[0];
        UpdateShortcutStatus();
        _loading = false;
    }

    private void UpdateShortcutStatus() =>
        ShortcutStatus = DesktopShortcutHelper.ShortcutExists()
            ? "Desktop shortcut is present."
            : "No desktop shortcut.";

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
}
