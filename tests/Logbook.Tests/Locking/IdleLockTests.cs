using Logbook.Core.Locking;
using Xunit;

namespace Logbook.Tests.Locking;

/// <summary>Idle auto-lock decides from the last input to the Windows session (DESIGN.md section 9).</summary>
public sealed class IdleLockTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 15, 0, 0, TimeSpan.FromHours(-5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ZeroOrLessNeverLocks(int minutes)
    {
        Assert.False(IdleLock.IsDue(Now.AddDays(-30), Now, minutes));
    }

    [Fact]
    public void ExactlyNMinutesIsDue()
    {
        Assert.True(IdleLock.IsDue(Now.AddMinutes(-15), Now, 15));
    }

    [Fact]
    public void JustUnderNMinutesIsNotDue()
    {
        Assert.False(IdleLock.IsDue(Now.AddMinutes(-15).AddMilliseconds(1), Now, 15));
    }

    [Fact]
    public void JustOverNMinutesIsDue()
    {
        Assert.True(IdleLock.IsDue(Now.AddMinutes(-15).AddMilliseconds(-1), Now, 15));
    }

    [Fact]
    public void InputJustNowIsNotDue()
    {
        Assert.False(IdleLock.IsDue(Now, Now, 1));
    }

    [Fact]
    public void LastInputInTheFutureIsNotIdle()
    {
        // A clock change can put the last input after "now"; that is not evidence the user walked away.
        Assert.False(IdleLock.IsDue(Now.AddHours(2), Now, 15));
    }

    [Fact]
    public void ComparesInstantsWhateverTheOffsets()
    {
        DateTimeOffset lastInputInTokyo = new(2026, 10, 7, 4, 50, 0, TimeSpan.FromHours(9)); // 14:50 at -05:00
        Assert.False(IdleLock.IsDue(lastInputInTokyo, Now, 15));
        Assert.True(IdleLock.IsDue(lastInputInTokyo, Now, 10));
    }
}
