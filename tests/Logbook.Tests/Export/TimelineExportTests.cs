using Logbook.Core;
using Logbook.Core.Export;
using Xunit;

namespace Logbook.Tests.Export;

/// <summary>Every toggle rendered against the fixed sample set. Expected text is exact, line for line.</summary>
public sealed class TimelineExportTests
{
    private static readonly ExportOptions Gaps = new(ShowGaps: true, GroupByMonth: false, Compact: false);
    private static readonly ExportOptions Months = new(ShowGaps: false, GroupByMonth: true, Compact: false);
    private static readonly ExportOptions Compact = new(ShowGaps: false, GroupByMonth: false, Compact: true);
    private static readonly ExportOptions Everything = new(ShowGaps: true, GroupByMonth: true, Compact: true);

    private static string Render(IEnumerable<Entry> entries, ExportOptions options, string filter = "#staging-access")
        => TimelineExport.Render(filter, entries, options);

    [Fact]
    public void NoTogglesRendersTheSampleWithoutGaps()
    {
        const string expected =
            "#staging-access — 3 entries, Oct 6 – Oct 20, 2026\n" +
            "\n" +
            "• Tue Oct 6, 2026 10:14 AM — Asked for access to the staging environment; told it would be looked into.\n" +
            "• Thu Oct 15, 2026 2:30 PM — Followed up; no response to the first ask.\n" +
            "• Tue Oct 20, 2026 9:02 AM — Asked again in standup; told it would be this week.\n" +
            "      Reason: wanted it on record before the release freeze.";
        Assert.Equal(expected, Render(SampleEntries.Sample, ExportOptions.None));
    }

    [Fact]
    public void ShowGapsAppendsDaysLaterFromTheSecondEntryOn()
    {
        // Matches the DESIGN.md section 11 sample, with the real weekdays for those dates.
        const string expected =
            "#staging-access — 3 entries, Oct 6 – Oct 20, 2026\n" +
            "\n" +
            "• Tue Oct 6, 2026 10:14 AM — Asked for access to the staging environment; told it would be looked into.\n" +
            "• Thu Oct 15, 2026 2:30 PM (9 days later) — Followed up; no response to the first ask.\n" +
            "• Tue Oct 20, 2026 9:02 AM (5 days later) — Asked again in standup; told it would be this week.\n" +
            "      Reason: wanted it on record before the release freeze.";
        Assert.Equal(expected, Render(SampleEntries.Sample, Gaps));
    }

    [Fact]
    public void GroupByMonthInsertsPerMonthCountsAndLeavesTheListUnchanged()
    {
        string text = Render(SampleEntries.All, Months, filter: "");
        string[] lines = text.Split('\n');
        Assert.Equal("All entries — 9 entries, Jul 14 – Oct 20, 2026", lines[0]);
        Assert.Equal("Jul (1) · Aug (3) · Sep (1) · Oct (4)", lines[1]);
        Assert.Equal("", lines[2]);
        Assert.Equal(Render(SampleEntries.All, ExportOptions.None, filter: "").Split('\n')[2..], lines[3..]);
    }

    [Fact]
    public void CompactDropsWeekdayAndTime()
    {
        const string expected =
            "#staging-access — 3 entries, Oct 6 – Oct 20, 2026\n" +
            "\n" +
            "• Oct 6, 2026 — Asked for access to the staging environment; told it would be looked into.\n" +
            "• Oct 15, 2026 — Followed up; no response to the first ask.\n" +
            "• Oct 20, 2026 — Asked again in standup; told it would be this week.\n" +
            "      Reason: wanted it on record before the release freeze.";
        Assert.Equal(expected, Render(SampleEntries.Sample, Compact));
    }

    [Fact]
    public void AllTogglesCombine()
    {
        const string expected =
            "#staging-access — 3 entries, Oct 6 – Oct 20, 2026\n" +
            "Oct (3)\n" +
            "\n" +
            "• Oct 6, 2026 — Asked for access to the staging environment; told it would be looked into.\n" +
            "• Oct 15, 2026 (9 days later) — Followed up; no response to the first ask.\n" +
            "• Oct 20, 2026 (5 days later) — Asked again in standup; told it would be this week.\n" +
            "      Reason: wanted it on record before the release freeze.";
        Assert.Equal(expected, Render(SampleEntries.Sample, Everything));
    }

    [Fact]
    public void InputOrderDoesNotMatterOutputIsOldestFirst()
    {
        Assert.Equal(Render(SampleEntries.Sample, Gaps), Render(SampleEntries.Sample.Reverse(), Gaps));
    }

    [Fact]
    public void BlankFilterIsTitledAllEntries()
    {
        Assert.StartsWith("All entries — 3 entries, ", Render(SampleEntries.Sample, ExportOptions.None, filter: "  "), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptySetIsJustTheHeader()
    {
        Assert.Equal("#nothing — 0 entries", Render([], Everything, filter: "#nothing"));
    }

    [Fact]
    public void SingleEntryUsesSingularAndOneDate()
    {
        Assert.StartsWith("#printer — 1 entry, Jul 14, 2026\n", Render([SampleEntries.PrinterOutage], Gaps, filter: "#printer"), StringComparison.Ordinal);
    }

    [Fact]
    public void RefsAreAppendedInBrackets()
    {
        Entry entry = SampleEntries.PrinterOutage with { Refs = ["INC0040001", "https://example.com/t/1"] };
        Assert.Contains("— Reported the printer outage to facilities [INC0040001] [https://example.com/t/1]\n", Render([entry], ExportOptions.None) + "\n", StringComparison.Ordinal);
    }

    [Fact]
    public void TagsAndPeopleAreNotRendered()
    {
        string text = Render(SampleEntries.All, Everything, filter: "");
        Assert.DoesNotContain("j-doe", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#asked", text, StringComparison.Ordinal);
        Assert.DoesNotContain("staging-access", text["All entries".Length..], StringComparison.Ordinal);
    }

    [Fact]
    public void MultiLineReasonIsIndentedOnEveryLine()
    {
        Entry entry = SampleEntries.SampleStandup with { Reason = "first line\r\nsecond line\nthird" };
        string[] lines = Render([entry], ExportOptions.None).Split('\n');
        Assert.Equal("      Reason: first line", lines[3]);
        Assert.Equal("      second line", lines[4]);
        Assert.Equal("      third", lines[5]);
    }

    [Fact]
    public void GapsAreWholeCalendarDaysInTheEntryOffset()
    {
        Entry evening = SampleEntries.Make("01K000000000000000000000Y1", new DateTimeOffset(2026, 10, 6, 23, 30, 0, TimeSpan.FromHours(-5)), "a", null, [], [], []);
        Entry morning = SampleEntries.Make("01K000000000000000000000Y2", new DateTimeOffset(2026, 10, 7, 0, 30, 0, TimeSpan.FromHours(-5)), "b", null, [], [], []);
        Entry later = SampleEntries.Make("01K000000000000000000000Y3", new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.FromHours(-5)), "c", null, [], [], []);
        string[] lines = Render([evening, morning, later], Gaps).Split('\n');
        Assert.Equal("• Tue Oct 6, 2026 11:30 PM — a", lines[2]);
        Assert.Equal("• Wed Oct 7, 2026 12:30 AM (1 day later) — b", lines[3]);
        Assert.Equal("• Wed Oct 7, 2026 9:00 AM (same day) — c", lines[4]);
    }

    [Fact]
    public void TimesRenderInTheEntryOwnOffset()
    {
        string[] lines = Render([SampleEntries.TokyoMidnight], ExportOptions.None).Split('\n');
        Assert.Equal("• Thu Oct 1, 2026 12:30 AM — Late night note from the Tokyo trip", lines[2]);
    }

    [Fact]
    public void MonthCountsCarryTheYearWhenTheSetSpansYears()
    {
        Entry december = SampleEntries.Make("01K000000000000000000000X1", new DateTimeOffset(2025, 12, 20, 9, 0, 0, TimeSpan.FromHours(-5)), "a", null, [], [], []);
        string[] lines = Render([december, SampleEntries.PrinterOutage], Months, filter: "").Split('\n');
        Assert.Equal("All entries — 2 entries, Dec 20, 2025 – Jul 14, 2026", lines[0]);
        Assert.Equal("Dec 2025 (1) · Jul 2026 (1)", lines[1]);
    }

    [Fact]
    public void OutputHasNoMarkdownAndNoTrailingNewline()
    {
        string text = Render(SampleEntries.All, Everything, filter: "");
        Assert.DoesNotContain("**", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n- ", text, StringComparison.Ordinal);
        Assert.False(text.EndsWith('\n'));
    }
}
