using Logbook.Core;
using Logbook.Core.Export;
using Xunit;

namespace Logbook.Tests.Export;

/// <summary>
/// The HTML clipboard format: the same timeline as <see cref="TimelineExport"/>, as a fragment with a real list and
/// bold dates. Expected markup is exact, line for line.
/// </summary>
public sealed class TimelineHtmlTests
{
    private static readonly ExportOptions Gaps = new(ShowGaps: true, GroupByMonth: false, Compact: false, TagsAndPeople: false);
    private static readonly ExportOptions Months = new(ShowGaps: false, GroupByMonth: true, Compact: false, TagsAndPeople: false);
    private static readonly ExportOptions Compact = new(ShowGaps: false, GroupByMonth: false, Compact: true, TagsAndPeople: false);
    private static readonly ExportOptions TagsAndPeople = new(ShowGaps: false, GroupByMonth: false, Compact: false, TagsAndPeople: true);
    private static readonly ExportOptions Everything = new(ShowGaps: true, GroupByMonth: true, Compact: true, TagsAndPeople: true);

    private static string Render(IEnumerable<Entry> entries, ExportOptions options, string filter = "#staging-access")
        => TimelineHtml.Render(filter, entries, options);

    [Fact]
    public void NoTogglesRendersAParagraphAndAList()
    {
        const string expected =
            "<p>#staging-access — 3 entries, Oct 6 – Oct 20, 2026</p>\n" +
            "<ul>\n" +
            "<li><b>Tue Oct 6, 2026 10:14 AM</b> — Asked for access to the staging environment; told it would be looked into.</li>\n" +
            "<li><b>Thu Oct 15, 2026 2:30 PM</b> — Followed up; no response to the first ask.</li>\n" +
            "<li><b>Tue Oct 20, 2026 9:02 AM</b> — Asked again in standup; told it would be this week.<div>Reason: wanted it on record before the release freeze.</div></li>\n" +
            "</ul>";
        Assert.Equal(expected, Render(SampleEntries.Sample, ExportOptions.None));
    }

    [Fact]
    public void ShowGapsPutsTheGapAfterTheBoldDate()
    {
        string[] lines = Render(SampleEntries.Sample, Gaps).Split('\n');
        Assert.Equal("<li><b>Tue Oct 6, 2026 10:14 AM</b> — Asked for access to the staging environment; told it would be looked into.</li>", lines[2]);
        Assert.Equal("<li><b>Thu Oct 15, 2026 2:30 PM</b> (9 days later) — Followed up; no response to the first ask.</li>", lines[3]);
        Assert.StartsWith("<li><b>Tue Oct 20, 2026 9:02 AM</b> (5 days later) — Asked again", lines[4], StringComparison.Ordinal);
    }

    [Fact]
    public void GapAcrossOffsetsIsNeverNegative()
    {
        Entry central = SampleEntries.Make("01K000000000000000000000Y4", new DateTimeOffset(2026, 10, 6, 23, 30, 0, TimeSpan.FromHours(-5)), "a", null, [], [], []);
        Entry tokyo = SampleEntries.Make("01K000000000000000000000Y5", new DateTimeOffset(2026, 10, 7, 1, 0, 0, TimeSpan.FromHours(9)), "b", null, [], [], []);
        string[] lines = Render([central, tokyo], Gaps).Split('\n');
        Assert.Equal("<li><b>Wed Oct 7, 2026 1:00 AM</b> — b</li>", lines[2]);
        Assert.Equal("<li><b>Tue Oct 6, 2026 11:30 PM</b> (same day) — a</li>", lines[3]);
    }

    [Fact]
    public void GroupByMonthAddsTheCountsToTheHeaderParagraph()
    {
        string[] lines = Render(SampleEntries.All, Months, filter: "").Split('\n');
        Assert.Equal("<p>All entries — 9 entries, Jul 14 – Oct 20, 2026<br>Jul (1) · Aug (3) · Sep (1) · Oct (4)</p>", lines[0]);
        Assert.Equal(Render(SampleEntries.All, ExportOptions.None, filter: "").Split('\n')[1..], lines[1..]);
    }

    [Fact]
    public void CompactDropsWeekdayAndTime()
    {
        string[] lines = Render(SampleEntries.Sample, Compact).Split('\n');
        Assert.Equal("<li><b>Oct 6, 2026</b> — Asked for access to the staging environment; told it would be looked into.</li>", lines[2]);
        Assert.Equal("<li><b>Oct 15, 2026</b> — Followed up; no response to the first ask.</li>", lines[3]);
    }

    [Fact]
    public void TagsAndPeopleComeAfterTheRefs()
    {
        string[] lines = Render([SampleEntries.PrinterOutage], TagsAndPeople, filter: "#printer").Split('\n');
        Assert.Equal("<li><b>Tue Jul 14, 2026 9:00 AM</b> — Reported the printer outage to facilities [INC0040001] #reported #printer @a-smith</li>", lines[2]);
    }

    [Fact]
    public void TagsAndPeopleOffLeavesThemOut()
    {
        string html = Render(SampleEntries.All, Everything with { TagsAndPeople = false }, filter: "");
        Assert.DoesNotContain("@j-doe", html, StringComparison.Ordinal);
        Assert.DoesNotContain("#asked", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RefsAreAppendedInBrackets()
    {
        Entry entry = SampleEntries.PrinterOutage with { Refs = ["INC0040001", "https://example.com/t/1"] };
        Assert.Contains("— Reported the printer outage to facilities [INC0040001] [https://example.com/t/1]</li>", Render([entry], ExportOptions.None), StringComparison.Ordinal);
    }

    [Fact]
    public void AllTogglesCombine()
    {
        const string expected =
            "<p>#staging-access — 3 entries, Oct 6 – Oct 20, 2026<br>Oct (3)</p>\n" +
            "<ul>\n" +
            "<li><b>Oct 6, 2026</b> — Asked for access to the staging environment; told it would be looked into. #asked #staging-access @j-doe</li>\n" +
            "<li><b>Oct 15, 2026</b> (9 days later) — Followed up; no response to the first ask. #asked #staging-access @j-doe</li>\n" +
            "<li><b>Oct 20, 2026</b> (5 days later) — Asked again in standup; told it would be this week. #asked #staging-access @j-doe<div>Reason: wanted it on record before the release freeze.</div></li>\n" +
            "</ul>";
        Assert.Equal(expected, Render(SampleEntries.Sample, Everything));
    }

    [Fact]
    public void EveryEntryIsOneListItemOldestFirst()
    {
        string html = Render(SampleEntries.All.Reverse(), Everything, filter: "");
        string[] items = [.. html.Split('\n').Where(line => line.StartsWith("<li>", StringComparison.Ordinal))];
        Assert.Equal(SampleEntries.All.Count, items.Length);
        Assert.StartsWith("<li><b>Jul 14, 2026</b>", items[0], StringComparison.Ordinal);
        Assert.StartsWith("<li><b>Oct 20, 2026</b>", items[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextMatchesThePlainTextTimeline()
    {
        // Same words in the same order: strip the markup and the bullets and the two must agree line for line.
        foreach (ExportOptions options in new[] { ExportOptions.None, Gaps, Months, Compact, TagsAndPeople, Everything })
        {
            string text = TimelineExport.Render("", SampleEntries.All, options);
            string html = Render(SampleEntries.All, options, filter: "");
            string fromHtml = html
                .Replace("<p>", "", StringComparison.Ordinal).Replace("</p>", "\n", StringComparison.Ordinal)
                .Replace("<br>", "\n", StringComparison.Ordinal)
                .Replace("<ul>\n", "", StringComparison.Ordinal).Replace("\n</ul>", "", StringComparison.Ordinal)
                .Replace("<li>", "• ", StringComparison.Ordinal).Replace("</li>", "", StringComparison.Ordinal)
                .Replace("<b>", "", StringComparison.Ordinal).Replace("</b>", "", StringComparison.Ordinal)
                .Replace("<div>Reason: ", "\n      Reason: ", StringComparison.Ordinal).Replace("</div>", "", StringComparison.Ordinal);
            Assert.Equal(text, fromHtml);
        }
    }

    [Fact]
    public void TextIsEscaped()
    {
        Entry entry = SampleEntries.SampleStandup with { Summary = "Used <b>&</b> \"quoted\"", Reason = "a < b & c > d", Refs = ["x&y"] };
        string html = Render([entry], ExportOptions.None, filter: "<script>");
        Assert.Contains("— Used &lt;b&gt;&amp;&lt;/b&gt; &quot;quoted&quot; [x&amp;y]", html, StringComparison.Ordinal);
        Assert.Contains("<div>Reason: a &lt; b &amp; c &gt; d</div>", html, StringComparison.Ordinal);
        Assert.StartsWith("<p>&lt;script&gt; — 1 entry", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>&</b>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiLineReasonBreaksEveryLine()
    {
        Entry entry = SampleEntries.SampleStandup with { Reason = "first line\r\nsecond <line>\nthird" };
        string[] lines = Render([entry], ExportOptions.None).Split('\n');
        Assert.Equal("<li><b>Tue Oct 20, 2026 9:02 AM</b> — Asked again in standup; told it would be this week.<div>Reason: first line<br>second &lt;line&gt;<br>third</div></li>", lines[2]);
    }

    [Fact]
    public void EmptySetIsJustTheHeaderParagraph()
    {
        Assert.Equal("<p>#nothing — 0 entries</p>", Render([], Everything, filter: "#nothing"));
    }

    [Fact]
    public void TimesRenderInTheEntryOwnOffset()
    {
        string[] lines = Render([SampleEntries.TokyoMidnight], ExportOptions.None).Split('\n');
        Assert.Equal("<li><b>Thu Oct 1, 2026 12:30 AM</b> — Late night note from the Tokyo trip</li>", lines[2]);
    }

    [Fact]
    public void HasNoClassesStylesOrExternalResources()
    {
        string html = Render(SampleEntries.All, Everything, filter: "");
        Assert.DoesNotContain("class=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("style=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("src=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=", html, StringComparison.Ordinal);
    }
}
