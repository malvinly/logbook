using Logbook.Core;

namespace Logbook.App.Main;

/// <summary>A prior revision in the detail pane's history list, read-only.</summary>
public sealed class RevisionRow
{
    public RevisionRow(Entry revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        Entry = revision;
        Heading = FormattableString.Invariant($"Revision {revision.Rev} · {Format.DateTime(revision.RecordedAt)}") + (revision.Deleted ? " · deleted" : "");
        Summary = revision.Summary;
        Detail = string.Join("   ", Parts(revision));
    }

    public Entry Entry { get; }

    public string Heading { get; }

    public string Summary { get; }

    public string Detail { get; }

    private static IEnumerable<string> Parts(Entry e)
    {
        yield return "occurred " + Format.DateTime(e.OccurredAt);
        if (e.Tags.Count > 0)
        {
            yield return string.Join(' ', e.Tags.Select(t => "#" + t));
        }

        if (e.People.Count > 0)
        {
            yield return string.Join(' ', e.People.Select(p => "@" + p));
        }

        if (!string.IsNullOrEmpty(e.Reason))
        {
            yield return "reason: " + e.Reason.Replace('\n', ' ');
        }

        if (e.Refs.Count > 0)
        {
            yield return "refs: " + string.Join(", ", e.Refs);
        }

        if (e.RemindAt is { } remindAt)
        {
            yield return "reminder " + Format.DateTime(remindAt);
        }
    }
}
