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

        Assert.Same(HotkeyFailed, queue.Closed());
        Assert.Equal("settings", queue.LastShown!.ClickOpens);
        Assert.Same(Downloaded, queue.Closed());
        Assert.Equal("nothing", queue.LastShown!.ClickOpens);
        Assert.Same(Reminder, queue.Closed());
        Assert.Equal("entry-1", queue.LastShown!.ClickOpens);
        Assert.Null(queue.Closed());
    }

    [Fact]
    public void TheCurrentOnesCloseReleasesTheNext()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);
        queue.Enqueue(Reminder);

        Assert.Same(HotkeyFailed, queue.Showing);
        Assert.Same(Reminder, queue.Closed());
        Assert.Same(Reminder, queue.Showing);
    }

    [Fact]
    public void AnEmptyQueueAfterACloseShowsNothing()
    {
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(HotkeyFailed);

        Assert.Null(queue.Closed());
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
        queue.Closed();

        Assert.Same(Reminder, queue.Enqueue(Reminder));
    }

    [Fact]
    public void TheClickActionOutlivesTheBalloonUntilTheNextOneReplacesIt()
    {
        // Windows can take a balloon off the screen and still let it be clicked from its notification list.
        BalloonOrder<Balloon> queue = new();
        queue.Enqueue(Reminder);
        queue.Closed();

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
}
