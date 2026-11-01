using System.Globalization;
using Logbook.Core;
using Logbook.Core.Export;
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
    private DateTime? _remindDate;
    private string _remindTime;
    private string _problem = "";

    public EntryEditor(Entry current, Entry? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        _current = current;
        _date = RangeText.StoredDate(current).ToDateTime(TimeOnly.MinValue);
        _time = Format.Time(current.OccurredAt);
        _summary = current.Summary;
        _reason = current.Reason ?? "";
        _tags = string.Join(' ', current.Tags);
        _people = string.Join(' ', current.People);
        _refs = string.Join(Environment.NewLine, current.Refs);
        // Shown in the reminder's own stored offset, like occurredAt.
        _remindDate = current.RemindAt?.DateTime.Date;
        _remindTime = current.RemindAt is { } remindAt ? Format.Time(remindAt) : "";
        ReminderPassed = current.RemindAt is { } due && due <= DateTimeOffset.Now;

        if (previous is not null)
        {
            OccurredAtChanged = previous.OccurredAt != current.OccurredAt;
            SummaryChanged = previous.Summary != current.Summary;
            ReasonChanged = (previous.Reason ?? "") != (current.Reason ?? "");
            TagsChanged = !previous.Tags.SequenceEqual(current.Tags);
            PeopleChanged = !previous.People.SequenceEqual(current.People);
            RefsChanged = !previous.Refs.SequenceEqual(current.Refs);
            RemindAtChanged = previous.RemindAt != current.RemindAt;
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

    public DateTime? RemindDate
    {
        get => _remindDate;
        set => Set(ref _remindDate, value);
    }

    public string RemindTime
    {
        get => _remindTime;
        set => Set(ref _remindTime, value);
    }

    /// <summary>True when the stored reminder is due; the pane then says how to stop the balloon.</summary>
    public bool ReminderPassed { get; }

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

    public bool RemindAtChanged { get; }

    /// <summary>Empties the reminder fields; saving then writes a revision without a reminder.</summary>
    public void ClearReminder()
    {
        RemindDate = null;
        RemindTime = "";
    }

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

        if (!TryReadReminder(out DateTimeOffset? remindAt))
        {
            return null;
        }

        string summary = _summary.Trim();
        if (summary.Length == 0)
        {
            Problem = "The summary cannot be empty.";
            return null;
        }

        // The box shows minutes only; when the user left the time alone, keep the stored seconds so nothing looks edited.
        TimeOnly stored = TimeOnly.FromDateTime(_current.OccurredAt.DateTime);
        if (time.Hour == stored.Hour && time.Minute == stored.Minute)
        {
            time = stored;
        }

        Problem = "";
        return _current with
        {
            Rev = _current.Rev + 1,
            OccurredAt = new DateTimeOffset(DateOnly.FromDateTime(_date.Value), time, _current.OccurredAt.Offset),
            RecordedAt = now,
            Summary = summary,
            Reason = string.IsNullOrWhiteSpace(_reason) ? null : _reason.Replace("\r\n", "\n", StringComparison.Ordinal).Trim(),
            Tags = TokenNormalizer.NormalizeList(_tags),
            People = TokenNormalizer.NormalizeList(_people),
            Refs = [.. _refs.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            RemindAt = remindAt,
            Deleted = false,
        };
    }

    // Both boxes empty means no reminder. An unchanged reminder keeps its stored value exactly, an edited one keeps its
    // stored offset (as occurredAt does), and a new one takes the local offset at that date and time, so a reminder set
    // across a daylight-saving change still fires at the wall-clock time typed.
    private bool TryReadReminder(out DateTimeOffset? remindAt)
    {
        remindAt = null;
        if (_remindDate is null && string.IsNullOrWhiteSpace(_remindTime))
        {
            return true;
        }

        if (_remindDate is null)
        {
            Problem = "Pick a date for the reminder, or clear it.";
            return false;
        }

        if (!TimeOnly.TryParseExact(_remindTime.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
        {
            Problem = "The reminder time should look like 9:00 AM or 09:00.";
            return false;
        }

        DateOnly date = DateOnly.FromDateTime(_remindDate.Value);
        if (_current.RemindAt is { } stored)
        {
            bool unchanged = DateOnly.FromDateTime(stored.DateTime) == date && stored.Hour == time.Hour && stored.Minute == time.Minute;
            remindAt = unchanged ? stored : new DateTimeOffset(date, time, stored.Offset);
        }
        else
        {
            DateTime local = date.ToDateTime(time);
            remindAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }

        return true;
    }

    /// <summary>A tombstone revision: the same fields, marked deleted.</summary>
    public Entry ToTombstone(DateTimeOffset now) => _current with { Rev = _current.Rev + 1, RecordedAt = now, Deleted = true };
}
