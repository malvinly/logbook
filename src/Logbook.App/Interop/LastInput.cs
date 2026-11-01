using System.Runtime.InteropServices;
using Logbook.Core.Locking;

namespace Logbook.App.Interop;

/// <summary>When the user last touched the keyboard or mouse anywhere in this Windows session, for idle auto-lock.</summary>
internal static class LastInput
{
    /// <summary>The instant of the last input, expressed against <paramref name="now"/>; null if Windows cannot say.</summary>
    public static DateTimeOffset? Before(DateTimeOffset now)
    {
        NativeMethods.LastInputInfo info = new() { Size = Marshal.SizeOf<NativeMethods.LastInputInfo>() };
        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return null;
        }

        return IdleLock.LastInputBefore(now, unchecked((uint)Environment.TickCount), info.Time);
    }
}
