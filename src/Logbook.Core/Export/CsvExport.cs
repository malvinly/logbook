using System.Globalization;
using System.Text;

namespace Logbook.Core.Export;

/// <summary>
/// A filtered set as CSV (docs/export.md): RFC 4180, comma separated, CRLF after every row, one header row,
/// oldest first. Timestamps are the stored round-trip form with their own offset, never converted. Lists are joined
/// by a single space, except refs, which may contain spaces themselves and are joined by "; ". A summary, reason, refs,
/// tags or people cell that would read as a formula gets a leading apostrophe, so those five are not always verbatim.
/// </summary>
public static class CsvExport
{
    private const string Header = "occurredAt,recordedAt,summary,reason,tags,people,refs,relatedTo,remindAt,id";

    public static string Render(IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        StringBuilder csv = new();
        csv.Append(Header).Append("\r\n");
        foreach (Entry entry in TimelineExport.Order(entries))
        {
            string[] fields =
            [
                Timestamp(entry.OccurredAt),
                Timestamp(entry.RecordedAt),
                DefuseFormula(entry.Summary),
                DefuseFormula(entry.Reason ?? ""),
                DefuseFormula(string.Join(' ', entry.Tags)),
                DefuseFormula(string.Join(' ', entry.People)),
                DefuseFormula(string.Join("; ", entry.Refs)),
                string.Join(' ', entry.RelatedTo),
                entry.RemindAt is { } remindAt ? Timestamp(remindAt) : "",
                entry.Id,
            ];
            csv.AppendJoin(',', fields.Select(Quote)).Append("\r\n");
        }

        return csv.ToString();
    }

    /// <summary>The file's bytes: UTF-8 with a byte order mark, which spreadsheet apps need to detect the encoding.</summary>
    public static byte[] FileBytes(IEnumerable<Entry> entries)
    {
        UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: true);
        return [.. utf8.GetPreamble(), .. utf8.GetBytes(Render(entries))];
    }

    private static string Timestamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    // Spreadsheet apps run a cell that starts with = + - or @ as a formula, so such a text cell gets a
    // leading apostrophe, which they read as "this is text". Applied before quoting, so the ' sits inside the quotes.
    // Tags and people go through it too: the normaliser allows a leading - or +, as in -urgent or +1.
    private static string DefuseFormula(string text)
        => text.Length > 0 && text[0] is '=' or '+' or '-' or '@' ? "'" + text : text;

    // RFC 4180: a field holding a comma, a quote or a line break is enclosed in quotes, with its quotes doubled.
    private static string Quote(string field)
        => field.AsSpan().IndexOfAny(",\"\r\n") < 0 ? field : "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
