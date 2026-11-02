namespace Logbook.Tests.Storage;

/// <summary>
/// Runs an action after a delay on a thread of its own. The retry tests block their thread in Thread.Sleep, and so do
/// the test classes running beside them, so a release queued with Task.Run can wait on thread-pool injection for
/// longer than the whole retry schedule on a small CI runner; a dedicated thread wakes when its sleep ends.
/// </summary>
internal static class Later
{
    public static Thread Run(TimeSpan delay, Action action)
    {
        Thread thread = new(() =>
        {
            Thread.Sleep(delay);
            action();
        })
        {
            IsBackground = true,
        };
        thread.Start();
        return thread;
    }
}
