using Logbook.Core.Grammar;
using Xunit;

namespace Logbook.Tests.Grammar;

/// <summary>One test per collision rule in DESIGN.md section 6, so that text which looks like a token stays text.</summary>
public sealed class CollisionRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 30, 0, TimeSpan.FromHours(-5));
    private static readonly DateOnly Today = new(2026, 10, 8);

    [Theory]
    [InlineData("ticket #12345")] // digit after #
    [InlineData("C#")] // not after whitespace
    [InlineData("someone@example.com")] // not after whitespace
    [InlineData("@ 3pm")] // space after @
    [InlineData("#")] // nothing after #
    [InlineData("#-x")] // punctuation after #
    [InlineData("#_x")]
    [InlineData("tag,#b")] // after a comma, not whitespace
    [InlineData("done (#12345)")] // after an opening paren, but a digit follows
    [InlineData("say \"@ 3pm\"")] // after an opening quote, but a space follows
    [InlineData("see [#x]")] // a bracket is not opening punctuation for this rule
    public void SigilNotAtWordStartOrNotBeforeALetterIsPlainText(string text)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now);
        Assert.Empty(line.Tags);
        Assert.Empty(line.People);
        Assert.Equal(text, line.Summary);
    }

    [Theory]
    [InlineData("#tag", "")] // start of line
    [InlineData("x #tag", "x")] // after a space
    [InlineData("x\t#tag", "x")] // after a tab
    [InlineData("x #tag y", "x y")] // mid-line
    public void SigilAtStartOfLineOrAfterWhitespaceStartsAToken(string text, string summary)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now);
        Assert.Equal(["tag"], line.Tags);
        Assert.Equal(summary, line.Summary);
    }

    [Theory]
    [InlineData("done (#asked)", "asked", "done ()")]
    [InlineData("(#asked) done", "asked", "() done")]
    [InlineData("said \"#urgent\" twice", "urgent", "said \"\" twice")]
    public void SigilAfterAnOpeningParenOrQuoteStartsATag(string text, string tag, string summary)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now);
        Assert.Equal([tag], line.Tags);
        Assert.Equal(summary, line.Summary);
    }

    [Theory]
    [InlineData("told \"@j-doe\" first", "told \"\" first")]
    [InlineData("(@j-doe) agreed", "() agreed")]
    public void SigilAfterAnOpeningParenOrQuoteStartsAPerson(string text, string summary)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now);
        Assert.Equal(["j-doe"], line.People);
        Assert.Equal(summary, line.Summary);
    }

    [Fact]
    public void OpeningParenStartsATokenInAQueryToo()
    {
        QueryLine line = LineParser.ParseQuery("(#asked) (@j-doe)", Today);
        Assert.Equal(["asked"], line.Tags);
        Assert.Equal(["j-doe"], line.People);
    }

    [Fact]
    public void InAQueryAnOpeningQuoteIsAPhraseNotAToken()
    {
        QueryLine line = LineParser.ParseQuery("\"@j-doe\"", Today);
        Assert.Empty(line.People);
        Assert.Equal(["@j-doe"], line.Phrases);
    }

    [Fact]
    public void TokenEndsAtWhitespaceOrEndOfLine()
    {
        Assert.Equal(["a", "b"], LineParser.ParseCapture("#a #b", Now).Tags);
        Assert.Equal(["a#b"], LineParser.ParseCapture("#a#b", Now).Tags);
    }

    [Theory]
    [InlineData("asked @j-doe,", ",")]
    [InlineData("asked @j-doe.", ".")]
    [InlineData("asked @j-doe;", ";")]
    [InlineData("asked @j-doe:", ":")]
    [InlineData("asked @j-doe)", ")")]
    [InlineData("asked @j-doe).", ").")]
    [InlineData("asked @j-doe\"", "\"")]
    public void TrailingPunctuationIsStrippedFromTokenAndLeftInText(string text, string leftover)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now);
        Assert.Equal(["j-doe"], line.People);
        Assert.Equal("asked " + leftover, line.Summary);
    }

    [Fact]
    public void TrailingPunctuationIsStrippedFromTagsToo()
    {
        CaptureLine line = LineParser.ParseCapture("done #asked.", Now);
        Assert.Equal(["asked"], line.Tags);
        Assert.Equal("done .", line.Summary);
    }

    [Theory]
    [InlineData("#c++", "c++")]
    [InlineData("#v1.2", "v1.2")]
    [InlineData("@j.doe", "j.doe")]
    public void PunctuationInsideATokenIsKept(string text, string expected)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now);
        Assert.Equal([expected], [.. line.Tags, .. line.People]);
    }

    [Theory]
    [InlineData("wait:15:00")] // keyword is not at a word boundary
    [InlineData("(at:15:00)")]
    [InlineData("at: 3pm")] // value does not follow the colon immediately
    [InlineData("at:")]
    [InlineData("At:15:00")] // keywords are lowercase
    public void KeywordNotAtWordBoundaryOrWithoutAValueIsPlainText(string text)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now);
        Assert.Null(line.OccurredAt);
        Assert.Equal(text, line.Summary);
    }

    [Theory]
    [InlineData("xsince:today")]
    [InlineData("since: today")]
    [InlineData("last:")]
    public void QueryKeywordNotAtWordBoundaryOrWithoutAValueIsWords(string text)
    {
        QueryLine line = LineParser.ParseQuery(text, Today);
        Assert.Null(line.Since);
        Assert.Equal(text.Split(' '), line.Words);
    }

    [Theory]
    [InlineData(@"\#notatag", "#notatag")]
    [InlineData(@"\@j-doe", "@j-doe")]
    [InlineData(@"say \#hi there", "say #hi there")]
    [InlineData(@"C:\temp\file.txt", @"C:\temp\file.txt")] // backslash elsewhere is ordinary text
    public void BackslashEscapesSigilInCapture(string text, string summary)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now);
        Assert.Empty(line.Tags);
        Assert.Empty(line.People);
        Assert.Equal(summary, line.Summary);
    }

    [Fact]
    public void BackslashEscapesSigilInQuery()
    {
        QueryLine line = LineParser.ParseQuery(@"\#tag \@j-doe", Today);
        Assert.Empty(line.Tags);
        Assert.Empty(line.People);
        Assert.Equal(["#tag", "@j-doe"], line.Words);
    }
}
