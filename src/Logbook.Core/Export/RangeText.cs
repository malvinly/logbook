using System.Globalization;

namespace Logbook.Core.Export;

/// <summary>
/// The count and date range shown above the results and in the export header (docs/main-window.md and docs/export.md):
/// <c>12 entries</c> and <c>Jul 14 – Oct 28, 2026</c>. Fixed US-style, invariant culture.
/// </summary>
public static class RangeText
{
    public static string Count(int count) => count == 1 ? "1 entry" : FormattableString.Invariant($"{count} entries");

    /// <summary>Range of the entries' own calendar dates. Empty string for an empty set.</summary>
    public static string Range(IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        List<DateOnly> dates = [.. entries.Select(StoredDate)];
        if (dates.Count == 0)
        {
            return "";
        }

        DateOnly first = dates.Min();
        DateOnly last = dates.Max();
        if (first == last)
        {
            return MonthDayYear(first);
        }

        return first.Year == last.Year
            ? $"{MonthDay(first)} – {MonthDayYear(last)}"
            : $"{MonthDayYear(first)} – {MonthDayYear(last)}";
    }

    /// <summary>The calendar date an entry carries in its own stored offset, never the machine zone (CLAUDE.md).</summary>
    public static DateOnly StoredDate(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return DateOnly.FromDateTime(entry.OccurredAt.DateTime);
    }

    private static string MonthDay(DateOnly date) => date.ToString("MMM d", CultureInfo.InvariantCulture);

    private static string MonthDayYear(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}
