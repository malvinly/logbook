using Logbook.Core;
using Logbook.Core.Grammar;
using Logbook.Core.Query;
using Xunit;

namespace Logbook.Tests.Query;

public sealed class EntryQueryTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);

    private static IEnumerable<string> Run(string filter) => EntryQuery.Filter(SampleEntries.All, LineParser.ParseQuery(filter, Today)).Select(e => e.Id);

    private static string Id(Entry entry) => entry.Id;

    [Fact]
    public void EmptyFilterReturnsEverythingNewestFirst()
    {
        Assert.Equal(SampleEntries.All.Reverse().Select(Id), Run(""));
    }

    [Fact]
    public void TagIsAnExactMatch()
    {
        Assert.Equal([SampleEntries.Escalated, SampleEntries.ToldAboutOutages, SampleEntries.PrinterOutage], EntryQuery.Filter(SampleEntries.All, LineParser.ParseQuery("#printer", Today)));
        Assert.Empty(Run("#print"));
        Assert.Empty(Run("#printers"));
    }

    [Fact]
    public void MultipleTagsAreAnded()
    {
        Assert.Equal([SampleEntries.Escalated.Id], Run("#printer #decided"));
    }

    [Fact]
    public void PersonFilters()
    {
        Assert.Equal([SampleEntries.ToldAboutOutages.Id, SampleEntries.PrinterOutage.Id], Run("@a-smith"));
    }

    [Fact]
    public void TagAndPersonCombine()
    {
        Assert.Equal([SampleEntries.ToldAboutOutages.Id], Run("#told @a-smith"));
        Assert.Empty(Run("#told @j-doe"));
    }

    [Fact]
    public void BareWordIsCaseInsensitiveSubstringOverSummaryAndReason()
    {
        Assert.Equal([SampleEntries.ToldAboutOutages.Id, SampleEntries.PrinterOutage.Id], Run("outage"));
        Assert.Equal([SampleEntries.ToldAboutOutages.Id], Run("numbers")); // only in the reason, different case
        Assert.Equal([SampleEntries.ToldAboutOutages.Id], Run("NUMBERS"));
    }

    [Fact]
    public void MultipleBareWordsAreAnded()
    {
        Assert.Equal([SampleEntries.ToldAboutOutages.Id], Run("outage budget"));
        Assert.Empty(Run("outage staging"));
    }

    [Fact]
    public void QuotedPhraseMatchesAsOneUnit()
    {
        Assert.Equal([SampleEntries.SampleFollowUp.Id], Run("\"no response\""));
        Assert.Empty(Run("\"response no\""));
        Assert.Equal([SampleEntries.SampleFollowUp.Id], Run("\"NO RESPONSE\""));
    }

    [Fact]
    public void SinceIsInclusive()
    {
        Assert.Equal([SampleEntries.SampleStandup.Id, SampleEntries.SampleFollowUp.Id, SampleEntries.SampleAsk.Id], Run("since:2026-10-06"));
        Assert.Equal([SampleEntries.SampleStandup.Id, SampleEntries.SampleFollowUp.Id], Run("since:2026-10-07"));
    }

    [Fact]
    public void UntilIsInclusive()
    {
        Assert.Equal([SampleEntries.FirstAsk.Id, SampleEntries.PrinterOutage.Id], Run("until:2026-08-03"));
        Assert.Equal([SampleEntries.PrinterOutage.Id], Run("until:2026-08-02"));
    }

    [Fact]
    public void SinceAndUntilBoundBothEnds()
    {
        Assert.Equal([SampleEntries.Escalated.Id, SampleEntries.ToldAboutOutages.Id], Run("since:2026-08-04 until:2026-09-08"));
    }

    [Fact]
    public void LastIsAnchoredToToday()
    {
        // today is Oct 28; last:30d is since Sep 28
        Assert.Equal([SampleEntries.SampleStandup.Id, SampleEntries.SampleFollowUp.Id, SampleEntries.SampleAsk.Id, SampleEntries.TokyoMidnight.Id], Run("last:30d"));
    }

    [Fact]
    public void DateBoundsUseTheEntryOwnOffsetNotUtc()
    {
        // Oct 1 00:30 in Tokyo is still Sep 30 in UTC, but the entry says it happened on Oct 1.
        Assert.Contains(SampleEntries.TokyoMidnight.Id, Run("since:2026-10-01 until:2026-10-01"));
        Assert.DoesNotContain(SampleEntries.TokyoMidnight.Id, Run("until:2026-09-30"));
    }

    [Fact]
    public void AllTokenKindsCombine()
    {
        Assert.Equal([SampleEntries.SampleFollowUp.Id], Run("#asked @j-doe since:2026-10-01 until:2026-10-19 \"first ask\" followed"));
    }

    [Fact]
    public void ResultsAreNewestFirstByOccurredAtAsAnInstant()
    {
        // Tokyo midnight Oct 1 (+9) is Sep 30 15:30 UTC, so it sorts after Sep 9 and before Oct 6.
        Assert.Equal([SampleEntries.SampleAsk.Id, SampleEntries.TokyoMidnight.Id, SampleEntries.SecondAsk.Id], Run("since:2026-09-01 until:2026-10-06"));
    }
}
