using Logbook.Core.Export;
using Logbook.Core.Grammar;

namespace Logbook.Core.Query;

/// <summary>Applies a parsed filter line to the live entries (docs/grammar.md and docs/main-window.md).</summary>
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

    /// <summary>
    /// The entries whose relatedTo names <paramref name="id"/>, oldest first with the id breaking ties (docs/main-window.md,
    /// "Followed up by"). Pass the live entries: a deleted follow-up is hidden here as everywhere else.
    /// </summary>
    public static IReadOnlyList<Entry> FollowUpsOf(string id, IEnumerable<Entry> live)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(live);
        return
        [
            .. live
                .Where(e => e.RelatedTo.Contains(id, StringComparer.Ordinal))
                .OrderBy(e => e.OccurredAt)
                .ThenBy(e => e.Id, StringComparer.Ordinal),
        ];
    }

    private static bool Matches(Entry entry, QueryLine query)
    {
        if (!query.Tags.All(entry.Tags.Contains) || !query.People.All(entry.People.Contains))
        {
            return false;
        }

        // Bounds are calendar dates, compared against the date the entry carries in its own stored offset.
        DateOnly date = RangeText.StoredDate(entry);
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
