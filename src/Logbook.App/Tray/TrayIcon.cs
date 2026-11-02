using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Threading;
using Logbook.App.Interop;
using Logbook.Core.Export;
using Logbook.Core.Notifications;
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
    private const int BalloonTimeoutMilliseconds = 10_000;

    // The balloon queue's two waits for a close that may never be reported (DESIGN.md section 12): 15 seconds after a
    // balloon is put up, then 45 more, so the cap falls 60 seconds after it was put up.
    private static readonly TimeSpan BalloonFallback = TimeSpan.FromMilliseconds(BalloonTimeoutMilliseconds + 5_000);
    private static readonly TimeSpan BalloonCapRemainder = TimeSpan.FromSeconds(60) - BalloonFallback;

    private readonly NotifyIcon _icon;
    private readonly ContextMenu _menu;
    private readonly IntPtr _focusHandle;
    private Icon _unlockedIcon;
    private Icon _lockedIcon;
    private Icon _pendingIcon;
    private bool _locked = true;
    private int _pending;
    private readonly BalloonOrder<Balloon> _balloons = new();
    private readonly DispatcherTimer _balloonFallback;
    private readonly MenuItem _lockItem;

    public TrayIcon(IntPtr focusHandle, Color accent, Color muted, Color danger)
    {
        _focusHandle = focusHandle;
        _unlockedIcon = DrawIcon(accent, Glyph.Letter, null);
        _lockedIcon = DrawIcon(muted, Glyph.Padlock, null);
        _pendingIcon = DrawIcon(accent, Glyph.Letter, danger);

        _menu = new ContextMenu();
        _menu.Items.Add(MenuItem("Open Logbook", () => OpenRequested?.Invoke()));
        _menu.Items.Add(MenuItem("New entry", () => CaptureRequested?.Invoke()));
        _lockItem = MenuItem("Lock now", () => LockRequested?.Invoke());
        _menu.Items.Add(_lockItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(MenuItem("Settings", () => SettingsRequested?.Invoke()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(MenuItem("Exit", () => ExitRequested?.Invoke()));

        _icon = new NotifyIcon { Visible = true };
        _icon.MouseUp += OnMouseUp;
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        // The action stays until the next balloon replaces it, not only while the balloon is up: Windows may time a balloon
        // out at once (do not disturb) or let it be clicked later from the notification list, and a reminder's click
        // must still open its entry then. A click does not release the queue: Windows may follow it with a close of the
        // same balloon, and releasing twice would put the next balloon up and take it down at once.
        _icon.BalloonTipClicked += (_, _) => _balloons.LastShown?.OnClick?.Invoke();
        _icon.BalloonTipShown += (_, _) => _balloons.Shown();
        _icon.BalloonTipClosed += (_, _) => AfterClose(_balloons.Closed());

        // Windows may never report a close for a balloon it did not show (notifications off, do not disturb), and the
        // queue would then wait forever. If no close has come by the balloon's timeout plus a margin, a balloon never
        // reported shown is taken as closed; one reported shown gets until the cap (OnBalloonFallback).
        _balloonFallback = new DispatcherTimer { Interval = BalloonFallback };
        _balloonFallback.Tick += (_, _) => OnBalloonFallback();
        Redraw();
    }

    public event Action? OpenRequested;

    public event Action? CaptureRequested;

    public event Action? LockRequested;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    /// <summary>
    /// Redraws the three icons in the theme's new colors (DESIGN.md section 12) and shows the one for the current state.
    /// The old icons are disposed only after the NotifyIcon has let go of them. The balloon queue is not touched.
    /// </summary>
    public void Recolor(Color accent, Color muted, Color danger)
    {
        Icon oldUnlocked = _unlockedIcon;
        Icon oldLocked = _lockedIcon;
        Icon oldPending = _pendingIcon;
        _unlockedIcon = DrawIcon(accent, Glyph.Letter, null);
        _lockedIcon = DrawIcon(muted, Glyph.Padlock, null);
        _pendingIcon = DrawIcon(accent, Glyph.Letter, danger);
        Redraw();
        oldUnlocked.Dispose();
        oldLocked.Dispose();
        oldPending.Dispose();
    }

    /// <summary>The unlocked "L" badge alone, which every window carries as its icon (DESIGN.md section 16).</summary>
    public static Icon DrawWindowIcon(Color accent) => DrawIcon(accent, Glyph.Letter, null);

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

    /// <summary>
    /// A balloon from the tray. Each caller decides what a click does; nothing when onClick is null. Balloons show one at
    /// a time (DESIGN.md section 12): this one shows now, or once those before it have closed. A reminder balloon still
    /// waiting its turn is dropped on lock (DropWaitingReminders).
    /// </summary>
    public void ShowBalloon(string title, string text, bool warning, Action? onClick = null, bool reminder = false)
    {
        if (_balloons.Enqueue(new Balloon(title, text, warning, onClick, reminder)) is { } now)
        {
            Display(now);
        }
    }

    /// <summary>
    /// Drops the reminder balloons still waiting their turn (DESIGN.md section 10): reminders show again after the next
    /// unlock, so one that came up after the lock would show twice. The balloon on screen and other kinds stay.
    /// </summary>
    public void DropWaitingReminders() => _balloons.RemoveWaiting(balloon => balloon.Reminder);

    private void Display(Balloon balloon)
    {
        _balloonFallback.Stop();
        _icon.ShowBalloonTip(BalloonTimeoutMilliseconds, balloon.Title, balloon.Text, balloon.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
        _balloonFallback.Interval = BalloonFallback;
        _balloonFallback.Start();
    }

    // The timer's interval says which wait just ended: the first, at 15 seconds, or the cap, 45 seconds later. Display
    // starts every balloon on the first, so a close that releases the next one between the two never carries the cap over.
    private void OnBalloonFallback()
    {
        if (_balloonFallback.Interval == BalloonCapRemainder)
        {
            AfterClose(_balloons.Expired());
            return;
        }

        Balloon? next = _balloons.ReleaseIfNeverShown();
        if (next is null && _balloons.Showing is not null)
        {
            // Windows reported it shown, so it may still be on screen: wait for its close, up to the cap.
            _balloonFallback.Stop();
            _balloonFallback.Interval = BalloonCapRemainder;
            _balloonFallback.Start();
            return;
        }

        AfterClose(next);
    }

    // A close that did not count (BalloonOrder.Closed says when) leaves the balloon up and the fallback running.
    private void AfterClose(Balloon? next)
    {
        if (next is not null)
        {
            Display(next);
        }
        else if (_balloons.Showing is null)
        {
            _balloonFallback.Stop();
        }
    }

    private sealed record Balloon(string Title, string Text, bool Warning, Action? OnClick, bool Reminder);

    private void Redraw()
    {
        _lockItem.IsEnabled = !_locked;
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
        _balloonFallback.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _unlockedIcon.Dispose();
        _lockedIcon.Dispose();
        _pendingIcon.Dispose();
    }
}
