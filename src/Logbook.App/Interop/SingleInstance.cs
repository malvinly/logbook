namespace Logbook.App.Interop;

/// <summary>
/// One named mutex per user session. A second launch posts a registered window message, which the first
/// instance's message window turns into "show the main window", and then exits (docs/settings.md).
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Logbook.SingleInstance";

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static uint ShowMessage { get; } = NativeMethods.RegisterWindowMessageW("Logbook.ShowMainWindow");

    /// <summary>Returns the held mutex when this is the first instance, otherwise null.</summary>
    public static SingleInstance? TryAcquire()
    {
        Mutex mutex = new(initiallyOwned: true, MutexName, out bool createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public static void NotifyFirstInstance()
    {
        // Before the post, so the first instance may take the foreground this launch holds (see NativeMethods).
        NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
        NativeMethods.PostMessageW(NativeMethods.HwndBroadcast, ShowMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
