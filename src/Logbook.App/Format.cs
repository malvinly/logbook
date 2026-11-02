using System.Globalization;

namespace Logbook.App;

/// <summary>The fixed US-style date and time patterns the windows use, matching the export (docs/development.md).</summary>
public static class Format
{
    public static string DateTime(DateTimeOffset value) => value.ToString("ddd MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset value) => value.ToString("h:mm tt", CultureInfo.InvariantCulture);

    public static string DayHeader(DateOnly value) => value.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateOnly value) => value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}
