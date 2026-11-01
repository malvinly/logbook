using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Logbook.App.Interop;
using Logbook.Core.Export;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace Logbook.App.Tray;

/// <summary>
/// The tray icon, drawn at runtime from the theme's colors so the repo carries no binary assets. A lock glyph shows
/// while locked (DESIGN.md section 9) and a danger-colored badge while captures are unsaved. NotifyIcon is the one
/// WinForms type in the app; the menu is a WPF ContextMenu.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenu _menu;
    private readonly IntPtr _focusHandle;
    private readonly Icon _unlockedIcon;
    private readonly Icon _lockedIcon;
    private readonly Icon _pendingIcon;
    private bool _locked = true;
    private int _pending;
    private Action? _balloonAction;

    public TrayIcon(IntPtr focusHandle, Color accent, Color muted, Color danger)
    {
        _focusHandle = focusHandle;
        _unlockedIcon = DrawIcon(accent, Glyph.Letter, null);
        _lockedIcon = DrawIcon(muted, Glyph.Padlock, null);
        _pendingIcon = DrawIcon(accent, Glyph.Letter, danger);

        _menu = new ContextMenu();
        _menu.Items.Add(MenuItem("Open Logbook", () => OpenRequested?.Invoke()));
        _menu.Items.Add(MenuItem("New entry", () => CaptureRequested?.Invoke()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(MenuItem("Settings", () => SettingsRequested?.Invoke()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(MenuItem("Exit", () => ExitRequested?.Invoke()));

        _icon = new NotifyIcon { Visible = true };
        _icon.MouseUp += OnMouseUp;
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) => _balloonAction?.Invoke();
        _icon.BalloonTipClosed += (_, _) => _balloonAction = null;
        Redraw();
    }

    public event Action? OpenRequested;

    public event Action? CaptureRequested;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    public void SetLocked(bool locked)
    {
        _locked = locked;
        Redraw();
    }

    /// <summary>The number of captures not yet on disk; the icon and tooltip show it until it is zero again.</summary>
    public void SetPending(int count)
    {
        _pending = count;
        Redraw();
    }

    /// <summary>A balloon from the tray. Each caller decides what a click does; nothing when onClick is null.</summary>
    public void ShowBalloon(string title, string text, bool warning, Action? onClick = null)
    {
        _balloonAction = onClick;
        _icon.ShowBalloonTip(10_000, title, text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    private void Redraw()
    {
        if (_locked)
        {
            _icon.Icon = _lockedIcon;
            _icon.Text = "Logbook (locked)";
        }
        else if (_pending > 0)
        {
            _icon.Icon = _pendingIcon;
            _icon.Text = $"Logbook — {RangeText.Count(_pending)} not saved yet";
        }
        else
        {
            _icon.Icon = _unlockedIcon;
            _icon.Text = "Logbook";
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            // Without a foreground window of our own the menu would not close when the user clicks elsewhere.
            NativeMethods.SetForegroundWindow(_focusHandle);
            _menu.IsOpen = true;
        }
        else if (e.Button == MouseButtons.Left && e.Clicks == 1)
        {
            // The tray icon opens the main window (DESIGN.md section 10); a double-click lands here too and is harmless.
            OpenRequested?.Invoke();
        }
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        MenuItem item = new() { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private enum Glyph
    {
        Letter,
        Padlock,
    }

    // A rounded square with a white glyph; an optional badge in the corner for the pending state.
    private static Icon DrawIcon(Color fill, Glyph glyph, Color? badge)
    {
        const int size = 32;
        using Bitmap bitmap = new(size, size);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using GraphicsPath square = RoundedRect(new Rectangle(1, 1, size - 2, size - 2), 8);
            using SolidBrush background = new(fill);
            g.FillPath(background, square);

            using Pen pen = new(Color.White, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (glyph == Glyph.Padlock)
            {
                g.DrawArc(pen, 10, 6, 12, 12, 180, 180); // shackle
                using SolidBrush body = new(Color.White);
                g.FillRectangle(body, 8, 14, 16, 12);
            }
            else
            {
                g.DrawLine(pen, 11, 7, 11, 24);
                g.DrawLine(pen, 11, 24, 23, 24);
            }

            if (badge is { } badgeColor)
            {
                using SolidBrush badgeBrush = new(badgeColor);
                g.FillEllipse(badgeBrush, 18, 0, 14, 14);
                using Pen ring = new(Color.White, 2);
                g.DrawEllipse(ring, 18, 0, 14, 14);
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            // Clone so the icon owns its bits and the GDI handle can be released.
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        GraphicsPath path = new();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _unlockedIcon.Dispose();
        _lockedIcon.Dispose();
        _pendingIcon.Dispose();
    }
}
