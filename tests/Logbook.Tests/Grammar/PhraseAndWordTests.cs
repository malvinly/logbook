using Logbook.Core.Grammar;
using Xunit;

namespace Logbook.Tests.Grammar;

public sealed class PhraseAndWordTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 30, 0, TimeSpan.FromHours(-5));
    private static readonly TimeZoneInfo Zone = TestZones.Fixed(TimeSpan.FromHours(-5));

    [Fact]
    public void QuotedPhraseIsOneUnit()
    {
        QueryLine line = LineParser.ParseQuery("\"staging environment\" access", Today);
        Assert.Equal(["staging environment"], line.Phrases);
        Assert.Equal(["access"], line.Words);
    }

    [Fact]
    public void TokensInsideQuotesAreLiteral()
    {
        QueryLine line = LineParser.ParseQuery("\"#not-a-tag @not-a-person since:2026-01-01\"", Today);
        Assert.Equal(["#not-a-tag @not-a-person since:2026-01-01"], line.Phrases);
        Assert.Empty(line.Tags);
        Assert.Empty(line.People);
        Assert.Null(line.Since);
    }

    [Fact]
    public void UnterminatedQuoteRunsToEndOfLine()
    {
        QueryLine line = LineParser.ParseQuery("told \"no response yet", Today);
        Assert.Equal(["no response yet"], line.Phrases);
        Assert.Equal(["told"], line.Words);
    }

    [Fact]
    public void EmptyQuotesProduceNoPhrase()
    {
        Assert.Empty(LineParser.ParseQuery("\"\" thing", Today).Phrases);
    }

    [Fact]
    public void QuoteInsideAWordIsPlainText()
    {
        Assert.Equal(["5\"", "drive"], LineParser.ParseQuery("5\" drive", Today).Words);
    }

    [Fact]
    public void QuotesAreOrdinaryTextInCapture()
    {
        CaptureLine line = LineParser.ParseCapture("said \"no\" to #asked", Now, Zone);
        Assert.Equal("said \"no\" to", line.Summary);
        Assert.Equal(["asked"], line.Tags);
    }

    [Fact]
    public void BareWordsAreSplitOnWhitespace()
    {
        Assert.Equal(["staging", "access", "denied"], LineParser.ParseQuery("  staging\taccess   denied ", Today).Words);
    }

    [Fact]
    public void BareWordsKeepTheirCase()
    {
        Assert.Equal(["Staging"], LineParser.ParseQuery("Staging", Today).Words);
    }

    [Fact]
    public void SummaryIsTrimmedWithWhitespaceCollapsed()
    {
        CaptureLine line = LineParser.ParseCapture("  asked   #asked   for\taccess  ", Now, Zone);
        Assert.Equal("asked for access", line.Summary);
    }

    [Fact]
    public void EmptyLineParsesToNothing()
    {
        CaptureLine capture = LineParser.ParseCapture("", Now, Zone);
        Assert.Equal("", capture.Summary);
        Assert.Empty(capture.Tags);
        QueryLine query = LineParser.ParseQuery("", Today);
        Assert.Empty(query.Words);
        Assert.Empty(query.Phrases);
    }
}
