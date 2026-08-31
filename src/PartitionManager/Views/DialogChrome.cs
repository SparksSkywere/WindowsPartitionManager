using System.Windows;
using PartitionManager.Helpers;

namespace PartitionManager.Views;

internal static class DialogChrome
{
    public static void Init(Window window)
    {
        AppIcon.ApplyTo(window);
        window.SourceInitialized += (_, _) =>
            WindowChromeHelper.ApplyTheme(window, ThemeManager.IsDarkEffective);
        ThemeManager.RefreshWindow(window);
    }
}
