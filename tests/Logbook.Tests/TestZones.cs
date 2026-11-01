namespace Logbook.Tests;

/// <summary>
/// Time zones built in code, so tests that depend on a zone's offsets do not depend on the machine's zone or its
/// time-zone database.
/// </summary>
internal static class TestZones
{
    /// <summary>A zone that is always at <paramref name="offset"/>: no clock changes.</summary>
    public static TimeZoneInfo Fixed(TimeSpan offset)
        => TimeZoneInfo.CreateCustomTimeZone($"Fixed {offset}", offset, $"Fixed {offset}", $"Fixed {offset}");

    /// <summary>
    /// UTC-6 standard, UTC-5 daylight, clocks forward at 02:00 on the second Sunday of March and back at 02:00 on the
    /// first Sunday of November. In 2026 that is Sunday 8 March and Sunday 1 November.
    /// </summary>
    public static readonly TimeZoneInfo Central = CreateCentral();

    private static TimeZoneInfo CreateCentral()
    {
        DateTime twoAm = new(1, 1, 1, 2, 0, 0);
        TimeZoneInfo.TransitionTime forward = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(twoAm, 3, 2, DayOfWeek.Sunday);
        TimeZoneInfo.TransitionTime back = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(twoAm, 11, 1, DayOfWeek.Sunday);
        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), forward, back);
        return TimeZoneInfo.CreateCustomTimeZone("Test Central", TimeSpan.FromHours(-6), "Test Central", "Test Standard", "Test Daylight", [rule]);
    }
}
