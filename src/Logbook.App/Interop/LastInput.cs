using System.Runtime.InteropServices;

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

        // Both are 32-bit millisecond tick counts that wrap every 49.7 days; the unchecked difference stays right
        // across a wrap. Read as signed, an input stamped after the tick count read here comes out negative, which
        // IdleLock treats as "not idle" like any last input in the future.
        int idleMilliseconds = unchecked((int)((uint)Environment.TickCount - info.Time));
        return now - TimeSpan.FromMilliseconds(idleMilliseconds);
    }
}
