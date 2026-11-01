using Logbook.Core.Grammar;
using Xunit;

namespace Logbook.Tests.Grammar;

public sealed class QueryDateTokenTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 30, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void NoDateTokensLeaveBoundsNull()
    {
        QueryLine line = LineParser.ParseQuery("thing", Today);
        Assert.Null(line.Since);
        Assert.Null(line.Until);
    }

    [Theory]
    [InlineData("since:2026-10-06", 2026, 10, 6)]
    [InlineData("since:today", 2026, 10, 8)]
    [InlineData("since:yesterday", 2026, 10, 7)]
    public void SinceForms(string token, int year, int month, int day)
    {
        QueryLine line = LineParser.ParseQuery($"thing {token}", Today);
        Assert.Equal(new DateOnly(year, month, day), line.Since);
        Assert.Equal(["thing"], line.Words);
    }

    [Theory]
    [InlineData("until:2026-10-06", 2026, 10, 6)]
    [InlineData("until:today", 2026, 10, 8)]
    [InlineData("until:yesterday", 2026, 10, 7)]
    public void UntilForms(string token, int year, int month, int day)
    {
        QueryLine line = LineParser.ParseQuery($"{token} thing", Today);
        Assert.Equal(new DateOnly(year, month, day), line.Until);
        Assert.Equal(["thing"], line.Words);
    }

    [Fact]
    public void SinceAndUntilTogether()
    {
        QueryLine line = LineParser.ParseQuery("since:2026-07-14 until:2026-10-28", Today);
        Assert.Equal(new DateOnly(2026, 7, 14), line.Since);
        Assert.Equal(new DateOnly(2026, 10, 28), line.Until);
    }

    [Theory]
    [InlineData("last:30d", 2026, 9, 8)]
    [InlineData("last:12w", 2026, 7, 16)]
    [InlineData("last:6m", 2026, 4, 8)]
    [InlineData("last:1d", 2026, 10, 7)]
    public void LastIsShorthandForSince(string token, int year, int month, int day)
    {
        QueryLine line = LineParser.ParseQuery(token, Today);
        Assert.Equal(new DateOnly(year, month, day), line.Since);
        Assert.Null(line.Until);
        Assert.Empty(line.Words);
    }

    [Fact]
    public void LastMonthsClampsToEndOfShorterMonth()
    {
        QueryLine line = LineParser.ParseQuery("last:1m", new DateOnly(2026, 3, 31));
        Assert.Equal(new DateOnly(2026, 2, 28), line.Since);
    }

    [Theory]
    [InlineData("since:mon")]
    [InlineData("since:10/06/2026")]
    [InlineData("last:0d")]
    [InlineData("last:3y")]
    [InlineData("last:d")]
    [InlineData("last:-3d")]
    [InlineData("until:")]
    [InlineData("last:999999999d")] // before year 1
    [InlineData("last:400000000w")] // 7 * count overflows int
    [InlineData("last:99999m")]
    public void UnparseableValueBecomesAWord(string token)
    {
        QueryLine line = LineParser.ParseQuery(token, Today);
        Assert.Null(line.Since);
        Assert.Null(line.Until);
        Assert.Equal([token], line.Words);
    }

    [Theory]
    [InlineData("last:7d,", 2026, 10, 1)]
    [InlineData("since:2026-10-06;", 2026, 10, 6)]
    [InlineData("since:yesterday.", 2026, 10, 7)]
    [InlineData("last:1d?!", 2026, 10, 7)]
    public void TrailingPunctuationIsStrippedFromTheValue(string token, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), LineParser.ParseQuery($"thing {token}", Today).Since);
    }

    [Fact]
    public void UntilValueIsStrippedToo()
    {
        Assert.Equal(new DateOnly(2026, 10, 8), LineParser.ParseQuery("until:today.", Today).Until);
    }

    [Theory]
    [InlineData("last:3y.")]
    [InlineData("since:mon,")]
    [InlineData("last:.")] // nothing is left once the punctuation goes
    public void UnparseableValueWithTrailingPunctuationIsOneWord(string token)
    {
        QueryLine line = LineParser.ParseQuery(token, Today);
        Assert.Null(line.Since);
        Assert.Equal([token], line.Words);
    }

    [Fact]
    public void LaterSinceOrLastOverridesEarlier()
    {
        QueryLine line = LineParser.ParseQuery("since:2026-01-01 last:7d", Today);
        Assert.Equal(new DateOnly(2026, 10, 1), line.Since);
    }

    [Theory]
    [InlineData("since:2026-10-06")]
    [InlineData("until:today")]
    [InlineData("last:30d")]
    public void QueryOnlyKeywordsAreSummaryTextInCapture(string token)
    {
        CaptureLine line = LineParser.ParseCapture($"thing {token}", Now);
        Assert.Equal($"thing {token}", line.Summary);
    }

    [Fact]
    public void QueryOnlyKeywordInCaptureKeepsItsPunctuationAndSpace()
    {
        Assert.Equal("thing since:2026-10-06; more", LineParser.ParseCapture("thing since:2026-10-06; more", Now).Summary);
    }
}
