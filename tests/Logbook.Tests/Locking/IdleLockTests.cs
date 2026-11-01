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

    [Fact]
    public void LastInputIsTheTickDifferenceBeforeNow()
    {
        Assert.Equal(Now.AddSeconds(-3), IdleLock.LastInputBefore(Now, tickCountNow: 5000, lastInputTick: 2000));
    }

    [Fact]
    public void SameTickIsNow()
    {
        Assert.Equal(Now, IdleLock.LastInputBefore(Now, tickCountNow: 5000, lastInputTick: 5000));
    }

    [Fact]
    public void ATickCountThatWrappedSinceTheLastInputGivesASmallIdleNotFortyNineDays()
    {
        // Last input 1000 ms before the wrap, read 500 ms after it: 1.5 s idle.
        DateTimeOffset lastInput = IdleLock.LastInputBefore(Now, tickCountNow: 500, lastInputTick: uint.MaxValue - 999);
        Assert.Equal(Now.AddMilliseconds(-1500), lastInput);
        Assert.False(IdleLock.IsDue(lastInput, Now, 1));
    }

    [Fact]
    public void ALongIdleAcrossTheWrapIsStillDue()
    {
        // Twenty minutes idle, the tick count having wrapped in between.
        uint twentyMinutes = 20 * 60 * 1000;
        DateTimeOffset lastInput = IdleLock.LastInputBefore(Now, tickCountNow: 1000, lastInputTick: unchecked(1000u - twentyMinutes));
        Assert.Equal(Now.AddMinutes(-20), lastInput);
        Assert.True(IdleLock.IsDue(lastInput, Now, 15));
    }

    [Fact]
    public void AnInputStampedAfterTheTickCountWasReadLiesAfterNowAndIsNotIdle()
    {
        DateTimeOffset lastInput = IdleLock.LastInputBefore(Now, tickCountNow: 5000, lastInputTick: 5040);
        Assert.Equal(Now.AddMilliseconds(40), lastInput);
        Assert.False(IdleLock.IsDue(lastInput, Now, 1));
    }

    [Fact]
    public void AnInputStampedAfterTheReadAcrossTheWrapAlsoLiesAfterNow()
    {
        DateTimeOffset lastInput = IdleLock.LastInputBefore(Now, tickCountNow: uint.MaxValue - 9, lastInputTick: 10);
        Assert.Equal(Now.AddMilliseconds(20), lastInput);
        Assert.False(IdleLock.IsDue(lastInput, Now, 1));
    }
}
