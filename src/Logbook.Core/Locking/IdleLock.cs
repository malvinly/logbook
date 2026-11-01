namespace Logbook.Core.Locking;

/// <summary>The idle check's answer; see <see cref="IdleLock.Decide"/>.</summary>
public enum IdleDecision
{
    NotDue,
    WaitForDialog,
    Lock,
}

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

    /// <summary>
    /// What the idle check does now. Not due: nothing. Due with none of the app's modal dialogs open: lock. Due with a
    /// dialog open: wait, so a half-made decision there is not closed under the user, but only until twice the idle
    /// limit has passed without input; then lock anyway and close the dialog with it (DESIGN.md section 9).
    /// </summary>
    public static IdleDecision Decide(DateTimeOffset lastInput, DateTimeOffset now, int idleLockMinutes, bool dialogOpen)
    {
        if (!IsDue(lastInput, now, idleLockMinutes))
        {
            return IdleDecision.NotDue;
        }

        if (!dialogOpen || now - lastInput >= TimeSpan.FromMinutes(idleLockMinutes) * 2)
        {
            return IdleDecision.Lock;
        }

        return IdleDecision.WaitForDialog;
    }

    /// <summary>
    /// The instant of the last input, from the Windows millisecond tick count now (<paramref name="tickCountNow"/>) and
    /// at the last input (<paramref name="lastInputTick"/>, from <c>GetLastInputInfo</c>), expressed against
    /// <paramref name="now"/>.
    /// </summary>
    public static DateTimeOffset LastInputBefore(DateTimeOffset now, uint tickCountNow, uint lastInputTick)
    {
        // Both are 32-bit tick counts that wrap every 49.7 days; the unchecked difference stays right across a wrap.
        // Read as signed, an input stamped after the tick count was read comes out negative, so the result lies after
        // now, which IsDue treats as "not idle" like any last input in the future.
        int idleMilliseconds = unchecked((int)(tickCountNow - lastInputTick));
        return now - TimeSpan.FromMilliseconds(idleMilliseconds);
    }
}
