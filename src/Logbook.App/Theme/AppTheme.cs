using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;

namespace Logbook.App.Theme;

/// <summary>
/// Light or dark: following the Windows "app mode" setting by default, or forced by the <c>theme</c> setting
/// (DESIGN.md section 12). Following Windows, WPF's Fluent theme follows the setting by itself (App.xaml merges its
/// "system" dictionary, and WPF swaps its light and dark resources when Windows changes), and this class does the same
/// for Logbook's own color tokens, Theme.Light.xaml or Theme.Dark.xaml, reading the same registry value, so the two
/// always agree; then it never touches the Fluent dictionary, because two owners replacing it on the same Windows
/// message raced and crashed WPF. Forced, the Fluent dictionary is replaced by its light or dark variant, from which
/// WPF infers that it should stop following Windows, and the matching tokens are merged; that swap happens only at
/// startup and when Settings is saved, never while WPF handles a Windows theme change.
/// </summary>
internal static class AppTheme
{
    public const string FollowSystem = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri FluentSystem = new("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml");
    private static readonly Uri FluentLight = new("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml");
    private static readonly Uri FluentDark = new("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml");
    private static readonly Uri TokensLight = new("pack://application:,,,/Logbook;component/Theme/Theme.Light.xaml");
    private static readonly Uri TokensDark = new("pack://application:,,,/Logbook;component/Theme/Theme.Dark.xaml");

    private static string _mode = FollowSystem;

    /// <summary>True when the theme follows Windows; false while the setting forces light or dark.</summary>
    public static bool FollowsWindows => _mode == FollowSystem;

    /// <summary>True when the dark tokens are in use.</summary>
    public static bool IsDark { get; private set; }

    /// <summary><c>light</c> or <c>dark</c> as written; anything else, a missing value included, follows Windows.</summary>
    public static string Normalize(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        Light => Light,
        Dark => Dark,
        _ => FollowSystem,
    };

    /// <summary>
    /// Applies the <c>theme</c> setting. Called at startup once the settings are loaded and after Settings is saved.
    /// App.xaml merges Fluent first, the tokens second and Theme.xaml third; only the first two entries are replaced.
    /// </summary>
    public static void Apply(Application app, string? theme)
    {
        ArgumentNullException.ThrowIfNull(app);
        _mode = Normalize(theme);
        if (_mode == FollowSystem)
        {
            ReplaceIfDifferent(app, 0, FluentSystem);
            FollowWindows(app);
            return;
        }

        bool dark = _mode == Dark;
        ReplaceIfDifferent(app, 0, dark ? FluentDark : FluentLight);
        ReplaceIfDifferent(app, 1, dark ? TokensDark : TokensLight);
        IsDark = dark;
    }

    /// <summary>
    /// Following Windows, reads the Windows setting and swaps the token dictionary if it changed. Called at startup
    /// and again whenever Windows broadcasts a color-set change, so open windows repaint (views use DynamicResource
    /// for brushes). With a forced theme it does nothing: Windows changing mode must not change the app.
    /// </summary>
    public static void FollowWindows(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (_mode != FollowSystem)
        {
            return;
        }

        bool dark = WindowsUsesDarkApps();
        ReplaceIfDifferent(app, 1, dark ? TokensDark : TokensLight);
        IsDark = dark;
    }

    private static void ReplaceIfDifferent(Application app, int index, Uri wanted)
    {
        Collection<ResourceDictionary> merged = app.Resources.MergedDictionaries;
        if (merged[index].Source != wanted)
        {
            merged[index] = new ResourceDictionary { Source = wanted };
        }
    }

    // A missing key or value means an older Windows without the setting, which was light.
    private static bool WindowsUsesDarkApps()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
