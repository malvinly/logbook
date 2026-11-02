using Logbook.Core.Grammar;
using Xunit;

namespace Logbook.Tests.Grammar;

public sealed class AtTokenTests
{
    // Thursday 8 October 2026, 14:30, UTC-5.
    private static readonly TimeSpan Offset = TimeSpan.FromHours(-5);
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 30, 0, Offset);
    private static readonly TimeZoneInfo Zone = TestZones.Fixed(Offset);

    [Fact]
    public void NoAtTokenLeavesOccurredAtNull()
    {
        Assert.Null(LineParser.ParseCapture("plain summary", Now, Zone).OccurredAt);
    }

    [Fact]
    public void DateOnlyStoresNoonInNowOffset()
    {
        CaptureLine line = LineParser.ParseCapture("thing at:2026-10-06", Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 12, 0, 0, Offset), line.OccurredAt);
        Assert.Equal("thing", line.Summary);
    }

    [Fact]
    public void DateAndTime()
    {
        CaptureLine line = LineParser.ParseCapture("at:2026-10-06,15:00 thing", Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 15, 0, 0, Offset), line.OccurredAt);
    }

    [Fact]
    public void Yesterday()
    {
        CaptureLine line = LineParser.ParseCapture("thing at:yesterday", Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 12, 0, 0, Offset), line.OccurredAt);
    }

    [Fact]
    public void YesterdayWithTwelveHourTime()
    {
        CaptureLine line = LineParser.ParseCapture("thing at:yesterday,3pm", Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 15, 0, 0, Offset), line.OccurredAt);
    }

    [Theory]
    [InlineData("9:30am", 9, 30)]
    [InlineData("12am", 0, 0)]
    [InlineData("12pm", 12, 0)]
    [InlineData("11:59PM", 23, 59)]
    public void TwelveHourTimeForms(string time, int hour, int minute)
    {
        CaptureLine line = LineParser.ParseCapture($"at:today,{time}", Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, hour, minute, 0, Offset), line.OccurredAt);
    }

    [Fact]
    public void TimeOnlyMeansTodayAtThatTime()
    {
        CaptureLine line = LineParser.ParseCapture("thing at:15:00", Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 15, 0, 0, Offset), line.OccurredAt);
    }

    [Theory]
    [InlineData("mon", 5)]
    [InlineData("wed", 7)]
    [InlineData("thu", 1)] // Today is Thursday; a weekday name always means a past day, so last Thursday.
    [InlineData("fri", 2)]
    [InlineData("sun", 4)]
    public void WeekdayMeansMostRecentSuchDayBeforeTodayAtNoon(string weekday, int expectedDay)
    {
        CaptureLine line = LineParser.ParseCapture($"thing at:{weekday}", Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, expectedDay, 12, 0, 0, Offset), line.OccurredAt);
    }

    [Fact]
    public void WeekdayWithTime()
    {
        CaptureLine line = LineParser.ParseCapture("thing at:mon,9:00", Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 9, 0, 0, Offset), line.OccurredAt);
    }

    [Fact]
    public void OffsetOfTheZoneIsKeptNotConverted()
    {
        DateTimeOffset tokyo = new(2026, 10, 8, 14, 30, 0, TimeSpan.FromHours(9));
        CaptureLine line = LineParser.ParseCapture("thing at:2026-10-06", tokyo, TestZones.Fixed(TimeSpan.FromHours(9)));
        Assert.Equal(TimeSpan.FromHours(9), line.OccurredAt!.Value.Offset);
    }

    // Clock changes, in TestZones.Central: UTC-5 in summer, UTC-6 in winter; 2026 changes on 8 March and 1 November.
    private static readonly TimeSpan Daylight = TimeSpan.FromHours(-5);
    private static readonly TimeSpan Standard = TimeSpan.FromHours(-6);

    private static DateTimeOffset? AtInCentral(string token, DateTimeOffset now)
        => LineParser.ParseCapture($"thing {token}", now, TestZones.Central).OccurredAt;

    [Fact]
    public void YesterdayTypedTheDayAfterFallingBackCarriesYesterdaysOffset()
    {
        DateTimeOffset now = new(2026, 11, 1, 10, 0, 0, Standard); // Sunday morning, clocks went back at 02:00
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 15, 0, 0, Daylight), AtInCentral("at:yesterday,3pm", now));
    }

    [Fact]
    public void ADateAfterSpringingForwardTypedBeforeItCarriesThePostChangeOffset()
    {
        DateTimeOffset now = new(2026, 3, 6, 10, 0, 0, Standard); // Friday before the change
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 15, 0, 0, Daylight), AtInCentral("at:2026-03-10,15:00", now));
    }

    [Fact]
    public void ADateWithoutATimeStoresNoonInThatDatesOffset()
    {
        DateTimeOffset now = new(2026, 3, 6, 10, 0, 0, Standard);
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 12, 0, 0, Daylight), AtInCentral("at:2026-03-10", now));
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 12, 0, 0, Daylight), AtInCentral("at:2026-10-30", new DateTimeOffset(2026, 11, 2, 9, 0, 0, Standard)));
    }

    [Fact]
    public void ATimeTodayTakesTheOffsetAtThatTimeNotNow()
    {
        DateTimeOffset now = new(2026, 11, 1, 1, 0, 0, Daylight); // Sunday 01:00, an hour before clocks go back
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 15, 0, 0, Standard), AtInCentral("at:15:00", now));
    }

    [Fact]
    public void AWeekdayTakesTheOffsetOfThatDate()
    {
        DateTimeOffset now = new(2026, 11, 2, 9, 0, 0, Standard); // Monday after the change; at:mon is the Monday before
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 12, 0, 0, Daylight), AtInCentral("at:mon", now));
    }

    [Fact]
    public void ATimeInTheSkippedHourGetsTheStandardOffset()
    {
        // 02:30 on 8 March does not exist on the wall clock; the rule is TimeZoneInfo.GetUtcOffset's: standard time.
        DateTimeOffset now = new(2026, 3, 9, 9, 0, 0, Daylight);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 2, 30, 0, Standard), AtInCentral("at:2026-03-08,2:30", now));
    }

    [Fact]
    public void ATimeInTheRepeatedHourGetsTheStandardOffset()
    {
        // 01:30 on 1 November happens twice; the rule is TimeZoneInfo.GetUtcOffset's: the second, standard-time one.
        DateTimeOffset now = new(2026, 11, 2, 9, 0, 0, Standard);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 1, 30, 0, Standard), AtInCentral("at:2026-11-01,1:30", now));
    }

    [Fact]
    public void WithNoClockChangeBetweenThemTheOffsetIsNows()
    {
        DateTimeOffset now = new(2026, 11, 4, 9, 0, 0, Standard);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 15, 0, 0, Standard), AtInCentral("at:yesterday,3pm", now));
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 15, 0, 0, Standard), AtInCentral("at:15:00", now));
    }

    [Theory]
    [InlineData("at:foo")]
    [InlineData("at:2026-13-40")]
    [InlineData("at:25:00")]
    [InlineData("at:foo.")] // stripping the punctuation does not make a bad value good
    [InlineData("at:25:00,")]
    [InlineData("at:9999-12-31,23:00")] // past the last representable instant once the negative offset is applied
    public void UnparseableValueStaysInSummary(string token)
    {
        CaptureLine line = LineParser.ParseCapture($"thing {token} here", Now, Zone);
        Assert.Null(line.OccurredAt);
        Assert.Equal($"thing {token} here", line.Summary);
    }

    [Theory]
    [InlineData("thing at:yesterday.", 7, 12, 0, "thing.")]
    [InlineData("thing at:yesterday,3pm!", 7, 15, 0, "thing!")]
    [InlineData("thing at:2026-10-06, here", 6, 12, 0, "thing, here")] // the space before the comma goes, as for a tag
    [InlineData("at:mon, thing", 5, 12, 0, "thing")] // nothing before the comma to keep it, so it is dropped
    [InlineData("thing at:15:00.", 8, 15, 0, "thing.")] // the colon inside the time is kept
    public void TrailingPunctuationIsStrippedFromTheValueAndLeftInTheSummary(string text, int day, int hour, int minute, string summary)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, day, hour, minute, 0, Offset), line.OccurredAt);
        Assert.Equal(summary, line.Summary);
    }

    [Fact]
    public void LastAtTokenWins()
    {
        CaptureLine line = LineParser.ParseCapture("at:2026-10-01 thing at:2026-10-02", Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 12, 0, 0, Offset), line.OccurredAt);
    }

    [Fact]
    public void AtIsCaptureOnlySoQueryTreatsItAsAWord()
    {
        QueryLine line = LineParser.ParseQuery("at:15:00 thing", new DateOnly(2026, 10, 8));
        Assert.Equal(["at:15:00", "thing"], line.Words);
    }

    [Fact]
    public void AtInAQueryKeepsItsTrailingPunctuationInTheWord()
    {
        QueryLine line = LineParser.ParseQuery("at:yesterday. thing", new DateOnly(2026, 10, 8));
        Assert.Equal(["at:yesterday.", "thing"], line.Words);
    }

    [Theory]
    [InlineData("fixed at:yesterday,#printer", "fixed")] // the comma between the two tokens is dropped
    [InlineData("fixed at:yesterday.,#printer", "fixed.")] // the "." stripped from the value attaches to the word before
    public void AtValueEndsAtASeparatorBeforeATag(string text, string summary)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 12, 0, 0, Offset), line.OccurredAt);
        Assert.Equal(["printer"], line.Tags);
        Assert.Equal(summary, line.Summary);
    }

    [Fact]
    public void AtValueKeepsItsOwnCommaBeforeASeparatorAndAPerson()
    {
        CaptureLine line = LineParser.ParseCapture("met at:yesterday,3pm;@j-doe", Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 15, 0, 0, Offset), line.OccurredAt);
        Assert.Equal(["j-doe"], line.People);
        Assert.Equal("met", line.Summary);
    }

    [Fact]
    public void AtValueRunsOnPastACommaNotBeforeATagOrPerson()
    {
        CaptureLine line = LineParser.ParseCapture("fixed at:yesterday,printer", Now, Zone);
        Assert.Null(line.OccurredAt);
        Assert.Equal("fixed at:yesterday,printer", line.Summary);
    }
}
