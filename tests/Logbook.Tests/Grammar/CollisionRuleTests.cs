using Logbook.Core.Grammar;
using Xunit;

namespace Logbook.Tests.Grammar;

/// <summary>One test per collision rule in DESIGN.md section 6, so that text which looks like a token stays text.</summary>
public sealed class CollisionRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 30, 0, TimeSpan.FromHours(-5));
    private static readonly TimeZoneInfo Zone = TestZones.Fixed(TimeSpan.FromHours(-5));
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
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
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
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
        Assert.Equal(["tag"], line.Tags);
        Assert.Equal(summary, line.Summary);
    }

    [Theory]
    [InlineData("done (#asked)", "asked", "done")]
    [InlineData("(#asked) done", "asked", "done")]
    [InlineData("said \"#urgent\" twice", "urgent", "said twice")]
    public void SigilAfterAnOpeningParenOrQuoteStartsATag(string text, string tag, string summary)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
        Assert.Equal([tag], line.Tags);
        Assert.Equal(summary, line.Summary);
    }

    [Theory]
    [InlineData("told \"@j-doe\" first", "told first")]
    [InlineData("(@j-doe) agreed", "agreed")]
    public void SigilAfterAnOpeningParenOrQuoteStartsAPerson(string text, string summary)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
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
        Assert.Equal(["a", "b"], LineParser.ParseCapture("#a #b", Now, Zone).Tags);
        Assert.Equal(["a#b"], LineParser.ParseCapture("#a#b", Now, Zone).Tags);
    }

    [Fact]
    public void TokenEndsAtACommaOrSemicolonSoAnotherMayFollow()
    {
        CaptureLine line = LineParser.ParseCapture("asked @j-doe,@a-smith about it", Now, Zone);
        Assert.Equal(["j-doe", "a-smith"], line.People);
        Assert.Equal(["a", "b"], LineParser.ParseCapture("#a;#b", Now, Zone).Tags);
        Assert.Equal(["a", "b"], LineParser.ParseCapture("#a,#b", Now, Zone).Tags);
    }

    [Fact]
    public void TokenEndsAtACommaOrSemicolonInAQueryToo()
    {
        QueryLine line = LineParser.ParseQuery("@j-doe,@a-smith #a;#b", Today);
        Assert.Equal(["j-doe", "a-smith"], line.People);
        Assert.Equal(["a", "b"], line.Tags);
    }

    [Theory]
    [InlineData("@a,.#b", ",.#b")] // only the "," itself lets a token start; the "." after it is text
    [InlineData("@a,,#b", ",,#b")]
    public void OnlyTheSeparatorItselfLetsAnotherTokenStart(string text, string summary)
    {
        // The "," the token left touches the user's text, so it stays in the summary with it.
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
        Assert.Equal(["a"], line.People);
        Assert.Empty(line.Tags);
        Assert.Equal(summary, line.Summary);
    }

    [Fact]
    public void QuoteAfterASeparatorIsNotSwallowedInAQuery()
    {
        QueryLine line = LineParser.ParseQuery("#a,\"x y\"", Today);
        Assert.Equal(["a"], line.Tags);
        Assert.Equal(["x y"], line.Phrases);
        Assert.Empty(line.Words);
    }

    [Theory]
    [InlineData("ticket #12345,")] // the comma does not make a digit-led name a token
    [InlineData("ticket #12345,#a")] // nor does a comma that ended no token open one
    [InlineData("C#,@x")]
    public void CommaThatEndedNoTokenStartsNone(string text)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
        Assert.Empty(line.Tags);
        Assert.Empty(line.People);
        Assert.Equal(text, line.Summary);
    }

    [Theory]
    [InlineData("asked @j-doe,", ",")]
    [InlineData("asked @j-doe.", ".")]
    [InlineData("asked @j-doe;", ";")]
    [InlineData("asked @j-doe:", ":")]
    [InlineData("asked @j-doe)", ")")]
    [InlineData("asked @j-doe).", ").")]
    [InlineData("asked @j-doe\"", "\"")]
    [InlineData("asked @j-doe?", "?")]
    [InlineData("asked @j-doe!", "!")]
    [InlineData("asked @j-doe?!", "?!")]
    public void TrailingPunctuationIsStrippedFromTokenAndLeftInText(string text, string leftover)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
        Assert.Equal(["j-doe"], line.People);
        Assert.Equal("asked" + leftover, line.Summary);
    }

    [Fact]
    public void TrailingPunctuationIsStrippedFromTagsToo()
    {
        CaptureLine line = LineParser.ParseCapture("done #asked.", Now, Zone);
        Assert.Equal(["asked"], line.Tags);
        Assert.Equal("done.", line.Summary);
    }

    [Theory]
    [InlineData("asked @j-doe, then", "asked, then")]
    [InlineData("asked #staging-access; no reply", "asked; no reply")]
    [InlineData("told @j-doe and @a-smith.", "told and.")]
    [InlineData("asked @j-doe , then", "asked , then")] // a space the user typed before the comma is theirs
    [InlineData("@j-doe, then", "then")] // with no word before it, the comma has only whitespace around it
    public void SpaceBeforeStrippedPunctuationIsDroppedFromTheSummary(string text, string summary)
    {
        Assert.Equal(summary, LineParser.ParseCapture(text, Now, Zone).Summary);
    }

    [Theory]
    [InlineData("done (#asked) now", "done now")]
    [InlineData("done (#asked).", "done")]
    [InlineData("(#a #b) done", "done")]
    [InlineData("#a;#b", "")]
    [InlineData("@j-doe, @a-smith: noted", "noted")]
    [InlineData("done (#asked and more)", "done and more)")] // read literally: the "(" alone is left behind with spaces around it
    public void PunctuationATokenLeavesWithOnlyWhitespaceAroundItIsDropped(string text, string summary)
    {
        Assert.Equal(summary, LineParser.ParseCapture(text, Now, Zone).Summary);
    }

    [Theory]
    [InlineData("asked @j-doe,@a-smith about it", "asked about it")]
    [InlineData("asked @j-doe;@a-smith about it", "asked about it")]
    [InlineData("told @j-doe,#printer", "told")]
    [InlineData("done #a?,#b", "done?")] // the "?" stripped from the tag still attaches to the word before
    [InlineData("asked @j-doe,@a-smith about #printer? at:yesterday.", "asked about?.")]
    public void SeparatorBetweenTwoTokensIsDropped(string text, string summary)
    {
        Assert.Equal(summary, LineParser.ParseCapture(text, Now, Zone).Summary);
    }

    [Theory]
    [InlineData("Reported to @a-smith, @b-jones.", "Reported to,.")] // a space follows the comma, so no token follows directly
    [InlineData("asked @a-smith, @b-jones", "asked,")]
    [InlineData("asked @j-doe,ticket", "asked,ticket")] // a word, not a token, follows
    [InlineData(@"asked @j-doe,\#b", "asked,#b")] // an escaped sigil is text
    [InlineData("asked @j-doe,#12345", "asked,#12345")] // a digit-led name is not a token
    public void SeparatorNotFollowedDirectlyByATokenIsKept(string text, string summary)
    {
        Assert.Equal(summary, LineParser.ParseCapture(text, Now, Zone).Summary);
    }

    [Fact]
    public void SeparatorBetweenTwoTokensIsDroppedInAQuery()
    {
        QueryLine line = LineParser.ParseQuery("asked @j-doe,@a-smith about #printer?;\"x y\"", Today);
        Assert.Equal(["asked", "about"], line.Words);
        Assert.Equal(["j-doe", "a-smith"], line.People);
        Assert.Equal(["printer"], line.Tags);
        Assert.Equal(["x y"], line.Phrases);
    }

    [Fact]
    public void DateRangeRewriteKeepsASeparatorBetweenTwoTokens()
    {
        Assert.Equal("#a,#b;@j-doe since:2026-02-02", LineParser.WithDateRange("#a,#b;@j-doe last:7d", new DateOnly(2026, 2, 2), null));
    }

    [Theory]
    [InlineData("done (really)")] // no token, so the parens are the user's
    [InlineData("why?")]
    [InlineData("x ( #a ) y", "x ( ) y")] // punctuation typed apart from the token is not left by it
    [InlineData("see foo(#a)", "see foo()")] // the "(" touches a word, so the pair is kept
    [InlineData("asked @j-doe , then", "asked , then")]
    public void PunctuationThatIsNotLeftAloneByATokenIsKept(string text, string? summary = null)
    {
        Assert.Equal(summary ?? text, LineParser.ParseCapture(text, Now, Zone).Summary);
    }

    [Theory]
    [InlineData("(#asked)")]
    [InlineData("@j-doe,")]
    [InlineData("(@j-doe) #a;#b")]
    [InlineData("last:7d,")]
    public void FilterRequiresNoPunctuationATokenLeftAlone(string text)
    {
        Assert.Empty(LineParser.ParseQuery(text, Today).Words);
    }

    [Theory]
    [InlineData("asked @j-doe, then", new[] { "asked", "then" })]
    [InlineData("why? (really)", new[] { "why?", "(really)" })]
    [InlineData("asked , then", new[] { "asked", ",", "then" })] // typed by the user, not left by a token
    [InlineData("at:yesterday.", new[] { "at:yesterday." })] // at: is a word in a filter, punctuation and all
    public void FilterKeepsWordsThatAreNotLeftoverPunctuation(string text, string[] words)
    {
        Assert.Equal(words, LineParser.ParseQuery(text, Today).Words);
    }

    [Fact]
    public void DateRangeRewriteKeepsTheSpaceBeforeStrippedPunctuation()
    {
        // The rewrite gives back the filter text as typed; only a capture summary drops the space.
        Assert.Equal("asked @j-doe, then since:2026-02-02", LineParser.WithDateRange("asked @j-doe, then", new DateOnly(2026, 2, 2), null));
    }

    [Fact]
    public void QuestionMarkExclamationAndPossessiveAreStrippedFromTagsToo()
    {
        CaptureLine line = LineParser.ParseCapture("asked about #printer? and #printer's tray!", Now, Zone);
        Assert.Equal(["printer"], line.Tags);
        Assert.Equal("asked about? and tray!", line.Summary);
    }

    [Theory]
    [InlineData("told @j-doe's manager", "told manager")]
    [InlineData("asked @j-doe's", "asked")]
    [InlineData("asked @j-doe's.", "asked.")] // the "." is the sentence's and stays attached to the word before
    [InlineData("asked @j-doe's, then", "asked, then")]
    [InlineData("asked @j-doe's,@a-smith", "asked")]
    [InlineData("fixed #it's tray", "fixed tray")]
    public void PossessiveGoesWithTheToken(string text, string summary)
    {
        Assert.Equal(summary, LineParser.ParseCapture(text, Now, Zone).Summary);
    }

    [Fact]
    public void PossessiveStrippedFromATagLeavesTheTagName()
    {
        Assert.Equal(["it"], LineParser.ParseCapture("#it's", Now, Zone).Tags);
    }

    [Theory]
    [InlineData("told j-doe's manager", "told j-doe's manager")] // no token, so the possessive is the user's
    [InlineData("told @j-doe 's manager", "told 's manager")] // typed apart from the token
    public void PossessiveThatNoTokenStrippedStays(string text, string summary)
    {
        Assert.Equal(summary, LineParser.ParseCapture(text, Now, Zone).Summary);
    }

    [Fact]
    public void OnlyAStraightApostropheMakesAPossessive()
    {
        CaptureLine line = LineParser.ParseCapture("asked @j-doe’s", Now, Zone);
        Assert.Equal(["j-doe’s"], line.People);
        Assert.Equal("asked", line.Summary);
    }

    [Fact]
    public void FilterDropsAPossessiveWithItsToken()
    {
        QueryLine line = LineParser.ParseQuery("told @j-doe's manager", Today);
        Assert.Equal(["told", "manager"], line.Words);
        Assert.Equal(["j-doe"], line.People);
    }

    [Fact]
    public void DateRangeRewriteKeepsAPossessiveAsTyped()
    {
        Assert.Equal("told @j-doe's manager since:2026-02-02", LineParser.WithDateRange("told @j-doe's manager last:7d", new DateOnly(2026, 2, 2), null));
    }

    [Fact]
    public void FilterStripsAQuestionMarkFromAPersonAndKeepsTheWordsAround()
    {
        QueryLine line = LineParser.ParseQuery("asked @j-doe? then", Today);
        Assert.Equal(["asked", "then"], line.Words);
        Assert.Equal(["j-doe"], line.People);
    }

    [Fact]
    public void FilterStripsAPossessiveFromAPersonAndLeavesNoWord()
    {
        QueryLine line = LineParser.ParseQuery("@j-doe's", Today);
        Assert.Equal(["j-doe"], line.People);
        Assert.Empty(line.Words);
    }

    [Fact]
    public void PossessiveGoesWithAKeywordToo()
    {
        CaptureLine line = LineParser.ParseCapture("thing at:yesterday's meeting", Now, Zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.FromHours(-5)), line.OccurredAt);
        Assert.Equal("thing meeting", line.Summary);
    }

    [Fact]
    public void PossessiveOfAKeywordThatDoesNotParseStaysAsTyped()
    {
        // "since:" is a query keyword, so in a capture it is text, possessive and all.
        Assert.Equal("since:today's meeting", LineParser.ParseCapture("since:today's meeting", Now, Zone).Summary);
    }

    [Theory]
    [InlineData("@j-o'doe", "j-o'doe")] // an apostrophe inside a name is kept
    [InlineData("#rock'n'roll", "rock'n'roll")]
    [InlineData("@j-doe'", "j-doe'")] // a lone trailing apostrophe is not a possessive
    [InlineData("@j-doe's-team", "j-doe's-team")] // 's that does not end the token
    [InlineData("@j-doe'S", "j-doe's")] // only a lowercase s; the name is then lowercased
    [InlineData("#why?not", "why?not")] // ? and ! inside a token are kept
    public void ApostropheOrMarkThatIsNotTrailingStaysInTheToken(string text, string expected)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
        Assert.Equal([expected], [.. line.Tags, .. line.People]);
        Assert.Equal("", line.Summary);
    }

    [Theory]
    [InlineData("#c++", "c++")]
    [InlineData("#v1.2", "v1.2")]
    [InlineData("@j.doe", "j.doe")]
    public void PunctuationInsideATokenIsKept(string text, string expected)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
        Assert.Equal([expected], [.. line.Tags, .. line.People]);
    }

    [Theory]
    [InlineData("wait:15:00")] // keyword is not at a word boundary
    [InlineData("(at:15:00)")]
    [InlineData("at: 3pm")] // value does not follow the colon immediately
    [InlineData("at:")]
    [InlineData("at:.")] // nothing is left once the trailing punctuation goes
    [InlineData("at:?!")]
    [InlineData("At:15:00")] // keywords are lowercase
    [InlineData("at:15:")] // stripping leaves "15", which is not a time
    public void KeywordNotAtWordBoundaryOrWithoutAValueIsPlainText(string text)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
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
    [InlineData(@"done (\#asked)", "done (#asked)")] // after an opening paren a tag could start, so the escape counts
    public void BackslashEscapesSigilInCapture(string text, string summary)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
        Assert.Empty(line.Tags);
        Assert.Empty(line.People);
        Assert.Equal(summary, line.Summary);
    }

    [Theory]
    [InlineData(@"\\server\#share")] // "#" after a letter could not start a token, so nothing is escaped
    [InlineData(@"ticket \#12345")] // nor before a digit
    [InlineData(@"x\@y")]
    [InlineData(@"\# alone")]
    [InlineData(@"\@")]
    public void BackslashThatEscapesNothingIsKeptAsTyped(string text)
    {
        CaptureLine line = LineParser.ParseCapture(text, Now, Zone);
        Assert.Empty(line.Tags);
        Assert.Empty(line.People);
        Assert.Equal(text, line.Summary);
    }

    [Fact]
    public void BackslashThatEscapesNothingIsKeptInAQuery()
    {
        Assert.Equal([@"\\server\#share"], LineParser.ParseQuery(@"\\server\#share", Today).Words);
    }

    [Fact]
    public void BackslashAfterAnOpeningParenEscapesSigilInAQuery()
    {
        QueryLine line = LineParser.ParseQuery(@"(\#asked)", Today);
        Assert.Empty(line.Tags);
        Assert.Equal(["(#asked)"], line.Words);
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
