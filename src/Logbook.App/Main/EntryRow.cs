using Logbook.App.Chips;
using Logbook.Core;
using Logbook.Core.Export;

namespace Logbook.App.Main;

/// <summary>One row of the results list: time, summary, chips, and the small markers (DESIGN.md section 10).</summary>
public sealed class EntryRow
{
    public EntryRow(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        Day = RangeText.LocalDate(entry);
        DayHeader = Format.DayHeader(Day);
        Time = Format.Time(entry.OccurredAt);
        Chips = Chip.For(entry);
    }

    public Entry Entry { get; }

    public string Id => Entry.Id;

    public DateOnly Day { get; }

    /// <summary>The group header text; grouping is by this string so the view needs no converter.</summary>
    public string DayHeader { get; }

    public string Time { get; }

    public string Summary => Entry.Summary;

    public IReadOnlyList<Chip> Chips { get; }

    public bool HasReason => !string.IsNullOrEmpty(Entry.Reason);

    public bool HasRefs => Entry.Refs.Count > 0;

    public bool IsEdited => Entry.Rev > 1;
}
