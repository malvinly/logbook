using System.Globalization;
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
        CaptureLine line = Under(culture, () => LineParser.ParseCapture("thing at:yesterday,3pm", Now));
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 15, 0, 0, TimeSpan.FromHours(-5)), line.OccurredAt);

        CaptureLine iso = Under(culture, () => LineParser.ParseCapture("thing at:2026-10-06,15:00", Now));
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
