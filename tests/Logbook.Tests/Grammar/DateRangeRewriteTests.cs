using Logbook.Core.Grammar;
using Xunit;

namespace Logbook.Tests.Grammar;

/// <summary>The date pickers rewrite the filter's date tokens through the real scanner, never by splitting on spaces.</summary>
public sealed class DateRangeRewriteTests
{
    private static readonly DateOnly From = new(2026, 2, 2);
    private static readonly DateOnly To = new(2026, 3, 3);

    [Fact]
    public void ReplacesExistingDateTokensAndAppendsNewOnes()
    {
        Assert.Equal("foo bar since:2026-02-02 until:2026-03-03", LineParser.WithDateRange("foo since:2026-01-01 bar until:today", From, To));
    }

    [Fact]
    public void RemovesLastAsWell()
    {
        Assert.Equal("#asked since:2026-02-02", LineParser.WithDateRange("last:30d #asked", From, null));
    }

    [Fact]
    public void RemovesTheTrailingPunctuationOfARemovedToken()
    {
        Assert.Equal("#asked since:2026-02-02", LineParser.WithDateRange("last:30d, #asked", From, null));
        Assert.Equal("at:15:00. since:2026-02-02", LineParser.WithDateRange("at:15:00.", From, null)); // not a date token, so kept
    }

    [Fact]
    public void ClearingBothBoundsLeavesOnlyTheRest()
    {
        Assert.Equal("", LineParser.WithDateRange("since:today", null, null));
        Assert.Equal("a  b", LineParser.WithDateRange("a  b last:30d", null, null)); // inner spacing is kept
    }

    [Fact]
    public void QuotedPhrasesAreLeftAlone()
    {
        Assert.Equal("\"since: the outage\" foo until:2026-03-03", LineParser.WithDateRange("\"since: the outage\" foo", null, To));
    }

    [Fact]
    public void KeywordsThatAreNotTokensStay()
    {
        // "at:" is capture-only and "since: x" has no value after the colon; neither is a date token.
        Assert.Equal("at:15:00 since: x until:2026-03-03", LineParser.WithDateRange("at:15:00 since: x", null, To));
    }

    [Fact]
    public void RoundTripsThroughTheParser()
    {
        string text = LineParser.WithDateRange("#asked @j-doe \"first ask\" last:7d", From, To);
        QueryLine query = LineParser.ParseQuery(text, new DateOnly(2026, 10, 8));
        Assert.Equal(From, query.Since);
        Assert.Equal(To, query.Until);
        Assert.Equal(["asked"], query.Tags);
        Assert.Equal(["j-doe"], query.People);
        Assert.Equal(["first ask"], query.Phrases);
        Assert.Empty(query.Words);
    }

    [Theory]
    [InlineData("last:7d,#printer", "#printer since:2026-02-02")]
    [InlineData("last:7d.,#printer foo", "#printer foo since:2026-02-02")]
    [InlineData("foo since:today;@j-doe", "foo @j-doe since:2026-02-02")]
    public void RemovesTheSeparatorBetweenARemovedTokenAndATag(string text, string expected)
    {
        Assert.Equal(expected, LineParser.WithDateRange(text, From, null));
    }

    [Fact]
    public void TagAfterARemovedTokenSurvivesTheRoundTrip()
    {
        QueryLine query = LineParser.ParseQuery(LineParser.WithDateRange("last:7d,#printer", From, null), new DateOnly(2026, 10, 8));
        Assert.Equal(From, query.Since);
        Assert.Equal(["printer"], query.Tags);
        Assert.Empty(query.Words);
    }
}
