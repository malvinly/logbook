namespace Logbook.Core;

/// <summary>
/// One revision of one log entry, exactly as it is stored on a line of the data file (docs/file-format.md).
/// A revision is never mutated; an edit is a new <see cref="Entry"/> with the same <see cref="Id"/> and <see cref="Rev"/> + 1.
/// </summary>
/// <param name="Id">ULID assigned when the entry is first captured. Never reused.</param>
/// <param name="Rev">Revision number, starting at 1. The loader keeps the highest revision per id.</param>
/// <param name="OccurredAt">When the thing happened, with the UTC offset captured at the time. Never converted.</param>
/// <param name="RecordedAt">When this revision was written down, with its captured offset.</param>
/// <param name="Summary">Required single line of text.</param>
/// <param name="Reason">Optional, may span multiple lines.</param>
/// <param name="Tags">Normalised tag names (lowercase kebab-case). May be empty.</param>
/// <param name="People">Normalised person names (lowercase kebab-case). May be empty.</param>
/// <param name="Refs">Free-form references: ticket numbers, URLs, paths, links. Rendered verbatim.</param>
/// <param name="RelatedTo">Ids of the entries this one follows up on. Empty for most entries and for lines written before the field existed.</param>
/// <param name="RemindAt">When to show a reminder for this entry, with its own offset; null when none is set.</param>
/// <param name="Deleted">True on a tombstone revision. Tombstoned ids are hidden everywhere except the revision view.</param>
public sealed record Entry(
    string Id,
    int Rev,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    string Summary,
    string? Reason,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> People,
    IReadOnlyList<string> Refs,
    IReadOnlyList<string> RelatedTo,
    DateTimeOffset? RemindAt,
    bool Deleted);
