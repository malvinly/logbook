using System.Windows.Interop;

namespace Logbook.App.Interop;

/// <summary>
/// A hidden window that exists only to receive WM_HOTKEY and the single-instance message. Hotkeys need an HWND
/// to be registered against, and a tray app has no visible window most of the time (DESIGN.md section 16).
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

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

    public void Dispose() => _source.Dispose();
}
