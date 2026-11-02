using System.Text;

namespace Logbook.Core.Export;

/// <summary>
/// The same timeline as <see cref="TimelineExport"/>, as an HTML fragment for the clipboard (docs/export.md), so
/// mail clients paste bold dates and real bullets. The header is a paragraph, each entry one list item with its date
/// in bold, and the reason a block under its item. All text is escaped; no classes, styles or external resources.
/// </summary>
public static class TimelineHtml
{
    /// <param name="filterText">The filter as typed, or a saved query's name (<see cref="ExportTitle"/>); blank means "All entries".</param>
    /// <param name="entries">The filtered set in any order.</param>
    /// <param name="options">The four toggles, with the same meaning as for the plain text.</param>
    public static string Render(string filterText, IEnumerable<Entry> entries, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(filterText);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        List<Entry> ordered = TimelineExport.Order(entries);
        StringBuilder header = new("<p>");
        header.Append(Escape(TimelineExport.Header(filterText, ordered)));
        if (ordered.Count > 0 && options.GroupByMonth)
        {
            header.Append("<br>").Append(Escape(TimelineExport.MonthCounts(ordered)));
        }

        header.Append("</p>");
        if (ordered.Count == 0)
        {
            return header.ToString();
        }

        List<string> lines = [header.ToString(), "<ul>"];
        DateOnly? previousDate = null;
        foreach (Entry entry in ordered)
        {
            DateOnly date = RangeText.StoredDate(entry);
            StringBuilder line = new("<li><b>");
            line.Append(Escape(TimelineExport.When(entry, options))).Append("</b>");
            if (options.ShowGaps && previousDate is { } previous)
            {
                line.Append(" (").Append(TimelineExport.GapText(date.DayNumber - previous.DayNumber)).Append(')');
            }

            line.Append(" — ").Append(Escape(entry.Summary));
            foreach (string reference in entry.Refs)
            {
                line.Append(" [").Append(Escape(reference)).Append(']');
            }

            if (options.TagsAndPeople)
            {
                foreach (string tag in entry.Tags)
                {
                    line.Append(" #").Append(Escape(tag));
                }

                foreach (string person in entry.People)
                {
                    line.Append(" @").Append(Escape(person));
                }
            }

            // The reason sits inside the item, so it stays under its entry when pasted; one <br> per line break.
            if (!string.IsNullOrEmpty(entry.Reason))
            {
                IEnumerable<string> reasonLines = entry.Reason.Split('\n').Select(part => Escape(part.TrimEnd('\r')));
                line.Append("<div>Reason: ").AppendJoin("<br>", reasonLines).Append("</div>");
            }

            line.Append("</li>");
            lines.Add(line.ToString());
            previousDate = date;
        }

        lines.Add("</ul>");
        return string.Join("\n", lines);
    }

    /// <summary>The four characters that could be read as markup or end an attribute.</summary>
    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal) // first, so the entities below are not escaped twice
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
