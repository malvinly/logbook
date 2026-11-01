using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace Logbook.App.Interop;

/// <summary>Where to put the capture popup and the toast: the work area of the monitor under the cursor.</summary>
internal static class Monitors
{
    /// <summary>The work area under the cursor in device-independent units, using the given device-to-DIP transform.</summary>
    public static Rect WorkAreaAtCursor(Matrix fromDevice)
    {
        NativeMethods.GetCursorPos(out NativeMethods.Point cursor);
        IntPtr monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        NativeMethods.MonitorInfo info = new() { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            return SystemParameters.WorkArea;
        }

        Point topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
        Point bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
        return new Rect(topLeft, bottomRight);
    }
}
