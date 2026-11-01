using System.Globalization;
using System.Text;
using Logbook.Core;
using Logbook.Core.Export;
using Logbook.Core.Grammar;
using Xunit;

namespace Logbook.Tests;

/// <summary>
/// DESIGN.md section 16: output is US-style regardless of machine locale, and the grammar reads the same everywhere.
/// Each test switches the thread to a locale with different month names, decimal rules and AM/PM designators.
/// </summary>
public sealed class CultureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 30, 0, TimeSpan.FromHours(-5));
    private static readonly TimeZoneInfo Zone = TestZones.Fixed(TimeSpan.FromHours(-5));

    private static T Under<T>(string culture, Func<T> action)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    [InlineData("ar-SA")]
    public void TimelineIsUsStyleUnderAnyCulture(string culture)
    {
        string text = Under(culture, () => TimelineExport.Render("#staging-access", SampleEntries.Sample, new ExportOptions(true, true, false, false)));
        Assert.Contains("#staging-access — 3 entries, Oct 6 – Oct 20, 2026", text, StringComparison.Ordinal);
        Assert.Contains("Oct (3)", text, StringComparison.Ordinal);
        Assert.Contains("• Thu Oct 15, 2026 2:30 PM (9 days later) — ", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("ja-JP")]
    public void EveryExportIsByteForByteTheInvariantOutputUnderAnyCulture(string culture)
    {
        // ar-SA defaults to the Um al-Qura calendar and ja-JP has its own AM/PM designators, so any format call that
        // follows the thread culture would change the bytes. Reminders and related ids exercise every CSV column.
        Entry withEverything = SampleEntries.ToldAboutOutages with
        {
            RemindAt = new DateTimeOffset(2026, 8, 21, 8, 15, 30, 250, TimeSpan.FromHours(2)),
            RelatedTo = [SampleEntries.PrinterOutage.Id],
            Refs = ["INC0040002", "a,b"],
        };
        Entry[] entries = [.. SampleEntries.All, withEverything with { Id = "01K0000000000000000000000Z" }];
        ExportOptions compact = new(ShowGaps: true, GroupByMonth: true, Compact: true, TagsAndPeople: true);
        ExportOptions full = new(ShowGaps: true, GroupByMonth: true, Compact: false, TagsAndPeople: true);

        string[] Render() =>
        [
            TimelineExport.Render("#printer", entries, compact),
            TimelineExport.Render("#printer", entries, full),
            TimelineHtml.Render("#printer", entries, compact),
            TimelineHtml.Render("#printer", entries, full),
            CsvExport.Render(entries),
        ];

        string[] invariant = Under("", Render);
        string[] local = Under(culture, Render);
        for (int i = 0; i < invariant.Length; i++)
        {
            Assert.Equal(Encoding.UTF8.GetBytes(invariant[i]), Encoding.UTF8.GetBytes(local[i]));
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    public void RangeAndCountAreUsStyleUnderAnyCulture(string culture)
    {
        Assert.Equal("Jul 14 – Oct 20, 2026", Under(culture, () => RangeText.Range(SampleEntries.All)));
        Assert.Equal("12 entries", Under(culture, () => RangeText.Count(12)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    public void AtTimesAndDatesParseTheSameUnderAnyCulture(string culture)
    {
        CaptureLine line = Under(culture, () => LineParser.ParseCapture("thing at:yesterday,3pm", Now, Zone));
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 15, 0, 0, TimeSpan.FromHours(-5)), line.OccurredAt);

        CaptureLine iso = Under(culture, () => LineParser.ParseCapture("thing at:2026-10-06,15:00", Now, Zone));
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 15, 0, 0, TimeSpan.FromHours(-5)), iso.OccurredAt);

        QueryLine query = Under(culture, () => LineParser.ParseQuery("last:30d since:2026-01-05", new DateOnly(2026, 10, 8)));
        Assert.Equal(new DateOnly(2026, 1, 5), query.Since);
    }

    [Fact]
    public void TagNormalisationIsInvariant()
    {
        // In Turkish, "I".ToLower() is a dotless ı; the normaliser must not follow the thread culture.
        Assert.Equal("ticket-id", Under("tr-TR", () => TokenNormalizer.Normalize("TICKET_ID")));
    }
}
