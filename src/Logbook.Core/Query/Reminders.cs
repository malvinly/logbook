namespace Logbook.Core.Query;

/// <summary>
/// Picks the reminder the tray balloon shows next (docs/main-window.md). A reminder is due once its moment has passed,
/// compared as instants. The caller remembers which reminders it has shown; a reminder that is moved to a new time
/// counts as a new one, while an edit that leaves it alone does not.
/// </summary>
public static class Reminders
{
    /// <summary>One reminder that has been shown: the entry and the moment it was set for.</summary>
    public readonly record struct Shown(string Id, DateTimeOffset RemindAt)
    {
        public static Shown Of(Entry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return new Shown(entry.Id, entry.RemindAt ?? throw new ArgumentException("The entry has no reminder.", nameof(entry)));
        }
    }

    /// <summary>The earliest due reminder not in <paramref name="shown"/>, or null when there is none.</summary>
    public static Entry? NextDue(IEnumerable<Entry> live, DateTimeOffset now, IReadOnlySet<Shown> shown)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(shown);
        return live
            .Where(e => e.RemindAt is { } at && at <= now && !shown.Contains(new Shown(e.Id, at)))
            .OrderBy(e => e.RemindAt)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
