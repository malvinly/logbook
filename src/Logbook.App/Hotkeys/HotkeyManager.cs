using System.Windows.Input;
using Logbook.App.Interop;

namespace Logbook.App.Hotkeys;

/// <summary>Registers global hotkeys against the message window and dispatches WM_HOTKEY to their actions.</summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 1;

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        _window.AddHook(OnMessage);
    }

    /// <summary>Returns false when Windows refused the chord, usually because another app owns it.</summary>
    public bool TryRegister(Hotkey hotkey, Action action)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        uint modifiers = NativeMethods.ModNoRepeat;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= NativeMethods.ModControl;
        }

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= NativeMethods.ModAlt;
        }

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= NativeMethods.ModShift;
        }

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= NativeMethods.ModWin;
        }

        int id = _nextId++;
        uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
        if (!NativeMethods.RegisterHotKey(_window.Handle, id, modifiers, virtualKey))
        {
            return false;
        }

        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _actions.Keys)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        }

        _actions.Clear();
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out Action? action))
        {
            handled = true;
            action();
        }

        return IntPtr.Zero;
    }

    public void Dispose() => UnregisterAll();
}
