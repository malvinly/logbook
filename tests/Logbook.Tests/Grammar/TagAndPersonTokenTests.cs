using Logbook.Core.Grammar;
using Xunit;

namespace Logbook.Tests.Grammar;

public sealed class TagAndPersonTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 30, 0, TimeSpan.FromHours(-5));
    private static readonly TimeZoneInfo Zone = TestZones.Fixed(TimeSpan.FromHours(-5));
    private static readonly DateOnly Today = new(2026, 10, 8);

    [Fact]
    public void CaptureAttachesTagAndRemovesItFromSummary()
    {
        CaptureLine line = LineParser.ParseCapture("Asked #asked for access", Now, Zone);
        Assert.Equal(["asked"], line.Tags);
        Assert.Equal("Asked for access", line.Summary);
    }

    [Fact]
    public void CaptureAttachesPersonAndRemovesItFromSummary()
    {
        CaptureLine line = LineParser.ParseCapture("Told @j-doe about it", Now, Zone);
        Assert.Equal(["j-doe"], line.People);
        Assert.Equal("Told about it", line.Summary);
    }

    [Fact]
    public void CaptureCollectsSeveralTagsAndPeopleInOrder()
    {
        CaptureLine line = LineParser.ParseCapture("#asked @j-doe #staging-access @a-smith thing", Now, Zone);
        Assert.Equal(["asked", "staging-access"], line.Tags);
        Assert.Equal(["j-doe", "a-smith"], line.People);
        Assert.Equal("thing", line.Summary);
    }

    [Theory]
    [InlineData("#Staging_Access", "staging-access")]
    [InlineData("#ASKED", "asked")]
    [InlineData("#already-kebab", "already-kebab")]
    public void TagIsLowercasedWithUnderscoresReplacedByHyphens(string input, string expected)
    {
        Assert.Equal([expected], LineParser.ParseCapture(input, Now, Zone).Tags);
    }

    [Theory]
    [InlineData("@J_Doe", "j-doe")]
    [InlineData("@JDOE", "jdoe")]
    public void PersonIsLowercasedWithUnderscoresReplacedByHyphens(string input, string expected)
    {
        Assert.Equal([expected], LineParser.ParseCapture(input, Now, Zone).People);
    }

    [Fact]
    public void NormalizerReplacesWhitespaceAndUnderscoresAndLowercases()
    {
        Assert.Equal("staging-access-now", TokenNormalizer.Normalize("Staging Access_Now"));
    }

    [Fact]
    public void NormalizeListSplitsTrimsSigilsNormalisesAndDeduplicates()
    {
        Assert.Equal(["asked", "staging-access", "j-doe"], TokenNormalizer.NormalizeList(" #Asked, staging_access;@J-Doe  asked "));
        Assert.Equal(["asked", "followup"], TokenNormalizer.NormalizeList("followup #asked", existing: ["asked"]));
        Assert.Empty(TokenNormalizer.NormalizeList("  , ; # @ "));
    }

    [Fact]
    public void NormalizeHandlesLongNamesWithoutStackPressure()
    {
        string name = new('A', 10_000);
        Assert.Equal(new string('a', 10_000), TokenNormalizer.Normalize(name));
    }

    [Fact]
    public void RepeatedTagOrPersonIsKeptOnce()
    {
        CaptureLine line = LineParser.ParseCapture("#asked #Asked @j-doe @j-doe", Now, Zone);
        Assert.Equal(["asked"], line.Tags);
        Assert.Equal(["j-doe"], line.People);
    }

    [Fact]
    public void QueryCollectsTagsAndPeople()
    {
        QueryLine line = LineParser.ParseQuery("#asked @j-doe #staging-access", Today);
        Assert.Equal(["asked", "staging-access"], line.Tags);
        Assert.Equal(["j-doe"], line.People);
        Assert.Empty(line.Words);
    }

    [Fact]
    public void LineWithOnlyTokensHasEmptySummary()
    {
        Assert.Equal("", LineParser.ParseCapture("#asked @j-doe", Now, Zone).Summary);
    }
}
