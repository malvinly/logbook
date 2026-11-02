namespace Logbook.Core.Notifications;

/// <summary>
/// The order tray balloons show in (docs/settings.md): one shows at a time, the next waits until the current one
/// has closed, and each keeps its own click action, so a hotkey failure at startup is not written over by the balloon
/// that follows it. <typeparamref name="T"/> is whatever the app shows and clicks; Core knows nothing of the tray.
/// </summary>
public sealed class BalloonOrder<T>
    where T : class
{
    private readonly Queue<T> _waiting = new();
    private bool _onScreen; // Windows has reported the showing balloon as shown

    /// <summary>The balloon on screen now, or null when none is.</summary>
    public T? Showing { get; private set; }

    /// <summary>
    /// The balloon whose click action is live: the one shown most recently, kept after it closes, because a balloon can
    /// be clicked from the Windows notification list after it has left the screen (docs/main-window.md). Null until
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

    /// <summary>Windows reports the showing balloon is on screen. Nothing changes while none is showing.</summary>
    public void Shown()
    {
        if (Showing is not null)
        {
            _onScreen = true;
        }
    }

    /// <summary>
    /// Windows reports a balloon has closed. Returns the next one for the caller to show, or null when none is waiting
    /// or the close does not count. It counts only once the showing balloon has been reported shown: Windows reports
    /// one close twice (hidden, then timed out), and the second arrives after the next balloon has been put up but
    /// before that one shows, so counting it would take the next balloon down at once.
    /// </summary>
    public T? Closed() => _onScreen ? Release() : null;

    /// <summary>
    /// The balloon's timeout plus a margin has passed with no close counted. Windows may never report a close for a
    /// balloon it did not show (notifications off, do not disturb), so one never reported shown is taken as closed and
    /// the next one returned, or null. One reported shown stays: some accessibility settings keep a balloon up well past
    /// its timeout, and its close will still come. Null then too, with <see cref="Showing"/> unchanged, so the caller
    /// keeps waiting until <see cref="Expired"/>.
    /// </summary>
    public T? ReleaseIfNeverShown() => Showing is null || _onScreen ? null : Release();

    /// <summary>
    /// The caller gives up waiting for Windows to report a close (the cap, for a balloon reported shown whose close never
    /// came) and takes the showing balloon as closed, shown or not. Returns the next one to show, or null.
    /// </summary>
    public T? Expired() => Showing is null ? null : Release();

    /// <summary>
    /// Drops every waiting balloon that matches, keeping the rest in order. The one showing, and the click action, are
    /// left alone: the app drops waiting reminders on lock (docs/main-window.md), since they show again after unlock.
    /// </summary>
    public void RemoveWaiting(Func<T, bool> match)
    {
        T[] kept = _waiting.Where(balloon => !match(balloon)).ToArray();
        _waiting.Clear();
        foreach (T balloon in kept)
        {
            _waiting.Enqueue(balloon);
        }
    }

    private T? Release()
    {
        if (_waiting.TryDequeue(out T? next))
        {
            Show(next);
            return next;
        }

        Showing = null;
        _onScreen = false;
        return null;
    }

    private void Show(T balloon)
    {
        Showing = balloon;
        LastShown = balloon;
        _onScreen = false;
    }
}
