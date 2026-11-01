namespace Logbook.Core.Export;

/// <summary>The four timeline toggles of DESIGN.md section 11, remembered between uses by the app.</summary>
/// <param name="ShowGaps">Append <c>(N days later)</c> after the timestamp from the second entry on.</param>
/// <param name="GroupByMonth">Insert a second header line with per-month counts.</param>
/// <param name="Compact">Drop the weekday and the time of day.</param>
/// <param name="TagsAndPeople">Append each entry's <c>#tags</c> and then its <c>@people</c> to its line, after the refs.</param>
public sealed record ExportOptions(bool ShowGaps, bool GroupByMonth, bool Compact, bool TagsAndPeople)
{
    public static readonly ExportOptions None = new(false, false, false, false);
}
