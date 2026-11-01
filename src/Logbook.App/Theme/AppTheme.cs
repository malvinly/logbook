using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;

namespace Logbook.App.Theme;

/// <summary>
/// Light or dark, following the Windows "app mode" setting. Two dictionaries change together: WPF's Fluent theme
/// (which styles the stock controls) and Logbook's own color tokens (Theme.Light.xaml or Theme.Dark.xaml). They are
/// chosen here rather than by Fluent's "follow the system" mode, so both always agree and the switch is one code path.
/// </summary>
internal static class AppTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri FluentLight = new("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml");
    private static readonly Uri FluentDark = new("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml");
    private static readonly Uri TokensLight = new("pack://application:,,,/Logbook;component/Theme/Theme.Light.xaml");
    private static readonly Uri TokensDark = new("pack://application:,,,/Logbook;component/Theme/Theme.Dark.xaml");

    /// <summary>True when the dark dictionaries are in use.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>
    /// Reads the Windows setting and swaps the two dictionaries if it changed. App.xaml merges the light pair first
    /// and Theme.xaml after them; only the first two entries are ever replaced, so Theme.xaml's styles stay put.
    /// </summary>
    public static void FollowWindows(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        bool dark = WindowsUsesDarkApps();
        Collection<ResourceDictionary> merged = app.Resources.MergedDictionaries;
        if (merged.Count >= 2 && merged[0].Source == (dark ? FluentDark : FluentLight) && merged[1].Source == (dark ? TokensDark : TokensLight))
        {
            IsDark = dark;
            return;
        }

        merged[0] = new ResourceDictionary { Source = dark ? FluentDark : FluentLight };
        merged[1] = new ResourceDictionary { Source = dark ? TokensDark : TokensLight };
        IsDark = dark;
    }

    // A missing key or value means an older Windows without the setting, which was light.
    private static bool WindowsUsesDarkApps()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
