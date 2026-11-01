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
    /// A date is ISO, <c>today</c>, <c>yesterday</c> or a weekday abbreviation (most recent, today included).
    /// A date without a time is noon; a time without a date is today. The offset is always that of <paramref name="now"/>.
    /// </summary>
    public static bool TryParseAt(string value, DateTimeOffset now, out DateTimeOffset result)
    {
        DateOnly today = DateOnly.FromDateTime(now.Date);
        int comma = value.IndexOf(',', StringComparison.Ordinal);
        if (comma >= 0)
        {
            if (TryParseCaptureDate(value[..comma], today, out DateOnly date) && TryParseTime(value[(comma + 1)..], out TimeOnly time))
            {
                result = new DateTimeOffset(date, time, now.Offset);
                return true;
            }
        }
        else if (TryParseCaptureDate(value, today, out DateOnly date))
        {
            result = new DateTimeOffset(date, new TimeOnly(12, 0), now.Offset);
            return true;
        }
        else if (TryParseTime(value, out TimeOnly time))
        {
            result = new DateTimeOffset(today, time, now.Offset);
            return true;
        }

        result = default;
        return false;
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

        switch (value[^1])
        {
            case 'd':
                since = today.AddDays(-count);
                return true;
            case 'w':
                since = today.AddDays(-7 * count);
                return true;
            case 'm':
                since = today.AddMonths(-count);
                return true;
            default:
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

        int daysBack = ((int)today.DayOfWeek - weekday + 7) % 7;
        date = today.AddDays(-daysBack);
        return true;
    }

    private static bool TryParseTime(string value, out TimeOnly time)
        => TimeOnly.TryParseExact(value, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
