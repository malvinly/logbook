using System.Globalization;

namespace Logbook.Core.Grammar;

/// <summary>Parses the values of the date keywords in DESIGN.md section 6, resolved against a supplied "now".</summary>
internal static class DateValues
{
    private const string IsoDate = "yyyy-MM-dd";

    private static readonly string[] TimeFormats = ["H:mm", "h:mmtt", "htt"];

    // Index matches DayOfWeek: Sunday is 0.
    private static readonly string[] Weekdays = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    /// <summary>
    /// <c>at:</c> value: <c>&lt;date&gt;</c>, <c>&lt;date&gt;,&lt;time&gt;</c> or <c>&lt;time&gt;</c>.
    /// A date is ISO, <c>today</c>, <c>yesterday</c> or a weekday abbreviation (the most recent one before today).
    /// A date without a time is noon; a time without a date is today, the date of <paramref name="now"/>. The offset is
    /// the one <paramref name="zone"/> has at the date and time named, not that of <paramref name="now"/>, so a time
    /// on the other side of a clock change carries the offset in force then.
    /// </summary>
    public static bool TryParseAt(string value, DateTimeOffset now, TimeZoneInfo zone, out DateTimeOffset result)
    {
        DateOnly today = DateOnly.FromDateTime(now.Date);
        int comma = value.IndexOf(',', StringComparison.Ordinal);
        DateOnly date;
        TimeOnly time;
        if (comma >= 0)
        {
            if (!TryParseCaptureDate(value[..comma], today, out date) || !TryParseTime(value[(comma + 1)..], out time))
            {
                result = default;
                return false;
            }
        }
        else if (TryParseCaptureDate(value, today, out date))
        {
            time = new TimeOnly(12, 0);
        }
        else if (TryParseTime(value, out time))
        {
            date = today;
        }
        else
        {
            result = default;
            return false;
        }

        // The first and last days of the calendar cannot carry every offset; treat those as text rather than crash.
        try
        {
            // GetUtcOffset reads an unspecified-kind time as wall-clock time in the zone. A time that does not exist
            // (the hour skipped when clocks go forward) or exists twice (the hour repeated when they go back) gets the
            // zone's standard offset: that is the BCL's rule, and the reminder field's, which uses the same call.
            DateTime local = date.ToDateTime(time);
            result = new DateTimeOffset(local, zone.GetUtcOffset(local));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            result = default;
            return false;
        }
    }

    /// <summary><c>since:</c> / <c>until:</c> value: ISO date, <c>today</c> or <c>yesterday</c>.</summary>
    public static bool TryParseQueryDate(string value, DateOnly today, out DateOnly date)
    {
        switch (value)
        {
            case "today":
                date = today;
                return true;
            case "yesterday":
                date = today.AddDays(-1);
                return true;
            default:
                return DateOnly.TryParseExact(value, IsoDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
    }

    /// <summary><c>last:</c> value: a count followed by <c>d</c>, <c>w</c> or <c>m</c>, giving the <c>since:</c> date.</summary>
    public static bool TryParseLast(string value, DateOnly today, out DateOnly since)
    {
        since = default;
        if (value.Length < 2 || !int.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1)
        {
            return false;
        }

        // A count that reaches before year 1 is text, not a crash; the filter runs on every keystroke.
        try
        {
            switch (value[^1])
            {
                case 'd':
                    since = today.AddDays(-count);
                    return true;
                case 'w':
                    since = today.AddDays(checked(-7 * count));
                    return true;
                case 'm':
                    since = today.AddMonths(-count);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or OverflowException)
        {
            return false;
        }
    }

    private static bool TryParseCaptureDate(string value, DateOnly today, out DateOnly date)
    {
        if (TryParseQueryDate(value, today, out date))
        {
            return true;
        }

        int weekday = Array.IndexOf(Weekdays, value);
        if (weekday < 0)
        {
            return false;
        }

        // Someone who types a weekday means a past day; for today they would type a time or nothing. So on a
        // Monday, at:mon is last Monday.
        int daysBack = ((int)today.DayOfWeek - weekday + 7) % 7;
        if (daysBack == 0)
        {
            daysBack = 7;
        }

        date = today.AddDays(-daysBack);
        return true;
    }

    private static bool TryParseTime(string value, out TimeOnly time)
        => TimeOnly.TryParseExact(value, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
