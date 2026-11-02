using Logbook.Core.Notifications;
using Xunit;

namespace Logbook.Tests.Notifications;

/// <summary>Tray balloons show one at a time, in order, each with its own click action (DESIGN.md sections 10 and 12).</summary>
public sealed class BalloonOrderTests
{
    private sealed record Balloon(string Title, string ClickOpens);

    private static readonly Balloon HotkeyFailed = new("Hotkey not set", "settings");
    private static readonly Balloon Downloaded = new("Downloaded from sync", "nothing");
    private static readonly Balloon Reminder = new("Reminder", "entry-1");

    // What Windows reports for a balloon that shows and then times out, as far as the order cares.
    private static Balloon? ShownThenClosed(BalloonOrder<Balloon> queue)
    {
        queue.Shown();
        return queue.Closed();
    }

    [Fact]
    public void ABalloonEnqueuedWhileNothingIsShowingShowsAtOnce()
    {
        BalloonOrder<Balloon> queue = new();

        Assert.Same(HotkeyFailed, queue.Enqueue(HotkeyFailed));
        Assert.Same(HotkeyFailed, queue.Showing);
        Assert.Same(HotkeyFailed, queue.LastShown);
    }

    [Fact]
    public void ABalloonEnqueuedWhileOneIsShowingWaits()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);

        Assert.Null(queue.Enqueue(Downloaded));
        Assert.Same(HotkeyFailed, queue.Showing);
        Assert.Same(HotkeyFailed, queue.LastShown);
    }

    [Fact]
    public void QueuedBalloonsShowInOrderOneAtATimeEachWithItsOwnPayload()
    {
        BalloonOrder<Balloon> queue = new();
        Balloon first = new("First", "a");
        queue.Enqueue(first);
        queue.Enqueue(HotkeyFailed);
        queue.Enqueue(Downloaded);
        queue.Enqueue(Reminder);

        Assert.Same(HotkeyFailed, ShownThenClosed(queue));
        Assert.Equal("settings", queue.LastShown!.ClickOpens);
        Assert.Same(Downloaded, ShownThenClosed(queue));
        Assert.Equal("nothing", queue.LastShown!.ClickOpens);
        Assert.Same(Reminder, ShownThenClosed(queue));
        Assert.Equal("entry-1", queue.LastShown!.ClickOpens);
        Assert.Null(ShownThenClosed(queue));
    }

    [Fact]
    public void TheCurrentOnesCloseReleasesTheNext()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);
        queue.Enqueue(Reminder);

        Assert.Same(HotkeyFailed, queue.Showing);
        Assert.Same(Reminder, ShownThenClosed(queue));
        Assert.Same(Reminder, queue.Showing);
    }

    [Fact]
    public void AnEmptyQueueAfterACloseShowsNothing()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);

        Assert.Null(ShownThenClosed(queue));
        Assert.Null(queue.Showing);
    }

    [Fact]
    public void ACloseWhileNothingIsShowingChangesNothing()
    {
        BalloonOrder<Balloon> queue = new();

        Assert.Null(queue.Closed());
        Assert.Null(queue.Showing);
        Assert.Null(queue.LastShown);
    }

    [Fact]
    public void AfterTheQueueEmptiesTheNextBalloonShowsAtOnceAgain()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);
        ShownThenClosed(queue);

        Assert.Same(Reminder, queue.Enqueue(Reminder));
    }

    [Fact]
    public void TheClickActionOutlivesTheBalloonUntilTheNextOneReplacesIt()
    {
        // Windows can take a balloon off the screen and still let it be clicked from its notification list.
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(Reminder);
        ShownThenClosed(queue);

        Assert.Null(queue.Showing);
        Assert.Same(Reminder, queue.LastShown);

        queue.Enqueue(HotkeyFailed);
        Assert.Same(HotkeyFailed, queue.LastShown);
    }

    [Fact]
    public void AWaitingBalloonDoesNotTakeOverTheClickActionBeforeItShows()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(Reminder);
        queue.Enqueue(HotkeyFailed);

        Assert.Same(Reminder, queue.LastShown);
    }

    [Fact]
    public void ASecondCloseForTheSameBalloonDoesNotTakeTheNextOneDown()
    {
        // Windows reports one balloon's close twice (hidden, then timed out); the second arrives after the next balloon
        // was put up but before Windows reports it shown.
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);
        queue.Enqueue(Downloaded);
        queue.Enqueue(Reminder);
        queue.Shown();

        Assert.Same(Downloaded, queue.Closed());
        Assert.Null(queue.Closed());
        Assert.Same(Downloaded, queue.Showing);

        queue.Shown();
        Assert.Same(Reminder, queue.Closed());
    }

    [Fact]
    public void ACloseBeforeTheBalloonIsReportedShownDoesNotCount()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);
        queue.Enqueue(Reminder);

        Assert.Null(queue.Closed());
        Assert.Same(HotkeyFailed, queue.Showing);
    }

    [Fact]
    public void ExpiredReleasesTheNextEvenIfWindowsNeverReportedTheBalloonShown()
    {
        // Notifications off or do not disturb: Windows may report neither a show nor a close.
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);
        queue.Enqueue(Reminder);

        Assert.Same(Reminder, queue.Expired());
        Assert.Same(Reminder, queue.Showing);
        Assert.Same(Reminder, queue.LastShown);
    }

    [Fact]
    public void ExpiredWithNothingWaitingLeavesNothingShowingButKeepsTheClickAction()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(Reminder);

        Assert.Null(queue.Expired());
        Assert.Null(queue.Showing);
        Assert.Same(Reminder, queue.LastShown);
    }

    [Fact]
    public void ExpiredWhileNothingIsShowingChangesNothing()
    {
        BalloonOrder<Balloon> queue = new();

        Assert.Null(queue.Expired());
        Assert.Null(queue.Showing);
    }

    [Fact]
    public void ALateCloseAfterTheQueueEmptiedChangesNothing()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(Reminder);
        queue.Expired();

        Assert.Null(queue.Closed());
        Assert.Same(HotkeyFailed, queue.Enqueue(HotkeyFailed));
    }

    [Fact]
    public void AShownReportWhileNothingIsShowingDoesNotLetTheNextBalloonsFirstCloseCount()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Shown();
        queue.Enqueue(HotkeyFailed);
        queue.Enqueue(Reminder);

        Assert.Null(queue.Closed());
        Assert.Same(HotkeyFailed, queue.Showing);
    }

    [Fact]
    public void ReleaseIfNeverShownReleasesTheNextWhenWindowsNeverReportedTheBalloonShown()
    {
        // Notifications off or do not disturb: the balloon was probably never on screen, so waiting longer helps nobody.
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);
        queue.Enqueue(Reminder);

        Assert.Same(Reminder, queue.ReleaseIfNeverShown());
        Assert.Same(Reminder, queue.Showing);
        Assert.Same(Reminder, queue.LastShown);
    }

    [Fact]
    public void ReleaseIfNeverShownKeepsABalloonWindowsReportedShown()
    {
        // Some accessibility settings keep a balloon up past its timeout; replacing it while still on screen is worse.
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);
        queue.Enqueue(Reminder);
        queue.Shown();

        Assert.Null(queue.ReleaseIfNeverShown());
        Assert.Same(HotkeyFailed, queue.Showing);
        Assert.Same(HotkeyFailed, queue.LastShown);
    }

    [Fact]
    public void ReleaseIfNeverShownWithNothingWaitingLeavesNothingShowingButKeepsTheClickAction()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(Reminder);

        Assert.Null(queue.ReleaseIfNeverShown());
        Assert.Null(queue.Showing);
        Assert.Same(Reminder, queue.LastShown);
    }

    [Fact]
    public void ReleaseIfNeverShownWhileNothingIsShowingChangesNothing()
    {
        BalloonOrder<Balloon> queue = new();

        Assert.Null(queue.ReleaseIfNeverShown());
        Assert.Null(queue.Showing);
        Assert.Null(queue.LastShown);
    }

    [Fact]
    public void TheCapReleasesTheNextEvenWhenTheBalloonWasReportedShown()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);
        queue.Enqueue(Reminder);
        queue.Shown();
        queue.ReleaseIfNeverShown();

        Assert.Same(Reminder, queue.Expired());
        Assert.Same(Reminder, queue.Showing);
    }

    [Fact]
    public void ACloseBetweenTheFallbackAndTheCapReleasesTheNextWhichStartsItsOwnWait()
    {
        // The next balloon starts unreported, so the cap meant for the one before cannot count it as shown, and its own
        // fallback waits for its close once Windows reports it shown.
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);
        queue.Enqueue(Downloaded);
        queue.Enqueue(Reminder);
        queue.Shown();
        queue.ReleaseIfNeverShown();

        Assert.Same(Downloaded, queue.Closed());
        Assert.Same(Downloaded, queue.Showing);

        queue.Shown();
        Assert.Null(queue.ReleaseIfNeverShown());
        Assert.Same(Downloaded, queue.Showing);
        Assert.Same(Reminder, queue.Closed());
    }

    [Fact]
    public void ACloseBetweenTheFallbackAndTheCapWithNothingWaitingLeavesTheCapNothingToRelease()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);
        queue.Shown();
        queue.ReleaseIfNeverShown();
        queue.Closed();

        Assert.Null(queue.Expired());
        Assert.Null(queue.Showing);
        Assert.Same(Reminder, queue.Enqueue(Reminder));
    }
}
