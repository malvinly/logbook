using Logbook.Core;

namespace Logbook.Tests;

/// <summary>A fixed set of entries shared by the query and export tests. Offsets are UTC-5 unless noted.</summary>
internal static class SampleEntries
{
    private static readonly TimeSpan Central = TimeSpan.FromHours(-5);

    public static readonly Entry PrinterOutage = Make("01K0000000000000000000000A", new DateTimeOffset(2026, 7, 14, 9, 0, 0, Central),
        "Reported the printer outage to facilities", null, ["reported", "printer"], ["a-smith"], ["INC0040001"]);

    public static readonly Entry FirstAsk = Make("01K0000000000000000000000B", new DateTimeOffset(2026, 8, 3, 11, 5, 0, Central),
        "Asked for staging access", null, ["asked", "staging-access"], ["j-doe"], []);

    public static readonly Entry ToldAboutOutages = Make("01K0000000000000000000000C", new DateTimeOffset(2026, 8, 20, 16, 45, 0, Central),
        "Told a-smith the outage count so far", "They asked for Numbers before the budget meeting", ["told", "printer"], ["a-smith"], []);

    public static readonly Entry Escalated = Make("01K0000000000000000000000D", new DateTimeOffset(2026, 8, 28, 8, 30, 0, Central),
        "Decided to escalate the printer issue", null, ["decided", "escalated", "printer"], [], []);

    public static readonly Entry SecondAsk = Make("01K0000000000000000000000E", new DateTimeOffset(2026, 9, 9, 13, 0, 0, Central),
        "Followed up on staging access", null, ["asked", "staging-access"], ["j-doe"], []);

    /// <summary>Just after midnight in Tokyo: the previous afternoon in UTC-5 and in UTC.</summary>
    public static readonly Entry TokyoMidnight = Make("01K0000000000000000000000F", new DateTimeOffset(2026, 10, 1, 0, 30, 0, TimeSpan.FromHours(9)),
        "Late night note from the Tokyo trip", null, ["travel"], [], []);

    // The three entries of the docs/export.md sample.
    public static readonly Entry SampleAsk = Make("01K0000000000000000000000G", new DateTimeOffset(2026, 10, 6, 10, 14, 0, Central),
        "Asked for access to the staging environment; told it would be looked into.", null, ["asked", "staging-access"], ["j-doe"], []);

    public static readonly Entry SampleFollowUp = Make("01K0000000000000000000000H", new DateTimeOffset(2026, 10, 15, 14, 30, 0, Central),
        "Followed up; no response to the first ask.", null, ["asked", "staging-access"], ["j-doe"], []);

    public static readonly Entry SampleStandup = Make("01K0000000000000000000000J", new DateTimeOffset(2026, 10, 20, 9, 2, 0, Central),
        "Asked again in standup; told it would be this week.", "wanted it on record before the release freeze.", ["asked", "staging-access"], ["j-doe"], []);

    /// <summary>Every entry, oldest first.</summary>
    public static readonly IReadOnlyList<Entry> All =
        [PrinterOutage, FirstAsk, ToldAboutOutages, Escalated, SecondAsk, TokyoMidnight, SampleAsk, SampleFollowUp, SampleStandup];

    /// <summary>The section 11 sample set, oldest first.</summary>
    public static readonly IReadOnlyList<Entry> Sample = [SampleAsk, SampleFollowUp, SampleStandup];

    public static Entry Make(string id, DateTimeOffset occurredAt, string summary, string? reason, string[] tags, string[] people, string[] refs)
        => new(id, 1, occurredAt, occurredAt.AddSeconds(6), summary, reason, tags, people, refs, [], null, false);
}
