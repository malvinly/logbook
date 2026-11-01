using Logbook.Core;
using Logbook.Core.Query;
using Xunit;

namespace Logbook.Tests.Query;

/// <summary>Which reminder the tray balloon shows next (DESIGN.md section 10).</summary>
public sealed class ReminderTests
{
    private static readonly TimeSpan Central = TimeSpan.FromHours(-5);
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, Central);

    private static Entry Remind(Entry entry, DateTimeOffset? at) => entry with { RemindAt = at };

    private static Entry? Next(IEnumerable<Entry> live, HashSet<Reminders.Shown>? shown = null)
        => Reminders.NextDue(live, Now, shown ?? []);

    [Fact]
    public void NoRemindersMeansNothingIsDue()
    {
        Assert.Null(Next(SampleEntries.All));
    }

    [Fact]
    public void AFutureReminderIsNotDue()
    {
        Assert.Null(Next([Remind(SampleEntries.SampleAsk, Now.AddMinutes(1))]));
    }

    [Fact]
    public void AReminderIsDueFromItsMomentOn()
    {
        Entry entry = Remind(SampleEntries.SampleAsk, Now);
        Assert.Same(entry, Next([entry]));
    }

    [Fact]
    public void DueIsAnInstantComparisonWhateverTheStoredOffset()
    {
        // 23:30 in Tokyo on Oct 20 is 09:30 in UTC-5: still in the future at 09:00 UTC-5, though its clock reads later in the day.
        Assert.Null(Next([Remind(SampleEntries.SampleAsk, new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.FromHours(9)))]));
        // 22:00 in Tokyo on Oct 20 is 08:00 in UTC-5: passed.
        Assert.NotNull(Next([Remind(SampleEntries.SampleAsk, new DateTimeOffset(2026, 10, 20, 22, 0, 0, TimeSpan.FromHours(9)))]));
    }

    [Fact]
    public void TheEarliestDueReminderComesFirst()
    {
        Entry later = Remind(SampleEntries.SampleAsk, Now.AddHours(-1));
        Entry earlier = Remind(SampleEntries.SampleStandup, Now.AddDays(-2));
        Assert.Same(earlier, Next([later, earlier]));
    }

    [Fact]
    public void RemindersDueAtTheSameInstantComeInIdOrder()
    {
        // The same instant written in two offsets; the lower id wins whatever the input order.
        DateTimeOffset at = Now.AddHours(-1);
        Entry lowerId = Remind(SampleEntries.SampleAsk, at.ToOffset(TimeSpan.FromHours(9)));
        Entry higherId = Remind(SampleEntries.SampleStandup, at);
        Assert.True(string.CompareOrdinal(lowerId.Id, higherId.Id) < 0);

        Assert.Same(lowerId, Next([higherId, lowerId]));
        Assert.Same(lowerId, Next([lowerId, higherId]));
    }

    [Fact]
    public void AReminderAlreadyShownIsSkipped()
    {
        Entry first = Remind(SampleEntries.SampleAsk, Now.AddDays(-2));
        Entry second = Remind(SampleEntries.SampleStandup, Now.AddHours(-1));
        HashSet<Reminders.Shown> shown = [Reminders.Shown.Of(first)];

        Assert.Same(second, Next([first, second], shown));
        shown.Add(Reminders.Shown.Of(second));
        Assert.Null(Next([first, second], shown));
    }

    [Fact]
    public void AMovedReminderIsDueAgain()
    {
        Entry original = Remind(SampleEntries.SampleAsk, Now.AddDays(-2));
        HashSet<Reminders.Shown> shown = [Reminders.Shown.Of(original)];

        Entry moved = original with { Rev = 2, RemindAt = Now.AddMinutes(-5) };
        Assert.Same(moved, Next([moved], shown));
    }

    [Fact]
    public void AnEditThatKeepsTheReminderDoesNotShowItAgain()
    {
        Entry original = Remind(SampleEntries.SampleAsk, Now.AddDays(-2));
        HashSet<Reminders.Shown> shown = [Reminders.Shown.Of(original)];

        Assert.Null(Next([original with { Rev = 2, Summary = "edited" }], shown));
    }
}
