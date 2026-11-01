using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Logbook.App.Theme;

namespace Logbook.App.Interop;

/// <summary>
/// Acrylic and rounded corners for a borderless window (the capture box). WPF's Fluent theme does this for windows
/// with standard chrome, but not for a <see cref="WindowChrome"/> window with no frame, so two DWM attributes are set
/// here. Windows 11 rounds the corners; Windows 11 22H2 and later also draws acrylic behind the window. Anything older
/// keeps the window's own opaque background and square corners: nothing fails, only the translucency is missing.
/// </summary>
internal static class Backdrop
{
    /// <summary>Call from the window's constructor. The attributes need the window handle, so they are set once it exists.</summary>
    public static void UseAcrylic(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SourceInitialized += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Set(hwnd, NativeMethods.DwmwaWindowCornerPreference, NativeMethods.DwmwcpRound);
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            return;
        }

        // The tint follows the app theme, not the window's own (WPF does not know this window is themed).
        Set(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, AppTheme.IsDark ? 1 : 0);
        if (!Set(hwnd, NativeMethods.DwmwaSystemBackdropType, NativeMethods.DwmsbtTransientWindow))
        {
            return;
        }

        // Only now give up the opaque background: the compositor draws the backdrop where the glass frame reaches and
        // WPF leaves the pixels transparent, so the frame is extended over the whole window.
        HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
        WindowChrome.GetWindowChrome(window).GlassFrameThickness = new Thickness(-1);
        window.Background = Brushes.Transparent;
    }

    /// <summary>Sets the tint again after the app theme changed while the window is open.</summary>
    public static void RefreshTint(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            Set(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, AppTheme.IsDark ? 1 : 0);
        }
    }

    private static bool Set(IntPtr hwnd, int attribute, int value) => NativeMethods.DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) >= 0;
}
