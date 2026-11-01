namespace Logbook.Core.Locking;

/// <summary>
/// The idle auto-lock decision (DESIGN.md section 9). "Idle" means no input to the Windows session at all, so working in
/// other apps keeps the log unlocked; the app reads the last input time from Windows and asks this whether to lock.
/// </summary>
public static class IdleLock
{
    /// <summary>
    /// True when <paramref name="idleLockMinutes"/> is positive and at least that long has passed since
    /// <paramref name="lastInput"/>. Zero or less never locks. A last input after <paramref name="now"/> (a clock change)
    /// is not idle. Only the instants are compared, so the offsets do not matter.
    /// </summary>
    public static bool IsDue(DateTimeOffset lastInput, DateTimeOffset now, int idleLockMinutes)
    {
        if (idleLockMinutes <= 0 || lastInput > now)
        {
            return false;
        }

        return now - lastInput >= TimeSpan.FromMinutes(idleLockMinutes);
    }
}
