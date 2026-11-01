using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Logbook.App.Interop;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace Logbook.App.Tray;

/// <summary>
/// The tray icon, drawn at runtime so the repo carries no binary assets. A lock glyph shows while locked
/// (DESIGN.md section 9). NotifyIcon is the one WinForms type in the app; the menu is a WPF ContextMenu.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenu _menu;
    private readonly IntPtr _focusHandle;
    private Icon _unlockedIcon;
    private Icon _lockedIcon;

    public TrayIcon(IntPtr focusHandle)
    {
        _focusHandle = focusHandle;
        _unlockedIcon = DrawIcon(locked: false);
        _lockedIcon = DrawIcon(locked: true);

        _menu = new ContextMenu();
        _menu.Items.Add(MenuItem("Open Logbook", () => OpenRequested?.Invoke()));
        _menu.Items.Add(MenuItem("New entry", () => CaptureRequested?.Invoke()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(MenuItem("Settings", () => SettingsRequested?.Invoke()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(MenuItem("Exit", () => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = _lockedIcon,
            Text = "Logbook (locked)",
            Visible = true,
        };
        _icon.MouseUp += OnMouseUp;
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke();
    }

    public event Action? OpenRequested;

    public event Action? CaptureRequested;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    public event Action? BalloonClicked;

    public void SetLocked(bool locked)
    {
        _icon.Icon = locked ? _lockedIcon : _unlockedIcon;
        _icon.Text = locked ? "Logbook (locked)" : "Logbook";
    }

    public void ShowBalloon(string title, string text, bool warning)
        => _icon.ShowBalloonTip(10_000, title, text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            // Without a foreground window of our own the menu would not close when the user clicks elsewhere.
            NativeMethods.SetForegroundWindow(_focusHandle);
            _menu.IsOpen = true;
        }
        else if (e.Button == MouseButtons.Left)
        {
            CaptureRequested?.Invoke();
        }
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        MenuItem item = new() { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    // A rounded square; a white "L" when unlocked, a white padlock when locked.
    private static Icon DrawIcon(bool locked)
    {
        const int size = 32;
        using Bitmap bitmap = new(size, size);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using GraphicsPath square = RoundedRect(new Rectangle(1, 1, size - 2, size - 2), 8);
            using SolidBrush fill = new(locked ? Color.FromArgb(0x6E, 0x6E, 0x78) : Color.FromArgb(0x2F, 0x6F, 0xED));
            g.FillPath(fill, square);

            using Pen pen = new(Color.White, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (locked)
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
        _unlockedIcon = null!;
        _lockedIcon = null!;
    }
}
