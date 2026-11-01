using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Logbook.App.Capture;

/// <summary>
/// The "Logged — click to edit" toast (DESIGN.md sections 5 and 16): a non-activating topmost window at the bottom
/// right of the work area that fades out after five seconds. The same window, with an error glyph, reports an unsaved capture.
/// </summary>
public sealed partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private bool _fading;
    private bool _closed;

    public ToastWindow(Rect workArea, string headline, string detail, bool danger, TimeSpan lifetime)
    {
        InitializeComponent();
        Headline.Text = headline;
        Detail.Text = detail;
        Detail.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (danger)
        {
            StatusGlyph.Text = "\uEA39"; // error badge in place of the check mark
            StatusGlyph.SetResourceReference(ForegroundProperty, "DangerBrush");
        }

        // The surface keeps a margin inside the window for its shadow, which also spaces it from the screen edges.
        Left = workArea.Right - Width;
        Loaded += (_, _) => Top = workArea.Bottom - ActualHeight;

        _timer.Interval = lifetime;
        _timer.Tick += (_, _) => FadeOut();
        _timer.Start();
    }

    /// <summary>Raised when the user clicks the toast, before it closes.</summary>
    public event Action? Clicked;

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        // Clicked can open the modal unlock prompt, and while it is up the fade or a lock may close this toast.
        Clicked?.Invoke();
        if (!_closed)
        {
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _timer.Stop();
        base.OnClosed(e);
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _timer.Stop();
        if (_fading)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            _fading = false;
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e) => _timer.Start();

    private void FadeOut()
    {
        _timer.Stop();
        _fading = true;
        DoubleAnimation fade = new(1, 0, TimeSpan.FromMilliseconds(600)) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) =>
        {
            if (_fading && !_closed)
            {
                Close();
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
