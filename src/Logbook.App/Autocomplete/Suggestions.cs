using Logbook.App.Chips;
using Logbook.Core;

namespace Logbook.App.Autocomplete;

/// <summary>
/// Tag and person names seen in the live entries, ranked most recently used or most frequent, capped, and
/// prefix-filtered as you type (docs/main-window.md). Rebuilt from the live set whenever a window opens.
/// </summary>
public sealed class Suggestions
{
    private readonly Dictionary<string, (DateTimeOffset LastUsed, int Count)> _tags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTimeOffset LastUsed, int Count)> _people = new(StringComparer.Ordinal);
    private readonly int _cap;
    private readonly bool _byFrequency;

    public Suggestions(IEnumerable<Entry> entries, int cap, string ranking)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _cap = Math.Max(1, cap);
        _byFrequency = string.Equals(ranking, "frequent", StringComparison.OrdinalIgnoreCase);
        foreach (Entry entry in entries)
        {
            foreach (string tag in entry.Tags)
            {
                Note(_tags, tag, entry.OccurredAt);
            }

            foreach (string person in entry.People)
            {
                Note(_people, person, entry.OccurredAt);
            }
        }

        // Only names the grammar reads back are offered: accepting "123" would insert "#123 ", stored as summary text.
        DropUnreadable(_tags, TokenName.ParsesBackAsTag);
        DropUnreadable(_people, TokenName.ParsesBackAsPerson);
    }

    private static void DropUnreadable(Dictionary<string, (DateTimeOffset LastUsed, int Count)> index, Func<string, bool> parsesBack)
    {
        foreach (string name in index.Keys.Where(name => !parsesBack(name)).ToList())
        {
            index.Remove(name);
        }
    }

    public IReadOnlyList<string> For(ChipKind kind, string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        var source = kind == ChipKind.Person ? _people : _tags;
        IEnumerable<KeyValuePair<string, (DateTimeOffset LastUsed, int Count)>> matches =
            source.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        IOrderedEnumerable<KeyValuePair<string, (DateTimeOffset LastUsed, int Count)>> ranked = _byFrequency
            ? matches.OrderByDescending(pair => pair.Value.Count).ThenByDescending(pair => pair.Value.LastUsed)
            : matches.OrderByDescending(pair => pair.Value.LastUsed).ThenByDescending(pair => pair.Value.Count);
        return [.. ranked.Take(_cap).Select(pair => pair.Key)];
    }

    private static void Note(Dictionary<string, (DateTimeOffset LastUsed, int Count)> index, string name, DateTimeOffset usedAt)
    {
        if (index.TryGetValue(name, out var existing))
        {
            index[name] = (usedAt > existing.LastUsed ? usedAt : existing.LastUsed, existing.Count + 1);
        }
        else
        {
            index[name] = (usedAt, 1);
        }
    }
}
