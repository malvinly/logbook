using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;

namespace Logbook.App.Theme;

/// <summary>
/// Light or dark, following the Windows "app mode" setting. WPF's Fluent theme follows that setting by itself (App.xaml
/// merges its "system" dictionary, and WPF swaps its light and dark resources when Windows changes). This class does
/// the same for Logbook's own color tokens, Theme.Light.xaml or Theme.Dark.xaml, reading the same registry value, so
/// the two always agree. It never touches the Fluent dictionary: two owners replacing it raced and crashed WPF.
/// </summary>
internal static class AppTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri TokensLight = new("pack://application:,,,/Logbook;component/Theme/Theme.Light.xaml");
    private static readonly Uri TokensDark = new("pack://application:,,,/Logbook;component/Theme/Theme.Dark.xaml");

    /// <summary>True when the dark tokens are in use.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>
    /// Reads the Windows setting and swaps the token dictionary if it changed. Called at startup and again whenever
    /// Windows broadcasts a color-set change, so open windows repaint (views use DynamicResource for brushes).
    /// App.xaml merges Fluent first, the light tokens second and Theme.xaml third; only the second entry is replaced.
    /// </summary>
    public static void FollowWindows(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        bool dark = WindowsUsesDarkApps();
        Uri wanted = dark ? TokensDark : TokensLight;
        Collection<ResourceDictionary> merged = app.Resources.MergedDictionaries;
        if (merged[1].Source != wanted)
        {
            merged[1] = new ResourceDictionary { Source = wanted };
        }

        IsDark = dark;
    }

    // A missing key or value means an older Windows without the setting, which was light.
    private static bool WindowsUsesDarkApps()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
