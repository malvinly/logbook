namespace Logbook.Core.Notifications;

/// <summary>
/// The order tray balloons show in (DESIGN.md section 12): one shows at a time, the next waits until the current one
/// has closed, and each keeps its own click action, so a hotkey failure at startup is not written over by the balloon
/// that follows it. <typeparamref name="T"/> is whatever the app shows and clicks; Core knows nothing of the tray.
/// </summary>
public sealed class BalloonOrder<T>
    where T : class
{
    private readonly Queue<T> _waiting = new();

    /// <summary>The balloon on screen now, or null when none is.</summary>
    public T? Showing { get; private set; }

    /// <summary>
    /// The balloon whose click action is live: the one shown most recently, kept after it closes, because a balloon can
    /// be clicked from the Windows notification list after it has left the screen (DESIGN.md section 10). Null until
    /// the first balloon shows.
    /// </summary>
    public T? LastShown { get; private set; }

    /// <summary>
    /// Adds a balloon. Returns it when nothing is showing, so the caller shows it now; null when it waits its turn.
    /// </summary>
    public T? Enqueue(T balloon)
    {
        if (Showing is not null)
        {
            _waiting.Enqueue(balloon);
            return null;
        }

        Show(balloon);
        return balloon;
    }

    /// <summary>
    /// The balloon on screen has closed. Returns the next one for the caller to show, or null when none is waiting.
    /// A close while nothing is showing changes nothing.
    /// </summary>
    public T? Closed()
    {
        if (_waiting.TryDequeue(out T? next))
        {
            Show(next);
            return next;
        }

        Showing = null;
        return null;
    }

    private void Show(T balloon)
    {
        Showing = balloon;
        LastShown = balloon;
    }
}
