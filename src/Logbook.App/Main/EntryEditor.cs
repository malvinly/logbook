using System.Globalization;
using Logbook.Core;
using Logbook.Core.Grammar;

namespace Logbook.App.Main;

/// <summary>
/// The detail pane's editable copy of one entry (DESIGN.md section 10). Saving produces a new revision; the
/// stored offset is kept. Each Changed flag says whether a field differs from the previous revision.
/// </summary>
public sealed class EntryEditor : ObservableObject
{
    private static readonly string[] TimeFormats = ["h:mm tt", "H:mm", "h:mmtt", "htt", "h tt"];

    private readonly Entry _current;
    private DateTime? _date;
    private string _time;
    private string _summary;
    private string _reason;
    private string _tags;
    private string _people;
    private string _refs;
    private string _problem = "";

    public EntryEditor(Entry current, Entry? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        _current = current;
        _date = current.OccurredAt.Date;
        _time = Format.Time(current.OccurredAt);
        _summary = current.Summary;
        _reason = current.Reason ?? "";
        _tags = string.Join(' ', current.Tags);
        _people = string.Join(' ', current.People);
        _refs = string.Join(Environment.NewLine, current.Refs);

        if (previous is not null)
        {
            OccurredAtChanged = previous.OccurredAt != current.OccurredAt;
            SummaryChanged = previous.Summary != current.Summary;
            ReasonChanged = (previous.Reason ?? "") != (current.Reason ?? "");
            TagsChanged = !previous.Tags.SequenceEqual(current.Tags);
            PeopleChanged = !previous.People.SequenceEqual(current.People);
            RefsChanged = !previous.Refs.SequenceEqual(current.Refs);
        }
    }

    public Entry Current => _current;

    public string RevisionText => _current.Rev == 1
        ? "Revision 1, recorded " + Format.DateTime(_current.RecordedAt)
        : FormattableString.Invariant($"Revision {_current.Rev}, edited {Format.DateTime(_current.RecordedAt)}");

    public DateTime? Date
    {
        get => _date;
        set => Set(ref _date, value);
    }

    public string Time
    {
        get => _time;
        set => Set(ref _time, value);
    }

    public string Summary
    {
        get => _summary;
        set => Set(ref _summary, value);
    }

    public string Reason
    {
        get => _reason;
        set => Set(ref _reason, value);
    }

    public string Tags
    {
        get => _tags;
        set => Set(ref _tags, value);
    }

    public string People
    {
        get => _people;
        set => Set(ref _people, value);
    }

    public string Refs
    {
        get => _refs;
        set => Set(ref _refs, value);
    }

    public string Problem
    {
        get => _problem;
        private set => Set(ref _problem, value);
    }

    public bool OccurredAtChanged { get; }

    public bool SummaryChanged { get; }

    public bool ReasonChanged { get; }

    public bool TagsChanged { get; }

    public bool PeopleChanged { get; }

    public bool RefsChanged { get; }

    /// <summary>The next revision, or null with <see cref="Problem"/> set when a field does not parse.</summary>
    public Entry? ToRevision(DateTimeOffset now)
    {
        if (_date is null)
        {
            Problem = "Pick a date.";
            return null;
        }

        if (!TimeOnly.TryParseExact(_time.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
        {
            Problem = "The time should look like 2:30 PM or 14:30.";
            return null;
        }

        string summary = _summary.Trim();
        if (summary.Length == 0)
        {
            Problem = "The summary cannot be empty.";
            return null;
        }

        Problem = "";
        return _current with
        {
            Rev = _current.Rev + 1,
            OccurredAt = new DateTimeOffset(DateOnly.FromDateTime(_date.Value), time, _current.OccurredAt.Offset),
            RecordedAt = now,
            Summary = summary,
            Reason = string.IsNullOrWhiteSpace(_reason) ? null : _reason.Replace("\r\n", "\n", StringComparison.Ordinal).Trim(),
            Tags = Names(_tags),
            People = Names(_people),
            Refs = [.. _refs.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            Deleted = false,
        };
    }

    /// <summary>A tombstone revision: the same fields, marked deleted.</summary>
    public Entry ToTombstone(DateTimeOffset now) => _current with { Rev = _current.Rev + 1, RecordedAt = now, Deleted = true };

    private static List<string> Names(string text)
    {
        List<string> names = [];
        foreach (string raw in text.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = TokenNormalizer.Normalize(raw.TrimStart('#', '@'));
            if (name.Length > 0 && !names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }
}
