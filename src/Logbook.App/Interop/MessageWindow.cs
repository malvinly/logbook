using System.Windows.Interop;

namespace Logbook.App.Interop;

/// <summary>
/// A hidden window that exists only to receive WM_HOTKEY, the single-instance message and session changes. Hotkeys and
/// session notifications need an HWND to be registered against, and a tray app has no visible window most of the time
/// (docs/development.md).
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private bool _sessionNotifications;

    public MessageWindow()
    {
        HwndSourceParameters parameters = new("LogbookMessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0, // WS_OVERLAPPED, never shown
        };
        _source = new HwndSource(parameters);
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>Device pixels to device-independent units, for placing windows from monitor coordinates.</summary>
    public System.Windows.Media.Matrix TransformFromDevice => _source.CompositionTarget.TransformFromDevice;

    public void AddHook(HwndSourceHook hook) => _source.AddHook(hook);

    /// <summary>Asks Windows for WM_WTSSESSION_CHANGE (lock, sign-out) for this session. False when it refused.</summary>
    public bool RegisterForSessionNotifications()
    {
        _sessionNotifications = NativeMethods.WTSRegisterSessionNotification(Handle, NativeMethods.NotifyForThisSession);
        return _sessionNotifications;
    }

    public void Dispose()
    {
        if (_sessionNotifications)
        {
            NativeMethods.WTSUnRegisterSessionNotification(Handle);
            _sessionNotifications = false;
        }

        _source.Dispose();
    }
}
