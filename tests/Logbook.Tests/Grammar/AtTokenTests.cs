using Logbook.Core.Grammar;
using Xunit;

namespace Logbook.Tests.Grammar;

public sealed class AtTokenTests
{
    // Thursday 8 October 2026, 14:30, UTC-5.
    private static readonly TimeSpan Offset = TimeSpan.FromHours(-5);
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 30, 0, Offset);

    [Fact]
    public void NoAtTokenLeavesOccurredAtNull()
    {
        Assert.Null(LineParser.ParseCapture("plain summary", Now).OccurredAt);
    }

    [Fact]
    public void DateOnlyStoresNoonInNowOffset()
    {
        CaptureLine line = LineParser.ParseCapture("thing at:2026-10-06", Now);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 12, 0, 0, Offset), line.OccurredAt);
        Assert.Equal("thing", line.Summary);
    }

    [Fact]
    public void DateAndTime()
    {
        CaptureLine line = LineParser.ParseCapture("at:2026-10-06,15:00 thing", Now);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 15, 0, 0, Offset), line.OccurredAt);
    }

    [Fact]
    public void Yesterday()
    {
        CaptureLine line = LineParser.ParseCapture("thing at:yesterday", Now);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 12, 0, 0, Offset), line.OccurredAt);
    }

    [Fact]
    public void YesterdayWithTwelveHourTime()
    {
        CaptureLine line = LineParser.ParseCapture("thing at:yesterday,3pm", Now);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 15, 0, 0, Offset), line.OccurredAt);
    }

    [Theory]
    [InlineData("9:30am", 9, 30)]
    [InlineData("12am", 0, 0)]
    [InlineData("12pm", 12, 0)]
    [InlineData("11:59PM", 23, 59)]
    public void TwelveHourTimeForms(string time, int hour, int minute)
    {
        CaptureLine line = LineParser.ParseCapture($"at:today,{time}", Now);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, hour, minute, 0, Offset), line.OccurredAt);
    }

    [Fact]
    public void TimeOnlyMeansTodayAtThatTime()
    {
        CaptureLine line = LineParser.ParseCapture("thing at:15:00", Now);
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
        CaptureLine line = LineParser.ParseCapture($"thing at:{weekday}", Now);
        Assert.Equal(new DateTimeOffset(2026, 10, expectedDay, 12, 0, 0, Offset), line.OccurredAt);
    }

    [Fact]
    public void WeekdayWithTime()
    {
        CaptureLine line = LineParser.ParseCapture("thing at:mon,9:00", Now);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 9, 0, 0, Offset), line.OccurredAt);
    }

    [Fact]
    public void OffsetOfNowIsKeptNotConverted()
    {
        DateTimeOffset tokyo = new(2026, 10, 8, 14, 30, 0, TimeSpan.FromHours(9));
        CaptureLine line = LineParser.ParseCapture("thing at:2026-10-06", tokyo);
        Assert.Equal(TimeSpan.FromHours(9), line.OccurredAt!.Value.Offset);
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
        CaptureLine line = LineParser.ParseCapture($"thing {token} here", Now);
        Assert.Null(line.OccurredAt);
        Assert.Equal($"thing {token} here", line.Summary);
    }

    [Theory]
    [InlineData("thing at:yesterday.", 7, 12, 0, "thing.")]
    [InlineData("thing at:yesterday,3pm!", 7, 15, 0, "thing!")]
    [InlineData("thing at:2026-10-06, here", 6, 12, 0, "thing, here")] // the space before the comma goes, as for a tag
    [InlineData("at:mon, thing", 5, 12, 0, ", thing")]
    [InlineData("thing at:15:00.", 8, 15, 0, "thing.")] // the colon inside the time is kept
    public void TrailingPunctuationIsStrippedFromTheValueAndLeftInTheSummary(string text, int day, int hour, int minute, string summary)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now);
        Assert.Equal(new DateTimeOffset(2026, 10, day, hour, minute, 0, Offset), line.OccurredAt);
        Assert.Equal(summary, line.Summary);
    }

    [Fact]
    public void LastAtTokenWins()
    {
        CaptureLine line = LineParser.ParseCapture("at:2026-10-01 thing at:2026-10-02", Now);
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
}
