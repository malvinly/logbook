using Logbook.Core;
using Logbook.Core.Export;
using Xunit;

namespace Logbook.Tests.Export;

public sealed class RangeTextTests
{
    private static Entry At(int year, int month, int day, int offsetHours = -5)
        => SampleEntries.Make("01K000000000000000000000ZZ", new DateTimeOffset(year, month, day, 10, 0, 0, TimeSpan.FromHours(offsetHours)), "x", null, [], [], []);

    [Theory]
    [InlineData(0, "0 entries")]
    [InlineData(1, "1 entry")]
    [InlineData(2, "2 entries")]
    [InlineData(12, "12 entries")]
    public void CountIsSingularOnlyForOne(int count, string expected)
    {
        Assert.Equal(expected, RangeText.Count(count));
    }

    [Fact]
    public void EmptySetHasNoRange()
    {
        Assert.Equal("", RangeText.Range([]));
    }

    [Fact]
    public void SingleDayShowsOneDate()
    {
        Assert.Equal("Oct 6, 2026", RangeText.Range([At(2026, 10, 6)]));
    }

    [Fact]
    public void SameYearShowsYearOnce()
    {
        Assert.Equal("Jul 14 – Oct 28, 2026", RangeText.Range([At(2026, 10, 28), At(2026, 7, 14)]));
    }

    [Fact]
    public void DifferentYearsShowBothYears()
    {
        Assert.Equal("Dec 20, 2025 – Jan 3, 2026", RangeText.Range([At(2025, 12, 20), At(2026, 1, 3)]));
    }

    [Fact]
    public void RangeUsesEachEntryOwnCalendarDate()
    {
        // 10:00 on Oct 1 in Tokyo is still Sep 30 in UTC-5, but the range says Oct 1.
        Assert.Equal("Sep 30 – Oct 1, 2026", RangeText.Range([At(2026, 9, 30), At(2026, 10, 1, offsetHours: 9)]));
    }
}
