using Logbook.Core.Grammar;

namespace Logbook.Core.Query;

/// <summary>Applies a parsed filter line to the live entries (DESIGN.md sections 6 and 10).</summary>
public static class EntryQuery
{
    /// <summary>Returns the entries that match every token of the query, newest first.</summary>
    public static IReadOnlyList<Entry> Filter(IEnumerable<Entry> entries, QueryLine query)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(query);
        return
        [
            .. entries
                .Where(e => Matches(e, query))
                .OrderByDescending(e => e.OccurredAt)
                .ThenByDescending(e => e.Id, StringComparer.Ordinal),
        ];
    }

    private static bool Matches(Entry entry, QueryLine query)
    {
        if (!query.Tags.All(entry.Tags.Contains) || !query.People.All(entry.People.Contains))
        {
            return false;
        }

        // Bounds are calendar dates, compared against the date the entry carries in its own stored offset.
        DateOnly date = DateOnly.FromDateTime(entry.OccurredAt.DateTime);
        if ((query.Since is { } since && date < since) || (query.Until is { } until && date > until))
        {
            return false;
        }

        return query.Words.All(word => ContainsText(entry, word)) && query.Phrases.All(phrase => ContainsText(entry, phrase));
    }

    private static bool ContainsText(Entry entry, string text)
        => entry.Summary.Contains(text, StringComparison.OrdinalIgnoreCase)
            || (entry.Reason is not null && entry.Reason.Contains(text, StringComparison.OrdinalIgnoreCase));
}
