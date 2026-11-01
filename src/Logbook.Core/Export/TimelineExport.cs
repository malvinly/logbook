using System.Globalization;
using System.Text;

namespace Logbook.Core.Export;

/// <summary>
/// Renders a filtered set as the plain-text timeline of DESIGN.md section 11: oldest first, Unicode bullets,
/// fixed US-style dates in each entry's own offset, no markdown. Tags and people are left out unless asked for.
/// </summary>
public static class TimelineExport
{
    private const string FullPattern = "ddd MMM d, yyyy h:mm tt";
    private const string CompactPattern = "MMM d, yyyy";
    private const string ReasonIndent = "      ";

    // The pieces below are shared with TimelineHtml, so the two formats always say the same thing.

    /// <summary>Oldest first; the id breaks ties so the order never depends on the input order.</summary>
    internal static List<Entry> Order(IEnumerable<Entry> entries)
        => [.. entries.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id, StringComparer.Ordinal)];

    /// <summary>"#staging-access — 3 entries, Oct 6 – Oct 20, 2026"; the range is left out of an empty set.</summary>
    internal static string Header(string filterText, List<Entry> ordered)
    {
        string title = string.IsNullOrWhiteSpace(filterText) ? "All entries" : filterText.Trim();
        string header = $"{title} — {RangeText.Count(ordered.Count)}";
        if (ordered.Count > 0)
        {
            header += ", " + RangeText.Range(ordered);
        }

        return header;
    }

    /// <summary>The date part of a line: full or compact, in the entry's own offset.</summary>
    internal static string When(Entry entry, ExportOptions options)
        => entry.OccurredAt.ToString(options.Compact ? CompactPattern : FullPattern, CultureInfo.InvariantCulture);

    /// <param name="filterText">The filter as typed; blank means "All entries".</param>
    /// <param name="entries">The filtered set in any order.</param>
    /// <param name="options">The four toggles.</param>
    public static string Render(string filterText, IEnumerable<Entry> entries, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(filterText);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        List<Entry> ordered = Order(entries);
        List<string> lines = [];

        string header = Header(filterText, ordered);
        lines.Add(header);
        if (ordered.Count == 0)
        {
            return header;
        }

        if (options.GroupByMonth)
        {
            lines.Add(MonthCounts(ordered));
        }

        lines.Add("");

        DateOnly? previousDate = null;
        foreach (Entry entry in ordered)
        {
            DateOnly date = RangeText.StoredDate(entry);
            StringBuilder line = new("• ");
            line.Append(When(entry, options));
            if (options.ShowGaps && previousDate is { } previous)
            {
                line.Append(" (").Append(GapText(date.DayNumber - previous.DayNumber)).Append(')');
            }

            line.Append(" — ").Append(entry.Summary);
            foreach (string reference in entry.Refs)
            {
                line.Append(" [").Append(reference).Append(']');
            }

            if (options.TagsAndPeople)
            {
                foreach (string tag in entry.Tags)
                {
                    line.Append(" #").Append(tag);
                }

                foreach (string person in entry.People)
                {
                    line.Append(" @").Append(person);
                }
            }

            lines.Add(line.ToString());

            if (!string.IsNullOrEmpty(entry.Reason))
            {
                string[] reasonLines = entry.Reason.Split('\n');
                lines.Add(ReasonIndent + "Reason: " + reasonLines[0].TrimEnd('\r'));
                for (int i = 1; i < reasonLines.Length; i++)
                {
                    lines.Add(ReasonIndent + reasonLines[i].TrimEnd('\r'));
                }
            }

            previousDate = date;
        }

        return string.Join("\n", lines);
    }

    // Gaps are whole calendar days, so Oct 15 2:30 PM to Oct 20 9:02 AM is 5 days, as in the design sample.
    internal static string GapText(int days) => days switch
    {
        0 => "same day",
        1 => "1 day later",
        _ => FormattableString.Invariant($"{days} days later"),
    };

    // "Jul (1) · Aug (3) · Oct (2)"; the year is added only when the set spans more than one.
    internal static string MonthCounts(List<Entry> ordered)
    {
        var groups = ordered
            .GroupBy(e => (RangeText.StoredDate(e).Year, RangeText.StoredDate(e).Month))
            .OrderBy(g => g.Key)
            .ToList();
        bool multipleYears = groups.Select(g => g.Key.Year).Distinct().Count() > 1;
        string pattern = multipleYears ? "MMM yyyy" : "MMM";
        return string.Join(" · ", groups.Select(g =>
            FormattableString.Invariant($"{new DateOnly(g.Key.Year, g.Key.Month, 1).ToString(pattern, CultureInfo.InvariantCulture)} ({g.Count()})")));
    }
}
